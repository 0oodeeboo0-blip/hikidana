using System.Diagnostics;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Paste;
using Hikidana.Core.Storage;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>
/// 本当に貼り付ける。貼り付け先は、Edge で開いたテストのページ（ChatGPT や Claude のアプリと同じ Chromium の入力欄）と、メモ帳。
/// クリップボードに書いて確かめ、貼り付け先を前に出し、Ctrl+V を送るまでの全部を通す。
/// </summary>
public sealed class PasteTests : IDisposable
{
    private readonly TempDir dir = new();
    private readonly ClipboardWatcher watcher = new();
    private readonly ClipStore store;
    private readonly PasteService service;

    public PasteTests()
    {
        watcher.Start();
        store = dir.Store();
        service = new PasteService(store, watcher);
    }

    public void Dispose()
    {
        watcher.Dispose();
        dir.Dispose();
    }

    [Fact(WindowsOnly = true)]
    public async Task ScreenshotPastesIntoChromiumAsImage()
    {
        var page = await ChromiumPage.SharedAsync();
        var image = TestImages.Opaque(800, 450);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = image, SourceApp = "SnippingTool.exe" })!;

        var result = await PasteAsync(clip, page.Window);

        Assert.Equal(PasteOutcome.Pasted, result.Outcome, result.Message);
        var paste = await page.NextPasteAsync();
        ScreenCapture.Save("paste-screenshot-chromium");
        Assert.Equal(1, paste.Files.Count, paste.Raw);
        Assert.Equal("image/png", paste.Files[0].Type, paste.Raw);

        // Win+Shift+S で撮った直後と同じく、画像の中身から作られたファイル（一時ファイルの参照ではない）
        Assert.Equal("image.png", paste.Files[0].Name, paste.Raw);
        Assert.Equal(800, paste.Width, paste.Raw);
        Assert.Equal(450, paste.Height, paste.Raw);
        Assert.Contains("「", result.Message);
    }

    [Fact(WindowsOnly = true)]
    public async Task TextPastesIntoChromium()
    {
        var page = await ChromiumPage.SharedAsync();
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "こんにちは、Hikidana から貼り付けました。" })!;

        var result = await PasteAsync(clip, page.Window);

        Assert.Equal(PasteOutcome.Pasted, result.Outcome, result.Message);
        var paste = await page.NextPasteAsync();
        ScreenCapture.Save("paste-text-chromium");
        Assert.Equal("こんにちは、Hikidana から貼り付けました。", paste.Text, paste.Raw);
        Assert.Equal(0, paste.Files.Count, paste.Raw);
    }

    [Fact(WindowsOnly = true)]
    public async Task RichTextPastesWithHtmlAndPlainTextWithout()
    {
        var page = await ChromiumPage.SharedAsync();
        var clip = store.Add(new ClipDraft
        {
            Kind = ClipKind.RichText,
            Text = "太字のテキスト",
            Preserved = [new ClipboardFormatData(0, FormatName.Html, HtmlClipboard.Build("<b>太字</b>のテキスト"))],
        })!;

        var rich = await PasteAsync(clip, page.Window);
        Assert.Equal(PasteOutcome.Pasted, rich.Outcome, rich.Message);
        var richPaste = await page.NextPasteAsync();
        Assert.True(richPaste.Types.Contains("text/html"), richPaste.Raw);
        Assert.True(richPaste.HtmlLength > 0, richPaste.Raw);

        // 「書式なしで貼り付け」では、HTML を置かない
        var plain = await PasteAsync(clip, page.Window, PasteMode.PlainText);
        Assert.Equal(PasteOutcome.Pasted, plain.Outcome, plain.Message);
        var plainPaste = await page.NextPasteAsync();
        Assert.False(plainPaste.Types.Contains("text/html"), plainPaste.Raw);
        Assert.Equal("太字のテキスト", plainPaste.Text, plainPaste.Raw);
    }

    [Fact(WindowsOnly = true)]
    public async Task ScreenshotAsFilePastesNamedPngFile()
    {
        var page = await ChromiumPage.SharedAsync();
        var image = TestImages.Opaque(400, 300);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = image, SourceApp = "SnippingTool.exe" })!;

        var result = await PasteAsync(clip, page.Window, PasteMode.AsFile);

        Assert.Equal(PasteOutcome.Pasted, result.Outcome, result.Message);
        Assert.NotNull(result.ExportedFile);
        Assert.True(File.Exists(result.ExportedFile), "書き出したファイルがありません");
        var paste = await page.NextPasteAsync();
        ScreenCapture.Save("paste-file-chromium");
        Assert.Equal(1, paste.Files.Count, paste.Raw);
        Assert.Equal(Path.GetFileName(result.ExportedFile), paste.Files[0].Name, paste.Raw);
        Assert.Equal(400, paste.Width, paste.Raw);
    }

    [Fact(WindowsOnly = true)]
    public async Task PasteWaitsForHeldShiftToBeReleased()
    {
        var page = await ChromiumPage.SharedAsync();
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "Shift を離してから" })!;
        var target = TargetWindow.From(page.Window)!;

        // Shift を押したままにして、少ししてから離す（Ctrl+Shift+V にならないこと）
        TestKeys.Down(TestKeys.VK_LSHIFT);
        Assert.True(TestKeys.IsDown(TestKeys.VK_LSHIFT), "Shift を押したことになっていません");
        var release = Task.Run(async () =>
        {
            await Task.Delay(250);
            TestKeys.Up(TestKeys.VK_LSHIFT);
        });
        var started = Stopwatch.GetTimestamp();
        var result = await service.PasteAsync(clip, target);
        await release;

        Assert.Equal(PasteOutcome.Pasted, result.Outcome, result.Message);
        Assert.True(Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(200), "Shift が離れるのを待っていません");
        var paste = await page.NextPasteAsync();
        Assert.Equal("Shift を離してから", paste.Text, paste.Raw);
    }

    [Fact(WindowsOnly = true)]
    public async Task TextPastesIntoNotepad()
    {
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "メモ帳への貼り付け 123" })!;
        using var notepad = await Notepad.OpenAsync();

        var result = await PasteAsync(clip, notepad.Window);

        Assert.Equal(PasteOutcome.Pasted, result.Outcome, result.Message);
        Assert.True(await Desktop.WaitUntilAsync(() => notepad.Text.Contains("メモ帳への貼り付け 123"), TimeSpan.FromSeconds(5)), $"メモ帳の中身: 「{notepad.Text}」");
        ScreenCapture.Save("paste-text-notepad");
    }

    [Fact(WindowsOnly = true)]
    public async Task ClosedTargetFallsBackToCopy()
    {
        // 貼り付け先が閉じていたら、コピーだけにする
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Text, Text = "閉じたウインドウ" })!;
        TargetWindow target;
        using (var notepad = await Notepad.OpenAsync())
        {
            target = TargetWindow.From(notepad.Window)!;
        }

        Assert.True(await Desktop.WaitUntilAsync(() => !target.Exists, TimeSpan.FromSeconds(5)));
        var result = await service.PasteAsync(clip, target);
        Assert.Equal(PasteOutcome.CopiedOnly, result.Outcome, result.Message);
        Assert.Equal("閉じたウインドウ", ClipboardText.Read());
    }

    private async Task<PasteResult> PasteAsync(Clip clip, nint window, PasteMode mode = PasteMode.Normal)
    {
        var target = TargetWindow.From(window) ?? throw new AssertionException("貼り付け先のウインドウがありません。");
        Console.WriteLine($"       貼り付け先: {target}、前面: {Desktop.ForegroundDescription()}");
        var result = await service.PasteAsync(clip, target, mode);
        Console.WriteLine($"       結果: {result.Outcome}「{result.Message}」");
        if (result.Outcome != PasteOutcome.Pasted)
        {
            ScreenCapture.Save($"failed-paste-{DateTime.Now:HHmmss}");
        }

        return result;
    }
}
