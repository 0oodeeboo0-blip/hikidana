using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Paste;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>
/// クリップボードの変化を見張る。専用のスレッドに見えないウインドウ（メッセージ専用）を作り、
/// Windows からの知らせ（WM_CLIPBOARDUPDATE）を受けて中身を読む。
/// クリップボードへの書き込みも、このスレッドで行う（<see cref="WriteAsync"/>）。
/// </summary>
public sealed unsafe class ClipboardWatcher : IDisposable
{
    /// <summary>知らせを受けてから読むまでの待ち時間。アプリは何回かに分けて書くことがあるので、落ち着くのを待つ。</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private const string ClassName = "HikidanaClipboardWatcher";
    private const uint WmInvoke = NativeMethods.WM_APP + 1;
    private const nuint ReadTimer = 1;
    private const uint BusyRetryMilliseconds = 100;
    private const int MaxBusyRetries = 12;
    private static readonly ConcurrentDictionary<nint, ClipboardWatcher> Windows = new();
    private static readonly Lock ClassGate = new();
    private static nint classNamePointer;

    private readonly ConcurrentQueue<WorkItem> work = new();
    private readonly ManualResetEventSlim ready = new();
    private Thread? thread;
    private volatile nint hwnd;
    private volatile uint lastSequence;
    private volatile bool paused;
    private volatile IReadOnlyCollection<string> excludedApps = [];
    private int busyRetries;
    private Exception? startError;

    /// <summary>取り込める中身が来た（スレッドプールのスレッドで呼ばれる）。</summary>
    public event Action<ClipboardSnapshot>? Captured;

    /// <summary>取り込まなかった（理由つき。スレッドプールのスレッドで呼ばれる）。</summary>
    public event Action<SkipReason>? Skipped;

    /// <summary>記録しないアプリ（実行ファイルの名前）。</summary>
    public IReadOnlyCollection<string> ExcludedApps
    {
        get => excludedApps;
        set => excludedApps = value ?? [];
    }

    /// <summary>記録を止めている。</summary>
    public bool Paused
    {
        get => paused;
        set => paused = value;
    }

    /// <summary>見えないウインドウ。書いたときのクリップボードの持ち主になる。</summary>
    public nint Handle => hwnd;

    /// <summary>見張っている。</summary>
    public bool IsRunning => hwnd != 0;

