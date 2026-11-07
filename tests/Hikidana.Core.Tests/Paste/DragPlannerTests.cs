using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Paste;
using Hikidana.Core.Storage;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Paste;

public sealed class DragPlannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hikidana-tests", Guid.NewGuid().ToString("N"));
    private readonly ClipStore store;

    public DragPlannerTests()
    {
        store = new ClipStore(root, time: new FakeTime(new DateTimeOffset(2026, 11, 7, 21, 45, 0, TimeSpan.Zero)));
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
    public void ScreenshotIsDraggedAsNamedPngFile()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = TestImages.Opaque(64, 48) })!;
        var plan = DragPlanner.Plan(store, clip);

        Assert.Equal(1, plan.Files.Count);
        Assert.Equal("スクリーンショット 2026-11-07 214500.png", Path.GetFileName(plan.Files[0]));
        Assert.Equal(plan.ExportedFile, plan.Files[0]);
        Assert.SequenceEqual(store.ReadPng(clip)!, File.ReadAllBytes(plan.Files[0]));
        Assert.SequenceEqual(new[] { FormatName.Png, FormatName.HikidanaMarker }, plan.Formats.Select(f => f.Name));
        Assert.Null(plan.Text);
        Assert.False(plan.AllowLink);

        // 2 回目も同じファイル（増やさない）
        Assert.Equal(plan.Files[0], DragPlanner.Plan(store, clip).Files[0]);
    }

    [Fact]
    public void TextKeepsItsFormattingButNotStandardIdFormats()
    {
        var clip = store.Add(new ClipDraft
        {
            Kind = ClipKind.RichText,
            Text = "太字のテキスト",
            Preserved =
            [
                new ClipboardFormatData(0xC0F0, FormatName.Html, HtmlClipboard.Build("<b>太字</b>のテキスト")),
                new ClipboardFormatData(0xC0F1, FormatName.Rtf, Encoding.ASCII.GetBytes(@"{\rtf1 x}")),
            ],
        })!;
        var plan = DragPlanner.Plan(store, clip);

        Assert.Equal("太字のテキスト", plan.Text);
        Assert.Equal(0, plan.Files.Count);
        Assert.SequenceEqual(new[] { FormatName.Html, FormatName.Rtf, FormatName.HikidanaMarker }, plan.Formats.Select(f => f.Name));
    }

    [Fact]
    public void LinkAlsoGoesAsUrl()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Link, Text = "https://example.com/a?b=1" })!;
        var plan = DragPlanner.Plan(store, clip);

        Assert.Equal("https://example.com/a?b=1", plan.Text);
        Assert.True(plan.AllowLink);
        var wide = plan.Formats.Single(f => f.Name == FormatName.UrlW).Data;
        Assert.Equal("https://example.com/a?b=1\0", Encoding.Unicode.GetString(wide));
        var ansi = plan.Formats.Single(f => f.Name == FormatName.Url).Data;
        Assert.Equal("https://example.com/a?b=1\0", Encoding.ASCII.GetString(ansi));
    }

    [Fact]
    public void FilesThatStillExistAreDragged()
    {
        var a = Path.Combine(root, "a.txt");
        var b = Path.Combine(root, "b.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(a, "a");
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [a, b] })!;

        var plan = DragPlanner.Plan(store, clip);
        Assert.SequenceEqual(new[] { a }, plan.Files);
        Assert.NotNull(plan.Note);
        Assert.False(plan.IsEmpty);

        File.Delete(a);
        var none = DragPlanner.Plan(store, clip);
        Assert.True(none.IsEmpty);
        Assert.NotNull(none.Note);
    }

    [Fact]
    public void MissingImageGivesEmptyPlan()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = TestImages.Opaque(8, 8) })!;
        File.Delete(store.ImagePath(clip)!);

        var plan = DragPlanner.Plan(store, clip);
        Assert.True(plan.IsEmpty);
        Assert.NotNull(plan.Note);
    }
}
