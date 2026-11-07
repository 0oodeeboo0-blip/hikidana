using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Tests.Clipboard;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Clips;

public class ClipClassifierTests
{
    [Fact]
    public void SnippingToolImageIsScreenshot()
    {
        var image = TestImages.Opaque(64, 36);
        var draft = ClipClassifier.Classify(Snap.Of("SnippingTool.exe", Snap.DibV5(image), Snap.Png(image)));
        Assert.NotNull(draft);
        Assert.Equal(ClipKind.Screenshot, draft.Kind);
        Assert.True(image.SamePixels(draft.Image!));
        Assert.NotNull(draft.OriginalPng, "PNG があればそのまま残す。");
    }

    [Fact]
    public void SnippingToolWindowModeKeepsTitleAsText()
    {
        // ウインドウを撮るモードでは、題名のテキストも一緒に置かれる
        var image = TestImages.Opaque(40, 30);
        var draft = ClipClassifier.Classify(Snap.Of("SnippingTool.exe", Snap.DibV5(image), Snap.UnicodeText("売上レポート - Microsoft Edge")));
        Assert.Equal(ClipKind.Screenshot, draft!.Kind);
        Assert.Equal("売上レポート - Microsoft Edge", draft.Text);
    }

    [Fact]
    public void PrintScreenWithoutOwnerIsScreenshot()
    {
        var image = TestImages.Opaque(32, 20);
        var draft = ClipClassifier.Classify(Snap.Of(null, Snap.Dib(image)));
        Assert.Equal(ClipKind.Screenshot, draft!.Kind);
        Assert.True(image.SamePixels(draft.Image!));
        Assert.Null(draft.OriginalPng);
    }

    [Fact]
    public void BrowserCopyImageIsImageNotLink()
    {
        // ブラウザーの「画像をコピー」は、画像の URL のテキストや <img> の HTML も一緒に置く
        var image = TestImages.WithAlpha(30, 20);
        var draft = ClipClassifier.Classify(Snap.Of(
            "chrome.exe",
            Snap.Html("<img src=\"https://example.com/photo.png\" alt=\"写真\">", "https://example.com/page"),
            Snap.UnicodeText("https://example.com/photo.png"),
            Snap.Png(image),
            Snap.Dib(image)));
        Assert.Equal(ClipKind.Image, draft!.Kind);
        Assert.True(image.SamePixels(draft.Image!), "PNG（透明あり）を優先する。");
    }

    [Fact]
    public void ExplorerCopyIsFiles()
    {
        var draft = ClipClassifier.Classify(Snap.Of(
            "explorer.exe",
            Snap.Named(FormatName.ShellIdList, new byte[] { 1, 2, 3 }),
            Snap.Standard(StandardFormat.HDrop, DropFiles.Build([@"C:\資料\見積書.xlsx", @"C:\資料\議事録.docx"])),
            Snap.Named(FormatName.PreferredDropEffect, DropFiles.DropEffect())));
        Assert.Equal(ClipKind.Files, draft!.Kind);
        Assert.SequenceEqual(new[] { @"C:\資料\見積書.xlsx", @"C:\資料\議事録.docx" }, draft.Files);
    }

    [Fact]
    public void ExcelRangeIsTableAndKeepsExcelFormats()
    {
        var picture = TestImages.Opaque(120, 40);
        var draft = ClipClassifier.Classify(Snap.Of(
            "EXCEL.EXE",
            Snap.Named(FormatName.Biff12, new byte[] { 9, 9, 9 }),
            Snap.Named(FormatName.XmlSpreadsheet, "<Workbook/>"),
            Snap.Html("<table><tr><td>品名</td><td>数量</td></tr><tr><td>ノート</td><td>12</td></tr></table>"),
            Snap.Named("Object Descriptor", new byte[] { 1 }),
            Snap.Dib(picture),
            Snap.UnicodeText("品名\t数量\r\nノート\t12\r\n")));
        Assert.Equal(ClipKind.Table, draft!.Kind);
        Assert.Equal("ノート", draft.TableRows![1][0]);
        var names = draft.Preserved.Select(f => f.Name).ToList();
        Assert.True(names.Contains(FormatName.Biff12));
        Assert.True(names.Contains(FormatName.XmlSpreadsheet));
        Assert.True(names.Contains(FormatName.Html));
        Assert.False(names.Contains("Object Descriptor"), "元のアプリが無いと使えない形は残さない。");
        Assert.False(names.Contains("CF_DIB"), "表の絵は残さない。");
        Assert.False(names.Contains("CF_UNICODETEXT"), "テキストは別に持つ。");
    }

