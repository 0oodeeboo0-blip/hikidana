using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Paste;
using Hikidana.Core.Storage;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>ほかのアプリのコピーを、本物のクリップボードから棚にしまうまで。</summary>
public sealed class RecorderTests : IDisposable
{
    private readonly TempDir dir = new();
    private readonly ClipboardWatcher watcher = new();
    private readonly ClipboardWatcher other = new();
    private readonly ClipStore store;
    private readonly ClipboardRecorder recorder;

    public RecorderTests()
    {
        watcher.Start();
        other.Start();
        store = dir.Store();
        recorder = new ClipboardRecorder(store, watcher);
    }

    public void Dispose()
    {
        recorder.Dispose();
        watcher.Dispose();
        other.Dispose();
        dir.Dispose();
    }

    [Fact(WindowsOnly = true)]
    public async Task ImageIsStoredWithSamePixels()
    {
        var image = TestImages.Opaque(640, 360);
        var clip = await RecordAsync(
        [
            OutgoingFormat.Named(FormatName.Png, PngCodec.Encode(image)),
            OutgoingFormat.Standard(StandardFormat.DibV5, DibCodec.EncodeV5(image)),
            OutgoingFormat.Standard(StandardFormat.Dib, DibCodec.EncodeDib(image)),
        ]);

        Assert.True(clip.IsImage, $"画像になっていません（{clip.Kind}）");
        Assert.Equal(640, clip.ImageWidth);
        Assert.Equal(360, clip.ImageHeight);
        Assert.True(clip.HasThumbnail || clip.ImageWidth <= store.Options.ThumbnailSize);
        Assert.True(PngCodec.Decode(store.ReadPng(clip)!).SamePixels(image), "しまった画像の画素が違います");
    }

    [Fact(WindowsOnly = true)]
    public async Task DibOnlyImageIsStoredAsPng()
    {
        var image = TestImages.Opaque(300, 200);
        var clip = await RecordAsync([OutgoingFormat.Standard(StandardFormat.Dib, DibCodec.EncodeDib(image))]);
        Assert.True(clip.IsImage);
        Assert.True(PngCodec.Decode(store.ReadPng(clip)!).SamePixels(image));
    }

    [Fact(WindowsOnly = true)]
    public async Task RichTextKeepsHtmlForPasting()
    {
        var html = HtmlClipboard.Build("<p><b>太字</b>と<i>斜体</i>のテキスト</p>", "https://example.com/page");
        var clip = await RecordAsync([PastePlanner.UnicodeText("太字と斜体のテキスト"), OutgoingFormat.Named(FormatName.Html, html)]);

        Assert.Equal(ClipKind.RichText, clip.Kind);
        Assert.Equal("https://example.com/page", clip.SourceUrl);
        Assert.Equal("太字と斜体のテキスト", store.ReadText(clip));
        Assert.True(store.ReadPreserved(clip).Any(f => f.Name == FormatName.Html), "HTML をしまっていません");
    }

    [Fact(WindowsOnly = true)]
    public async Task SameTextTwiceIsStoredOnce()
    {
        var first = await RecordAsync([PastePlanner.UnicodeText("同じテキスト")]);
        await RecordAsync([PastePlanner.UnicodeText("別のテキスト")]);
        var again = await RecordAsync([PastePlanner.UnicodeText("同じテキスト")]);

        // 同じテキストは、新しくしまい直して古い方と入れ替える（最初にしまった日時は引き継ぐ）
        Assert.Equal(2, store.Items.Count);
        Assert.Equal(again.Id, store.Items[0].Id);
        Assert.Equal("同じテキスト", store.ReadText(store.Items[0]));
        Assert.Equal(first.CreatedAt, again.CreatedAt);
        Assert.Null(store.Find(first.Id));
    }

    [Fact(WindowsOnly = true)]
    public async Task CopyingFromTheShelfIsNotStoredAgain()
    {
        var clip = await RecordAsync([PastePlanner.UnicodeText("棚からコピーする")]);
        var recorded = 0;
        recorder.Recorded += _ => Interlocked.Increment(ref recorded);

        var service = new PasteService(store, watcher);
        var result = await service.CopyAsync(clip);
        Assert.Equal(PasteOutcome.CopiedOnly, result.Outcome, result.Message);

        // 印も見張りの番号も効いて、しまい直さない
        await Task.Delay(800);
        Assert.True(await recorder.WaitIdleAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, recorded);
        Assert.Equal(1, store.Items.Count);
        Assert.Equal("棚からコピーする", ClipboardText.Read());
    }

    private async Task<Clip> RecordAsync(IReadOnlyList<OutgoingFormat> formats)
    {
        var recorded = new TaskCompletionSource<Clip>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRecorded(Clip clip) => recorded.TrySetResult(clip);
        recorder.Recorded += OnRecorded;
        try
        {
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync(formats));
            try
            {
                return await recorded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                throw new AssertionException("15 秒待っても、しまわれませんでした。");
            }
        }
        finally
        {
            recorder.Recorded -= OnRecorded;
        }
    }
}