    /// <summary>見張りを始める（見えないウインドウができるまで待つ）。</summary>
    public void Start()
    {
        if (thread is not null)
        {
            return;
        }

        thread = new Thread(Run) { IsBackground = true, Name = "Hikidana clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (startError is not null)
        {
            throw new InvalidOperationException("クリップボードを見張れませんでした。", startError);
        }
    }

    /// <summary>
    /// クリップボードに書いて、読み戻して確かめる。書いた変化は取り込まない。
    /// 見張りのスレッドで動かす（そのスレッドの見えないウインドウが持ち主になる）。
    /// </summary>
    public Task<WriteStatus> WriteAsync(IReadOnlyList<OutgoingFormat> formats) =>
        InvokeAsync(h =>
        {
            var status = ClipboardAccess.WriteAndVerify(h, formats, out var sequence);
            if (status == WriteStatus.Ok)
            {
                lastSequence = sequence;
            }

            return status;
        });

    /// <summary>見張りのスレッドで動かす。引数は見えないウインドウ。</summary>
    public Task<T> InvokeAsync<T>(Func<nint, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(
            h =>
            {
                try
                {
                    done.TrySetResult(action(h));
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            },
            ex => done.TrySetException(ex));
        work.Enqueue(item);
        var h = hwnd;
        if ((h == 0 || !NativeMethods.PostMessage(h, WmInvoke, 0, 0)) && item.TryTake())
        {
            item.Fail(new InvalidOperationException("クリップボードの見張りが動いていません。"));
        }

        return done.Task;
    }

    public void Dispose()
    {
        var h = hwnd;
        if (h != 0)
        {
            NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, 0, 0);
            thread?.Join(TimeSpan.FromSeconds(2));
        }

        FailPending();
    }

    private void Run()
    {
        try
        {
            var instance = NativeMethods.GetModuleHandle(null);
            lock (ClassGate)
            {
                if (classNamePointer == 0)
                {
                    var name = Marshal.StringToHGlobalUni(ClassName);
                    var wc = new NativeMethods.WNDCLASSEXW
                    {
                        cbSize = (uint)sizeof(NativeMethods.WNDCLASSEXW),
                        lpfnWndProc = &WndProc,
                        hInstance = instance,
                        lpszClassName = (char*)name,
                    };
                    if (NativeMethods.RegisterClassEx(&wc) == 0)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        Marshal.FreeHGlobal(name);
                        throw new InvalidOperationException($"ウインドウの種類を登録できませんでした（エラー {error}）。");
                    }

                    // 登録した名前は、プロセスが終わるまで使う
                    classNamePointer = name;
                }
            }

            var window = NativeMethods.CreateWindowEx(0, ClassName, "Hikidana", 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, instance, 0);
            if (window == 0)
            {
                throw new InvalidOperationException($"見えないウインドウを作れませんでした（エラー {Marshal.GetLastPInvokeError()}）。");
            }

            Windows[window] = this;
            lastSequence = NativeMethods.GetClipboardSequenceNumber();
            if (!NativeMethods.AddClipboardFormatListener(window))
            {
                var error = Marshal.GetLastPInvokeError();
                Windows.TryRemove(window, out _);
                NativeMethods.DestroyWindow(window);
                throw new InvalidOperationException($"クリップボードの知らせを受けられませんでした（エラー {error}）。");
            }

            hwnd = window;
        }
        catch (Exception ex)
        {
            startError = ex;
            ready.Set();
            return;
        }

        ready.Set();
        NativeMethods.MSG msg;
        while (NativeMethods.GetMessage(&msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(&msg);
            NativeMethods.DispatchMessage(&msg);
        }

        FailPending();
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (Windows.TryGetValue(hwnd, out var self))
        {
            try
            {
                switch (msg)
                {
                    case NativeMethods.WM_CLIPBOARDUPDATE:
                        self.OnClipboardUpdate(hwnd);
                        return 0;
                    case NativeMethods.WM_TIMER when (nuint)wParam == ReadTimer:
                        NativeMethods.KillTimer(hwnd, ReadTimer);
                        self.ReadNow(hwnd);
                        return 0;
                    case WmInvoke:
                        while (self.work.TryDequeue(out var item))
                        {
                            if (item.TryTake())
                            {
                                item.Run(hwnd);
                            }
                        }

                        return 0;
                    case NativeMethods.WM_CLOSE:
                        NativeMethods.RemoveClipboardFormatListener(hwnd);
                        NativeMethods.DestroyWindow(hwnd);
                        return 0;
                    case NativeMethods.WM_DESTROY:
                        Windows.TryRemove(hwnd, out _);
                        self.hwnd = 0;
                        NativeMethods.PostQuitMessage(0);
                        return 0;
                }
            }
            catch (Exception ex)
            {
                // ここから例外を外へ出すとアプリごと落ちるので、止めて記録だけする
                System.Diagnostics.Trace.WriteLine($"[Hikidana] クリップボードの見張りで例外: {ex}");
                return 0;
            }
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void OnClipboardUpdate(nint window)
    {
        if (NativeMethods.GetClipboardSequenceNumber() == lastSequence)
        {
            // 自分が書いた変化
            return;
        }

        busyRetries = 0;
        NativeMethods.SetTimer(window, ReadTimer, (uint)SettleDelay.TotalMilliseconds, 0);
    }

    private void ReadNow(nint window)
    {
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == lastSequence)
        {
            return;
        }

        if (paused)
        {
            lastSequence = sequence;
            RaiseSkipped(SkipReason.Paused);
            return;
        }

        var result = ClipboardAccess.Read(window, excludedApps);
        if (result.Busy)
        {
            // ほかのアプリが開いたまま。少し待って読み直す
            if (++busyRetries <= MaxBusyRetries)
            {
                NativeMethods.SetTimer(window, ReadTimer, BusyRetryMilliseconds, 0);
            }

            return;
        }

        lastSequence = result.Snapshot is { Sequence: not 0 } read ? read.Sequence : sequence;
        if (result.Snapshot is { } snapshot)
        {
            var handler = Captured;
            if (handler is not null)
            {
                ThreadPool.QueueUserWorkItem(_ => handler(snapshot));
            }
        }
        else
        {
            RaiseSkipped(result.Reason);
        }
    }

    private void RaiseSkipped(SkipReason reason)
    {
        var handler = Skipped;
        if (handler is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => handler(reason));
        }
    }

    private void FailPending()
    {
        while (work.TryDequeue(out var item))
        {
            if (item.TryTake())
            {
                item.Fail(new ObjectDisposedException(nameof(ClipboardWatcher), "クリップボードの見張りは止まっています。"));
            }
        }
    }

    /// <summary>見張りのスレッドで動かす仕事。動かすか、失敗にするかの、どちらか一度だけ。</summary>
    private sealed class WorkItem(Action<nint> run, Action<Exception> fail)
    {
        private int taken;

        public bool TryTake() => Interlocked.Exchange(ref taken, 1) == 0;

        public void Run(nint window) => run(window);

        public void Fail(Exception error) => fail(error);
    }
}
