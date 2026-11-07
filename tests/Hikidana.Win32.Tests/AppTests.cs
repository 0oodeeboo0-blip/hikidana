using System.Diagnostics;
using System.Text.Json;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>
/// 発行したアプリ（Hikidana.exe）を本当に動かして、人と同じようにマウスで操作する。
/// 貼り付け先は Edge のテストのページ（ChatGPT や Claude のアプリと同じ Chromium）。
/// 環境変数 HIKIDANA_EXE に exe の場所を入れたときだけ動く（CI では発行のあとに動かす）。
/// </summary>
public sealed class AppTests
{
    private static string DemoRoot => Path.Combine(Path.GetTempPath(), "Hikidana-demo");

    [Fact(WindowsOnly = true)]
    public async Task ClickingTheNewestCardPastesTheScreenshotIntoChromium()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);
        ScreenCapture.Save("app-shelf-open");

        // いちばん上のカード（見本のスクリーンショット、1600×900）の画像のあたりをクリックする
        var card = FirstCardPoint(shelf);
        await TestMouse.ClickAsync(card.X, card.Y, hoverMilliseconds: 400);

        var paste = await page.NextPasteAsync(15000);
        await Task.Delay(300);
        ScreenCapture.Save("app-paste-into-chromium");
        Assert.Equal("paste", paste.Kind, paste.Raw);
        Assert.Equal(1, paste.Files.Count, paste.Raw);
        Assert.Equal("image.png", paste.Files[0].Name, paste.Raw);
        Assert.Equal(1600, paste.Width, paste.Raw);
        Assert.Equal(900, paste.Height, paste.Raw);

        // 棚をクリックしても、Edge が前面のまま（フォーカスを奪っていない）
        Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"前面: {Desktop.ForegroundDescription()}");

        // 貼り付けたら棚は閉じて、知らせが出る
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3)), "棚が閉じていません。");
        Assert.NotEqual((nint)0, await app.WaitForWindowAsync("Hikidana の知らせ", TimeSpan.FromSeconds(3)), "知らせが出ていません。");
        await Task.Delay(800);
        ScreenCapture.Save("app-toast");
    }

    [Fact(WindowsOnly = true)]
    public async Task DraggingTheNewestCardDropsTheScreenshotFileIntoChromium()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);

        // いちばん上のカード（見本のスクリーンショット）を、Edge のページの真ん中へドラッグする
        var viewport = page.Viewport();
        var target = (viewport.Left + ((viewport.Right - viewport.Left) / 3), viewport.Top + ((viewport.Bottom - viewport.Top) / 2));
        await TestMouse.DragAsync(FirstCardPoint(shelf), target);

        var drop = await page.NextPasteAsync(15000);
        await Task.Delay(300);
        ScreenCapture.Save("app-drag-into-chromium");
        Assert.Equal("drop", drop.Kind, drop.Raw);
        Assert.Equal(1, drop.Files.Count, drop.Raw);
        Assert.True(drop.Files[0].Name.StartsWith("スクリーンショット ", StringComparison.Ordinal) && drop.Files[0].Name.EndsWith(".png", StringComparison.Ordinal), drop.Raw);
        Assert.Equal("image/png", drop.Files[0].Type, drop.Raw);
        Assert.Equal(1600, drop.Width, drop.Raw);
        Assert.Equal(900, drop.Height, drop.Raw);

        // 落としたら棚は閉じる
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3)), "棚が閉じていません。");
    }

    [Fact(WindowsOnly = true)]
    public async Task DraggingTextFromChromiumOntoTheHandleStoresIt()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        Assert.True(TestNative.GetWindowRect(app.Handle, out var handle));

        // ページの右上の「ドラッグできる文字」を、画面の端の取っ手まで運んで落とす
        var viewport = page.Viewport();
        var source = (viewport.Right - 24 - 150, viewport.Top + 84 + 28);
        var strip = (handle.Right - 3, (handle.Top + handle.Bottom) / 2);
        await TestMouse.DragAsync(source, strip, steps: 40);

        var stored = await Desktop.WaitUntilAsync(() => DemoPreviews().Contains("Edge からドラッグしたテキスト"), TimeSpan.FromSeconds(8));
        ScreenCapture.Save("app-drop-onto-handle");
        Assert.True(stored, $"棚にしまわれていません（いまの棚: {string.Join(" / ", DemoPreviews().Take(3))}）。");
        Assert.NotEqual((nint)0, await app.WaitForWindowAsync("Hikidana の知らせ", TimeSpan.FromSeconds(3)), "知らせが出ていません。");
    }

    [Fact(WindowsOnly = true)]
    public async Task HoveringTheHandleOpensTheShelfAndLeavingClosesIt()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync("--hover");
        await app.FocusAsync(page);
        Assert.True(TestNative.GetWindowRect(app.Handle, out var handle));

        // 取っ手にマウスを乗せたままにすると開く（クリックはしない）
        TestMouse.MoveTo(handle.Right - 3, (handle.Top + handle.Bottom) / 2);
        var shelf = await app.WaitForWindowAsync("Hikidana", TimeSpan.FromSeconds(3));
        Assert.NotEqual((nint)0, shelf, "取っ手にマウスを乗せても、棚が開きませんでした。");
        Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"前面: {Desktop.ForegroundDescription()}");
        await Task.Delay(400);
        ScreenCapture.Save("app-hover-open");

        // 棚の上に動かしてから、Edge のページへ離れると閉じる
        Assert.True(TestNative.GetWindowRect(shelf, out var rect));
        TestMouse.MoveTo((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        await Task.Delay(800);
        Assert.True(TestNative.IsWindowVisible(shelf), "棚の上にマウスがあるのに閉じました。");
        var viewport = page.Viewport();
        TestMouse.MoveTo((viewport.Left + viewport.Right) / 2, (viewport.Top + viewport.Bottom) / 2);
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(4)), "棚からマウスを離しても、閉じませんでした。");
    }

    [Fact(WindowsOnly = true)]
    public async Task MoreMenuShowsItsItems()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);
        Assert.True(TestNative.GetWindowRect(shelf, out var rect));
        var scale = (rect.Right - rect.Left) / 360.0;

        // 上の帯の「…」（右から 2 つ目のボタン）を押して、メニューの字が読めるか画面写真に残す
        await TestMouse.ClickAsync(rect.Right - (int)(53 * scale), rect.Top + (int)(25 * scale), hoverMilliseconds: 300);
        await Task.Delay(700);
        ScreenCapture.Save("app-more-menu");
        var menu = await Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)app.ProcessId && Desktop.ClassOf(w).StartsWith("HwndWrapper", StringComparison.Ordinal) && w != shelf && Desktop.TitleOf(w).Length == 0, TimeSpan.FromSeconds(2));
        Assert.NotEqual((nint)0, menu, "「…」のメニューが出ませんでした。");
    }

    /// <summary>棚のいちばん上のカードの、画像のあたり（本当の画素）。</summary>
    private static (int X, int Y) FirstCardPoint(nint shelf)
    {
        Assert.True(TestNative.GetWindowRect(shelf, out var rect));
        var scale = (rect.Right - rect.Left) / 360.0;
        var point = (rect.Left + ((rect.Right - rect.Left) / 2), rect.Top + (int)(230 * scale));
        Console.WriteLine($"       棚: ({rect.Left}, {rect.Top})-({rect.Right}, {rect.Bottom})、カード {point}");
        return point;
    }

    /// <summary>見本の棚にしまってあるものの見出し（アプリが書いている途中でも読めるよう、索引のファイルを直接読む）。</summary>
    private static IReadOnlyList<string> DemoPreviews()
    {
        try
        {
            using var stream = new FileStream(Path.Combine(DemoRoot, "clips.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("Items").EnumerateArray()
                .Select(item => item.TryGetProperty("Preview", out var preview) ? preview.GetString() ?? string.Empty : string.Empty)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>テストのために動かしたアプリ（見本の中身で）。</summary>
    private sealed class RunningApp : IDisposable
    {
        private readonly Process process;

        private RunningApp(Process process, nint handle)
        {
            this.process = process;
            Handle = handle;
        }

        /// <summary>画面の端の取っ手のウインドウ。</summary>
        public nint Handle { get; }

        public int ProcessId => process.Id;

        public static async Task<RunningApp> StartAsync(params string[] extra)
        {
            var exe = Environment.GetEnvironmentVariable("HIKIDANA_EXE");
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                throw new SkipException("HIKIDANA_EXE がありません（発行したアプリで試すテスト）。");
            }

            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var argument in new[] { "--demo", "--theme", "light" }.Concat(extra))
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start) ?? throw new AssertionException("アプリを起動できませんでした。");
            var handle = await Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)process.Id && Desktop.TitleOf(w) == "Hikidana の取っ手", TimeSpan.FromSeconds(30));
            if (handle == 0)
            {
                ScreenCapture.Save("failed-app-start");
                var state = process.HasExited ? $"終了コード {process.ExitCode} で終わっています" : "動いています";
                process.Dispose();
                throw new AssertionException($"取っ手が出てきませんでした（アプリは{state}）。");
            }

            // 見本をしまい終えて落ち着くまで待つ
            await Task.Delay(1000);
            return new RunningApp(process, handle);
        }

        /// <summary>Edge を前面にする（利用者が ChatGPT の入力欄で作業している様子）。</summary>
        public async Task FocusAsync(ChromiumPage page)
        {
            InputSender.TapMaskKey();
            TestNative.SetForegroundWindow(page.Window);
            Assert.True(await Desktop.WaitUntilAsync(() => TestNative.GetForegroundWindow() == page.Window, TimeSpan.FromSeconds(5)), $"Edge を前面にできませんでした（前面: {Desktop.ForegroundDescription()}）。");
        }

        /// <summary>Edge を前面にしてから、取っ手をクリックして棚を開く。</summary>
        public async Task<nint> OpenShelfAsync(ChromiumPage page)
        {
            await FocusAsync(page);

            Assert.True(TestNative.GetWindowRect(Handle, out var rect));
            Console.WriteLine($"       取っ手: ({rect.Left}, {rect.Top})-({rect.Right}, {rect.Bottom})");
            await TestMouse.ClickAsync(rect.Right - 3, (rect.Top + rect.Bottom) / 2, hoverMilliseconds: 600);
            var shelf = await WaitForWindowAsync("Hikidana", TimeSpan.FromSeconds(5));
            if (shelf == 0)
            {
                ScreenCapture.Save("failed-app-open");
                throw new AssertionException($"取っ手をクリックしても、棚が開きませんでした（前面: {Desktop.ForegroundDescription()}）。");
            }

            Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"棚を開いたら前面が変わりました: {Desktop.ForegroundDescription()}");
            await Task.Delay(500);
            return shelf;
        }

        public Task<nint> WaitForWindowAsync(string title, TimeSpan timeout) =>
            Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)process.Id && Desktop.TitleOf(w) == title, timeout);

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }
    }
}
