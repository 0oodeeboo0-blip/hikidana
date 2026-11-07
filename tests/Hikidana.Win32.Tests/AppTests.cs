using System.Diagnostics;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>
/// 発行したアプリ（Hikidana.exe）を本当に動かして、棚のカードをマウスでクリックし、Edge のテストのページに貼り付ける。
/// 環境変数 HIKIDANA_EXE に exe の場所を入れたときだけ動く（CI では発行のあとに動かす）。
/// 棚をクリックしても Edge が前面のまま（フォーカスを奪わない）ことも確かめる。
/// </summary>
public sealed class AppTests
{
    [Fact(WindowsOnly = true)]
    public async Task ClickingTheNewestCardPastesTheScreenshotIntoChromium()
    {
        var exe = Environment.GetEnvironmentVariable("HIKIDANA_EXE");
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            throw new SkipException("HIKIDANA_EXE がありません（発行したアプリで試すテスト）。");
        }

        var page = await ChromiumPage.SharedAsync();
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var argument in new[] { "--demo", "--open", "--theme", "light" })
        {
            start.ArgumentList.Add(argument);
        }

        using var app = Process.Start(start) ?? throw new AssertionException("アプリを起動できませんでした。");
        try
        {
            var shelf = await Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)app.Id && Desktop.TitleOf(w) == "Hikidana", TimeSpan.FromSeconds(30));
            if (shelf == 0)
            {
                ScreenCapture.Save("failed-app-start");
                throw new AssertionException($"棚が出てきませんでした（アプリは{(app.HasExited ? $"終了コード {app.ExitCode} で終わっています" : "動いています")}）。");
            }

            // 利用者が ChatGPT の入力欄で作業している様子: Edge を前面にする
            await Task.Delay(1500);
            InputSender.TapMaskKey();
            TestNative.SetForegroundWindow(page.Window);
            Assert.True(await Desktop.WaitUntilAsync(() => TestNative.GetForegroundWindow() == page.Window, TimeSpan.FromSeconds(5)), $"Edge を前面にできませんでした（前面: {Desktop.ForegroundDescription()}）。");
            ScreenCapture.Save("app-before-click");

            // いちばん上のカード（見本のスクリーンショット、1600×900）の画像のあたりをクリックする
            Assert.True(TestNative.GetWindowRect(shelf, out var rect));
            var scale = (rect.Right - rect.Left) / 360.0;
            var x = rect.Left + (int)((rect.Right - rect.Left) / 2.0);
            var y = rect.Top + (int)(150 * scale);
            Console.WriteLine($"       棚: ({rect.Left}, {rect.Top})-({rect.Right}, {rect.Bottom})、クリック ({x}, {y})");
            TestNative.SetCursorPos(x, y);
            await Task.Delay(400);
            TestNative.mouse_event(TestNative.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            await Task.Delay(60);
            TestNative.mouse_event(TestNative.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

            var paste = await page.NextPasteAsync(15000);
            await Task.Delay(300);
            ScreenCapture.Save("app-paste-into-chromium");
            Assert.Equal(1, paste.Files.Count, paste.Raw);
            Assert.Equal("image.png", paste.Files[0].Name, paste.Raw);
            Assert.Equal(1600, paste.Width, paste.Raw);
            Assert.Equal(900, paste.Height, paste.Raw);

            // 棚をクリックしても、Edge が前面のまま（フォーカスを奪っていない）
            Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"前面: {Desktop.ForegroundDescription()}");

            // 貼り付けたら棚は閉じて、知らせが出る
            Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3)), "棚が閉じていません。");
            var toast = await Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)app.Id && Desktop.TitleOf(w) == "Hikidana の知らせ", TimeSpan.FromSeconds(3));
            Assert.NotEqual((nint)0, toast, "知らせが出ていません。");
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill();
                app.WaitForExit(5000);
            }
        }
    }
}
