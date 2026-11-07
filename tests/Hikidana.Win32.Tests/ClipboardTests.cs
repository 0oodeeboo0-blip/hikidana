using System.Buffers.Binary;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Paste;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>本物のクリップボードを使う。「ほかのアプリ」は、もう 1 つの見張り（別のスレッドと見えないウインドウ）で代わりをする。</summary>
public sealed class ClipboardTests : IDisposable
{
    private readonly TempDir dir = new();
    private readonly ClipboardWatcher watcher = new();
    private readonly ClipboardWatcher other = new();

    public ClipboardTests()
    {
        watcher.Start();
        other.Start();
    }

    public void Dispose()
    {
        watcher.Dispose();
        other.Dispose();
        dir.Dispose();
    }

    [Fact(WindowsOnly = true)]
    public async Task ScreenshotIsWrittenAsImageFormatsOnly()
    {
        var store = dir.Store();
        var image = TestImages.Opaque(320, 200);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = image, SourceApp = "SnippingTool.exe" })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal);

        Assert.Equal(WriteStatus.Ok, await watcher.WriteAsync(plan.Formats));

        var names = ClipboardAccess.ListFormats(0).Select(f => f.Name).ToList();
        Console.WriteLine($"       クリップボードの形: {string.Join(", ", names)}");
        foreach (var expected in new[] { "PNG", "CF_DIBV5", "CF_DIB", "CF_BITMAP", FormatName.HikidanaMarker })
        {
            Assert.True(names.Contains(expected), $"{expected} がありません");
        }

        // Edge-Drop の問題: 画像にファイルの参照を混ぜると、OneNote などがそのファイルを探して失敗する
        foreach (var unexpected in new[] { "CF_HDROP", FormatName.FileNameW, FormatName.FileName, FormatName.ShellIdList })
        {
            Assert.False(names.Contains(unexpected), $"{unexpected} が混ざっています");
        }

        Assert.True(PngCodec.Decode(ClipboardAccess.ReadFormat(0, FormatName.Png)!).SamePixels(image), "PNG の画素が違います");
        Assert.True(DibCodec.Decode(ClipboardAccess.ReadFormat(0, StandardFormat.DibV5)!).SamePixels(image), "CF_DIBV5 の画素が違います");
        Assert.True(DibCodec.Decode(ClipboardAccess.ReadFormat(0, StandardFormat.Dib)!).SamePixels(image), "CF_DIB の画素が違います");
    }

    [Fact(WindowsOnly = true)]
    public async Task TransparentImageKeepsAlphaInPngAndDibV5()
    {
        var store = dir.Store();
        var image = TestImages.WithAlpha(64, 48);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = image })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal);

        Assert.Equal(WriteStatus.Ok, await watcher.WriteAsync(plan.Formats));

        Assert.True(PngCodec.Decode(ClipboardAccess.ReadFormat(0, FormatName.Png)!).SamePixels(image), "PNG の画素が違います");
        Assert.True(DibCodec.Decode(ClipboardAccess.ReadFormat(0, StandardFormat.DibV5)!).SamePixels(image), "CF_DIBV5 の画素が違います");

        // 透明度を読まないアプリ向けの CF_DIB は、白の上に重ねた色
        var dib = DibCodec.Decode(ClipboardAccess.ReadFormat(0, StandardFormat.Dib)!);
        Assert.False(dib.HasTransparency(), "CF_DIB に透明な所があります");
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), dib.GetPixel(0, 0));
        Assert.Equal(image.GetPixel(63, 0), dib.GetPixel(63, 0));
    }

    [Fact(WindowsOnly = true)]
    public async Task TextFromAnotherAppIsCaptured()
    {
        using var events = new WatcherEvents(watcher);
        Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("こんにちは、Hikidana")]));

        var snapshot = await events.NextCaptureAsync();
        Assert.Equal("こんにちは、Hikidana", ClipClassifier.ReadText(snapshot));
        Assert.Equal(ProcessInfo.ImageName((uint)Environment.ProcessId), snapshot.OwnerProcess);
        Assert.NotEqual(0u, snapshot.Sequence);
    }

    [Fact(WindowsOnly = true)]
    public async Task OwnWritesAreNotCaptured()
    {
        using var events = new WatcherEvents(watcher);
        var marker = OutgoingFormat.Named(FormatName.HikidanaMarker, PastePlanner.Marker("test"));

        // 自分の見張りで書いた変化は、知らせも出さない
        Assert.Equal(WriteStatus.Ok, await watcher.WriteAsync([PastePlanner.UnicodeText("自分で書いた"), marker]));
        await Task.Delay(800);
        Assert.Equal(0, events.CapturedCount, "自分で書いた中身を取り込みました");
        Assert.Equal(0, events.SkippedCount, "自分で書いた変化を読みにいきました");

        // ほかの経路で書かれても、印があれば取り込まない
        Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("印つき"), marker]));
        Assert.Equal(SkipReason.OwnWrite, await events.NextSkipAsync());
        Assert.Equal(0, events.CapturedCount);
    }

    [Fact(WindowsOnly = true)]
    public async Task PrivateCopiesAreNotCaptured()
    {
        var cases = new (string Format, byte[] Data, SkipReason Expected)[]
        {
            (FormatName.ExcludeFromMonitor, [0], SkipReason.MarkedPrivate),
            (FormatName.CanIncludeInHistory, Int32(0), SkipReason.NotForHistory),
            (FormatName.ViewerIgnore, [0], SkipReason.ViewerIgnore),
        };
        foreach (var (format, data, expected) in cases)
        {
            using var events = new WatcherEvents(watcher);
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("パスワード"), OutgoingFormat.Named(format, data)]));
            Assert.Equal(expected, await events.NextSkipAsync(), format);
            Assert.Equal(0, events.CapturedCount, format);
        }

        // CanIncludeInClipboardHistory が 1 なら、ふつうに取り込む
        using (var events = new WatcherEvents(watcher))
        {
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("履歴に残してよい"), OutgoingFormat.Named(FormatName.CanIncludeInHistory, Int32(1))]));
            Assert.Equal("履歴に残してよい", ClipClassifier.ReadText(await events.NextCaptureAsync()));
        }
    }

    [Fact(WindowsOnly = true)]
    public async Task ExcludedAppsAndPauseAreHonored()
    {
        using (var events = new WatcherEvents(watcher))
        {
            watcher.ExcludedApps = [ProcessInfo.ImageName((uint)Environment.ProcessId)!];
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("記録しないアプリ")]));
            Assert.Equal(SkipReason.ExcludedApp, await events.NextSkipAsync());
            watcher.ExcludedApps = [];
        }

        using (var events = new WatcherEvents(watcher))
        {
            watcher.Paused = true;
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("止めている間")]));
            Assert.Equal(SkipReason.Paused, await events.NextSkipAsync());
            watcher.Paused = false;

            // 止めるのをやめたら、また取り込む
            Assert.Equal(WriteStatus.Ok, await other.WriteAsync([PastePlanner.UnicodeText("再開した")]));
            Assert.Equal("再開した", ClipClassifier.ReadText(await events.NextCaptureAsync()));
        }
    }

    [Fact(WindowsOnly = true)]
    public async Task OnlyTheOriginalDibIsRead()
    {
        using var events = new WatcherEvents(watcher);
        var image = TestImages.Opaque(200, 120);

        // CF_DIB だけを置くと、Windows が CF_DIBV5 と CF_BITMAP を作って後ろに並べる
        Assert.Equal(WriteStatus.Ok, await other.WriteAsync([OutgoingFormat.Standard(StandardFormat.Dib, DibCodec.EncodeDib(image))]));
        Console.WriteLine($"       クリップボードの形: {string.Join(", ", ClipboardAccess.ListFormats(0).Select(f => f.Name))}");

        var snapshot = await events.NextCaptureAsync();
        Console.WriteLine($"       読んだ形: {snapshot}");
        Assert.True(snapshot.Has(StandardFormat.Dib), "元の CF_DIB を読んでいません");
        Assert.False(snapshot.Has(StandardFormat.DibV5), "Windows が作った CF_DIBV5 まで読みました");
        Assert.True(ClipClassifier.ReadImage(snapshot).Image!.SamePixels(image), "画素が違います");
    }

    [Fact(WindowsOnly = true)]
    public async Task PngIsPreferredOverDib()
    {
        using var events = new WatcherEvents(watcher);
        var image = TestImages.Opaque(160, 90);
        Assert.Equal(WriteStatus.Ok, await other.WriteAsync(
        [
            OutgoingFormat.Named(FormatName.Png, PngCodec.Encode(image)),
            OutgoingFormat.Standard(StandardFormat.DibV5, DibCodec.EncodeV5(image)),
            OutgoingFormat.Standard(StandardFormat.Dib, DibCodec.EncodeDib(image)),
        ]));

        var snapshot = await events.NextCaptureAsync();
        Assert.True(snapshot.Has(FormatName.Png));
        Assert.False(snapshot.Has(StandardFormat.DibV5), "PNG があるのに CF_DIBV5 も読みました");
        Assert.False(snapshot.Has(StandardFormat.Dib), "PNG があるのに CF_DIB も読みました");
    }

    [Fact(WindowsOnly = true)]
    public async Task WriteFailsCleanlyWhileAnotherAppHoldsTheClipboard()
    {
        // ほかのアプリが開いたままにしている間は、書けずに Busy を返す（固まらない）
        var opened = await other.InvokeAsync(ClipboardHold.Open);
        Assert.True(opened, "クリップボードを開けませんでした");
        try
        {
            var started = DateTime.UtcNow;
            var status = await watcher.WriteAsync([PastePlanner.UnicodeText("書けないはず")]);
            Assert.Equal(WriteStatus.Busy, status);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "待ちすぎです");
        }
        finally
        {
            await other.InvokeAsync(ClipboardHold.Close);
        }

        Assert.Equal(WriteStatus.Ok, await watcher.WriteAsync([PastePlanner.UnicodeText("書ける")]));
    }

    private static byte[] Int32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
