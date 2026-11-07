using System.Runtime.InteropServices;
using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Paste;
using Hikidana.Core.Storage;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Paste;

public sealed class PastePlannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hikidana-tests", Guid.NewGuid().ToString("N"));
    private readonly ClipStore store;

    public PastePlannerTests()
    {
        store = new ClipStore(root, time: new FakeTime(new DateTimeOffset(2026, 11, 7, 18, 12, 5, TimeSpan.Zero)));
        store.Load();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void ScreenshotToAppIsImageOnly()
    {
        var image = TestImages.Opaque(120, 80);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = image })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal);
        Assert.True(plan.SendKeys);
        Assert.SequenceEqual(new[] { FormatName.Png, "CF_DIBV5", "CF_DIB", FormatName.HikidanaMarker }, plan.Formats.Select(f => f.Name));
        Assert.False(plan.Formats.Any(f => f.StandardId == StandardFormat.HDrop), "画像にファイルの参照を混ぜない（Edge-Drop の不具合の原因）。");
        Assert.True(image.SamePixels(PngCodec.Decode(plan.Formats[0].Data)));
        Assert.True(image.SamePixels(DibCodec.Decode(plan.Formats[1].Data)));
        Assert.True(image.SamePixels(DibCodec.Decode(plan.Formats[2].Data)));
        Assert.Null(plan.ExportedFile);
    }

    [Fact]
    public void ScreenshotToExplorerIsNamedFile()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = TestImages.Opaque(40, 30) })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.FileManager, PasteMode.Normal);
        Assert.True(plan.SendKeys);
        var files = DropFiles.Parse(plan.Formats.Single(f => f.StandardId == StandardFormat.HDrop).Data);
        Assert.Equal(1, files.Count);
        Assert.Equal("スクリーンショット 2026-11-07 181205.png", Path.GetFileName(files[0]));
        Assert.SequenceEqual(store.ReadPng(clip)!, File.ReadAllBytes(files[0]));
        Assert.True(plan.Formats.Any(f => f.Name == FormatName.PreferredDropEffect));
        Assert.Equal(files[0] + "\0", ReadUtf16(plan.Formats.Single(f => f.Name == FormatName.FileNameW).Data));
        Assert.False(plan.Formats.Any(f => f.Name == FormatName.Png), "ファイルとして渡すときは画像の形を置かない。");
    }

    [Fact]
    public void ImageAsFileToApp()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = TestImages.Opaque(10, 10) })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.AsFile);
        Assert.NotNull(plan.ExportedFile);
        Assert.Equal("画像 2026-11-07 181205.png", Path.GetFileName(plan.ExportedFile));
    }

    [Fact]
    public void ImageToTerminalIsCopyOnly()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = TestImages.Opaque(10, 10) })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.Terminal, PasteMode.Normal);
        Assert.False(plan.SendKeys);
        Assert.Contains("ターミナル", plan.Note);
        Assert.True(plan.Formats.Any(f => f.Name == FormatName.Png), "コピーはしておく。");
    }

    [Fact]
    public void RichTextKeepsFormats()
    {
        var clip = store.Add(new ClipDraft
        {
            Kind = ClipKind.RichText,
            Text = "締め切りは 11/15",
            Preserved = [new ClipboardFormatData(0xC0AA, FormatName.Html, HtmlClipboard.Build("<b>締め切り</b>は 11/15"))],
        })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal);
        Assert.SequenceEqual(new[] { "CF_UNICODETEXT", FormatName.Html, FormatName.HikidanaMarker }, plan.Formats.Select(f => f.Name));
        Assert.Equal("締め切りは 11/15\0", ReadUtf16(plan.Formats[0].Data));
    }

    [Fact]
    public void PlainTextDropsFormats()
    {
        var clip = store.Add(new ClipDraft
        {
            Kind = ClipKind.RichText,
            Text = "太字",
            Preserved = [new ClipboardFormatData(0xC0AA, FormatName.Rtf, "{\\rtf1 {\\b 太字}}"u8.ToArray())],
        })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.PlainText);
        Assert.SequenceEqual(new[] { "CF_UNICODETEXT", FormatName.HikidanaMarker }, plan.Formats.Select(f => f.Name));
    }

    [Fact]
    public void TextToExplorerIsCopyOnly()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "メモ" })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.FileManager, PasteMode.Normal);
        Assert.False(plan.SendKeys);
        Assert.Contains("エクスプローラー", plan.Note);
    }

    [Fact]
    public void FilesSkipMissingOnes()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [@"C:\ある.txt", @"C:\ない.txt"] })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal, p => p.Contains("ある"));
        Assert.SequenceEqual(new[] { @"C:\ある.txt" }, DropFiles.Parse(plan.Formats[0].Data));
        Assert.Contains("見つからないファイルが 1 個", plan.Note);
    }

    [Fact]
    public void MissingFilesAreNotPasted()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [@"C:\古い資料.pdf"] })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal, _ => false);
        Assert.True(plan.IsEmpty);
        Assert.False(plan.SendKeys);
    }

    [Fact]
    public void FilesAsPlainTextArePaths()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [@"C:\a.txt", @"C:\b.txt"] })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.PlainText, _ => true);
        Assert.Equal("C:\\a.txt\r\nC:\\b.txt\0", ReadUtf16(plan.Formats[0].Data));
    }

    [Fact]
    public void MarkerNamesTheClip()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "x" })!;
        var plan = PastePlanner.Plan(store, clip, PasteTarget.App, PasteMode.Normal);
        Assert.Contains(":" + clip.Id, Encoding.UTF8.GetString(plan.Formats.Single(f => f.Name == FormatName.HikidanaMarker).Data));
    }

    private static string ReadUtf16(byte[] data) => new(MemoryMarshal.Cast<byte, char>(data));
}
