using System.Runtime.InteropServices;
using System.Windows.Threading;
using Hikidana.Core.Diagnostics;

namespace Hikidana.App.Interop;

/// <summary>
/// 棚が開いている間だけ付ける、マウスとキーボードの低レベルの見張り（WH_MOUSE_LL と WH_KEYBOARD_LL）。
/// 棚はフォーカスを取らないので、作業中のアプリ（ChatGPT など）をそのままクリックしても、Windows からは何も知らされない
/// （前面のアプリが変わらないため）。そこで、棚の外のクリックと Esc を、ここで見つけて知らせる。
/// <list type="bullet">
/// <item>クリック: 押してから離すまで、ずっと棚の外なら「外のクリック」。クリックは止めずに、そのままアプリへ届ける。
/// このアプリのウインドウ（棚・メニュー・知らせ・設定など）と、タスクバー（タスクトレイのアイコンで開け閉めする）は外に数えない。</item>
/// <item>Esc: ほかのアプリが前面のときに、何も一緒に押さずに押したら知らせる。このときの Esc はアプリへ渡さない。</item>
/// </list>
/// 見張りは、専用のスレッドで付けて、そこで呼ばれる。画面のスレッドが重い仕事をしている間に呼ばれると、Windows は待ちきれずに
/// 見張りを黙って外してしまう（LowLevelHooksTimeout）ので、画面のスレッドでは付けない。
/// 見つけたクリックは画面のスレッドへ送り、そこで「いま数えてよいか」（メニューが開いているなど）を決める。
/// キーは Esc かどうかだけを見て、ほかは何も覚えない・書かない。
/// </summary>
internal sealed unsafe partial class DismissWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const uint WM_APP_STOP = 0x8000 + 0x51;
    private const uint PM_NOREMOVE = 0x0000;
    private const uint VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const uint GA_ROOT = 2;

    /// <summary>タスクバーとタスクトレイ。ここのクリックは外に数えない（タスクトレイのアイコンで開け閉めするため）。</summary>
    private static readonly string[] TaskbarClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];

    /// <summary>見張りの関数は static なので、そのスレッドの見張りをここで覚える（見張りは、付けたスレッドで呼ばれる）。</summary>
    [ThreadStatic]
    private static HookThread? current;

    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private HookThread? running;

    /// <summary>いまのクリックを、押したときに数えてよかったか（画面のスレッドだけで使う）。</summary>
    private bool downCounts;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public Native.POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Native.POINT Point;
        public uint Private;
    }

    /// <summary>棚の外をクリックした（画面のスレッドで、少しあとに呼ばれる）。</summary>
    public event Action? ClickedOutside;

    /// <summary>ほかのアプリを使っている間に、Esc が押された（画面のスレッドで、少しあとに呼ばれる）。</summary>
    public event Action? EscapePressed;

    /// <summary>いまのクリックを数えない（メニューやダイアログを開いている、ドラッグしているなど）なら true。画面のスレッドで呼ぶ。</summary>
    public Func<bool> IsBusy { get; init; } = static () => false;

    /// <summary>
    /// Esc を棚が受け取ってよいなら true（開いたままにしているときは false にして、Esc をアプリへ渡す）。
    /// 見張りのスレッドで呼ぶので、画面の部品には触らず、覚えている値を読むだけにする。
    /// </summary>
    public Func<bool> TakesEscape { get; init; } = static () => true;

    public bool IsWatching => running is not null;

    /// <summary>見張りを付ける（棚を開いたとき）。</summary>
    public void Start()
    {
        if (running is not null)
        {
            return;
        }

        downCounts = false;
        running = new HookThread(this);
        running.Start();
    }

    /// <summary>見張りを外す（棚を閉じたとき）。</summary>
    public void Stop()
    {
        running?.Stop();
        running = null;
        downCounts = false;
    }

    public void Dispose() => Stop();

    /// <summary>押した（画面のスレッド）。このときメニューが開いていたら、そのクリックはメニューを閉じるだけにする。</summary>
    private void OnDown(HookThread from, bool outside)
    {
        if (ReferenceEquals(from, running))
        {
            downCounts = outside && !IsBusy();
        }
    }

    /// <summary>離した（画面のスレッド）。押したときも離したときも外なら、外のクリック。</summary>
    private void OnUp(HookThread from, bool outside)
    {
        if (!ReferenceEquals(from, running))
        {
            return;
        }

        var counts = downCounts && outside;
        downCounts = false;
        if (counts)
        {
            ClickedOutside?.Invoke();
        }
    }

    private void OnEscape(HookThread from)
    {
        if (ReferenceEquals(from, running))
        {
            EscapePressed?.Invoke();
        }
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && current is { } hooks)
            {
                hooks.OnMouse((int)message, ((MouseHookData*)data)->Point);
            }
        }
        catch (Exception ex)
        {
            // ここで例外を外へ出すと、アプリが落ちる
            Log.Warn("棚の外のクリックの見張りで例外", ex);
        }

        return CallNextHookEx(0, code, message, data);
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && current is { } hooks && hooks.OnKey((int)message, ((KeyboardHookData*)data)->VirtualKey))
            {
                // 棚を閉じるのに使った Esc は、アプリへ渡さない
                return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Esc の見張りで例外", ex);
        }

        return CallNextHookEx(0, code, message, data);
    }

    /// <summary>その点が、このアプリのウインドウでもタスクバーでもなければ true。</summary>
    private static bool IsOutside(Native.POINT point)
    {
        var window = WindowFromPoint(point);
        if (window == 0)
        {
            return true;
        }

        var root = GetAncestor(window, GA_ROOT);
        if (root == 0)
        {
            root = window;
        }

        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        var buffer = stackalloc char[64];
        var length = GetClassName(root, buffer, 64);
        var className = length > 0 ? new string(buffer, 0, length) : string.Empty;
        return Array.IndexOf(TaskbarClasses, className) < 0;
    }

    private static bool ForegroundIsOwn()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static bool AnyModifierHeld() =>
        GetAsyncKeyState(VK_SHIFT) < 0 || GetAsyncKeyState(VK_CONTROL) < 0 || GetAsyncKeyState(VK_MENU) < 0
        || GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0;

    /// <summary>
    /// 見張りを付けて、Windows からの呼び出しを受けるだけのスレッド（棚を開くたびに 1 つ作り、閉じたら終わる）。
    /// ここでは軽いことだけをして、見つけたことは画面のスレッドへ送る。
    /// </summary>
    private sealed class HookThread
    {
        private readonly DismissWatcher owner;
        private readonly Thread thread;
        private readonly ManualResetEventSlim ready = new();
        private int threadId;
        private volatile bool stopped;
        private nint mouseHook;
        private nint keyboardHook;
        private bool swallowEscapeUp;

        public HookThread(DismissWatcher owner)
        {
            this.owner = owner;
            thread = new Thread(Run) { IsBackground = true, Name = "Hikidana の外のクリックの見張り" };
        }

        public void Start()
        {
            thread.Start();

            // 見張りが付くまで少しだけ待つ（棚が開いてすぐのクリックも拾えるように）
            ready.Wait(TimeSpan.FromSeconds(1));
        }

        public void Stop()
        {
            stopped = true;
            var id = Volatile.Read(ref threadId);
            if (id != 0)
            {
                PostThreadMessage((uint)id, WM_APP_STOP, 0, 0);
            }
        }

        public void OnMouse(int message, Native.POINT point)
        {
            switch (message)
            {
                case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN:
                {
                    // 画面のスレッドへの知らせは、このクリックがアプリに届くより前に並ぶ（メニューが閉じる前の様子で決められる）
                    var outside = IsOutside(point);
                    owner.dispatcher.BeginInvoke(() => owner.OnDown(this, outside));
                    break;
                }

                case WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP:
                {
                    var outside = IsOutside(point);
                    owner.dispatcher.BeginInvoke(() => owner.OnUp(this, outside));
                    break;
                }
            }
        }

        /// <returns>アプリへ渡さないなら true。</returns>
        public bool OnKey(int message, uint key)
        {
            if (key != VK_ESCAPE)
            {
                return false;
            }

            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (stopped || !owner.TakesEscape() || AnyModifierHeld() || ForegroundIsOwn())
                {
                    return false;
                }

                swallowEscapeUp = true;
                owner.dispatcher.BeginInvoke(() => owner.OnEscape(this));
                return true;
            }

            if ((message is WM_KEYUP or WM_SYSKEYUP) && swallowEscapeUp)
            {
                swallowEscapeUp = false;
                return true;
            }

            return false;
        }

        private void Run()
        {
            current = this;
            try
            {
                // メッセージの列を作ってから、終わりの知らせ（WM_APP_STOP）を受けられるようにする
                _ = PeekMessage(out _, 0, 0, 0, PM_NOREMOVE);
                Volatile.Write(ref threadId, (int)GetCurrentThreadId());
                var module = Native.GetModuleHandle(null);
                mouseHook = SetWindowsHookEx(WH_MOUSE_LL, &MouseProc, module, 0);
                if (mouseHook == 0)
                {
                    Log.Warn($"棚の外のクリックを見張れませんでした（エラー {Marshal.GetLastPInvokeError()}）。");
                }

                keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, &KeyboardProc, module, 0);
                if (keyboardHook == 0)
                {
                    Log.Warn($"Esc を見張れませんでした（エラー {Marshal.GetLastPInvokeError()}）。");
                }

                ready.Set();
                if (!stopped)
                {
                    // 見張りの呼び出しは、ここでメッセージを待っている間に来る
                    while (GetMessage(out var message, 0, 0, 0) > 0 && message.Id != WM_APP_STOP)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("外のクリックの見張りのスレッドで例外", ex);
            }
            finally
            {
                if (mouseHook != 0)
                {
                    UnhookWindowsHookEx(mouseHook);
                    mouseHook = 0;
                }

                if (keyboardHook != 0)
                {
                    UnhookWindowsHookEx(keyboardHook);
                    keyboardHook = 0;
                }

                current = null;
                ready.Set();
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int hookType, delegate* unmanaged<int, nint, nint, nint> callback, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out Message message, nint window, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Native.POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(nint hwnd, char* name, int capacity);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}
