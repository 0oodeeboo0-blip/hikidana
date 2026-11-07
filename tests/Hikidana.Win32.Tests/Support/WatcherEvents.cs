using System.Threading.Channels;
using Hikidana.Core.Clipboard;
using Hikidana.Testing;

namespace Hikidana.Win32.Tests.Support;

/// <summary>見張りからの知らせを、来た順にためて、待てるようにする。</summary>
internal sealed class WatcherEvents : IDisposable
{
    private readonly ClipboardWatcher watcher;
    private readonly Channel<ClipboardSnapshot> captured = Channel.CreateUnbounded<ClipboardSnapshot>();
    private readonly Channel<SkipReason> skipped = Channel.CreateUnbounded<SkipReason>();

    public WatcherEvents(ClipboardWatcher watcher)
    {
        this.watcher = watcher;
        watcher.Captured += OnCaptured;
        watcher.Skipped += OnSkipped;
    }

    public int CapturedCount => captured.Reader.Count;

    public int SkippedCount => skipped.Reader.Count;

    public async Task<ClipboardSnapshot> NextCaptureAsync(int timeoutMilliseconds = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            return await captured.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new AssertionException($"{timeoutMilliseconds} ミリ秒待っても、取り込みの知らせが来ませんでした。");
        }
    }

    public async Task<SkipReason> NextSkipAsync(int timeoutMilliseconds = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            return await skipped.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new AssertionException($"{timeoutMilliseconds} ミリ秒待っても、取り込まなかった知らせが来ませんでした。");
        }
    }

    public void Dispose()
    {
        watcher.Captured -= OnCaptured;
        watcher.Skipped -= OnSkipped;
    }

    private void OnCaptured(ClipboardSnapshot snapshot) => captured.Writer.TryWrite(snapshot);

    private void OnSkipped(SkipReason reason) => skipped.Writer.TryWrite(reason);
}