    [Fact]
    public void ExcelSingleCellIsTextButKeepsFormats()
    {
        var draft = ClipClassifier.Classify(Snap.Of(
            "EXCEL.EXE",
            Snap.Named(FormatName.Biff12, new byte[] { 1 }),
            Snap.UnicodeText("12345\r\n")));
        Assert.Equal(ClipKind.Text, draft!.Kind);
        Assert.True(draft.Preserved.Any(f => f.Name == FormatName.Biff12));
    }

    [Fact]
    public void GoogleSheetsTableIsTable()
    {
        var draft = ClipClassifier.Classify(Snap.Of(
            "chrome.exe",
            Snap.Html("<google-sheets-html-origin><table><tr><td>a</td><td>b</td></tr></table>"),
            Snap.UnicodeText("a\tb\r\nc\td")));
        Assert.Equal(ClipKind.Table, draft!.Kind);
    }

    [Fact]
    public void WordSelectionIsRichText()
    {
        var draft = ClipClassifier.Classify(Snap.Of(
            "WINWORD.EXE",
            Snap.Named(FormatName.Rtf, "{\\rtf1 {\\b 締め切り}}"),
            Snap.Html("<b>締め切り</b>は 11 月 15 日です。"),
            Snap.UnicodeText("締め切りは 11 月 15 日です。")));
        Assert.Equal(ClipKind.RichText, draft!.Kind);
        Assert.Equal("締め切りは 11 月 15 日です。", draft.Text);
        Assert.Equal(2, draft.Preserved.Count);
    }

    [Fact]
    public void CodeFromEditor()
    {
        var draft = ClipClassifier.Classify(Snap.Of("Code.exe", Snap.UnicodeText("winget install Microsoft.DotNet.SDK.10")));
        Assert.Equal(ClipKind.Code, draft!.Kind);
    }

    [Fact]
    public void PlainTextFromNotepad()
    {
        var draft = ClipClassifier.Classify(Snap.Of("Notepad.exe", Snap.UnicodeText("明日の打ち合わせは 10:00 から")));
        Assert.Equal(ClipKind.Text, draft!.Kind);
        Assert.Equal("Notepad.exe", draft.SourceApp);
    }

    [Fact]
    public void AddressBarUrlIsLink()
    {
        var draft = ClipClassifier.Classify(Snap.Of(
            "msedge.exe",
            Snap.Html("<a href=\"https://learn.microsoft.com/ja-jp/\">https://learn.microsoft.com/ja-jp/</a>"),
            Snap.UnicodeText("https://learn.microsoft.com/ja-jp/")));
        Assert.Equal(ClipKind.Link, draft!.Kind);
    }

    [Fact]
    public void ColorCode()
    {
        var draft = ClipClassifier.Classify(Snap.Of("PowerToys.ColorPickerUI.exe", Snap.UnicodeText("#0B6E78")));
        Assert.Equal(ClipKind.Color, draft!.Kind);
        Assert.Equal("#0B6E78", draft.Color!.Value.Hex);
    }

    [Fact]
    public void BrokenPngFallsBackToDib()
    {
        var image = TestImages.Opaque(16, 16);
        var draft = ClipClassifier.Classify(Snap.Of(
            "app.exe",
            Snap.Named(FormatName.Png, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 }),
            Snap.DibV5(image)));
        Assert.Equal(ClipKind.Image, draft!.Kind);
        Assert.True(image.SamePixels(draft.Image!));
        Assert.Null(draft.OriginalPng);
    }

    [Fact]
    public void NothingUsableIsNull()
    {
        Assert.Null(ClipClassifier.Classify(Snap.Of("app.exe")));
        Assert.Null(ClipClassifier.Classify(Snap.Of("app.exe", Snap.UnicodeText("   \r\n"))));
        Assert.Null(ClipClassifier.Classify(Snap.Of("app.exe", Snap.Named("Some Private Format", new byte[] { 1 }))));
    }

    [Fact]
    public void DroppedImageIsImageNotScreenshot()
    {
        var image = TestImages.Opaque(20, 10);
        var draft = ClipClassifier.Classify(Snap.Of(ClipLabels.DropSource, Snap.Png(image)))!;
        Assert.Equal(ClipKind.Image, draft.Kind);
        Assert.Equal(ClipLabels.DropSource, draft.SourceApp);
    }
}
