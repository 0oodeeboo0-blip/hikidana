using System.Threading.Channels;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Storage;

namespace Hikidana.Win32;

/// <summary>
/// 見張りが読んだ中身を見分けて、棚にしまう。来た順に 1 つずつ（大きな画像は保存に時間がかかるため、
/// 見張りのスレッドとは別のスレッドで）。
/// </summary>
public sealed class ClipboardRecorder : IDisposable
{
    private readonly ClipStore store;
    private readonly ClipboardWatcher watcher;
    private readonly Channel<ClipboardSnapshot> queue = Channel.CreateUnbounded<ClipboardSnapshot>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task worker;
    private int pending;

    public ClipboardRecorder(ClipStore store, ClipboardWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(watcher);
        this.store = store;
        this.watcher = watcher;
        watcher.Captured += OnCaptured;
        worker = Task.Run(WorkAsync);
    }

    /// <summary>しまった（同じものが既にあれば、その項目）。スレッドプールのスレッドで呼ばれる。</summary>
    public event Action<Clip>? Recorded;

    /// <summary>しまえなかった。スレッドプールのスレッドで呼ばれる。</summary>
    public event Action<Exception>? Failed;

    /// <summary>しまうのを待っているものと、しまっている途中のものが無くなるまで待つ（テスト用）。</summary>
    public async Task<bool> WaitIdleAsync(TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (Volatile.Read(ref pending) == 0)
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return Volatile.Read(ref pending) == 0;
    }

    public void Dispose()
    {
        watcher.Captured -= OnCaptured;
        queue.Writer.TryComplete();
        worker.Wait(TimeSpan.FromSeconds(5));
    }

    private void OnCaptured(ClipboardSnapshot snapshot)
    {
        Interlocked.Increment(ref pending);
        if (!queue.Writer.TryWrite(snapshot))
        {
            Interlocked.Decrement(ref pending);
        }
    }

    private async Task WorkAsync()
    {
        await foreach (var snapshot in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var draft = ClipClassifier.Classify(snapshot);
                if (draft is not null && store.Add(draft) is { } clip)
                {
                    Recorded?.Invoke(clip);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Hikidana] しまえませんでした: {ex}");
                Failed?.Invoke(ex);
            }
            finally
            {
                Interlocked.Decrement(ref pending);
            }
        }
    }
}
