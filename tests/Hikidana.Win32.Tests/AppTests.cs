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
    public async Task ClickingOutsideOrPressingEscapeClosesTheShelf()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();

        // 作業中のアプリ（Edge）をそのままクリックしても閉じる。Edge は前面のまま
        var shelf = await app.OpenShelfAsync(page);
        var viewport = page.Viewport();
        await TestMouse.ClickAsync(viewport.Left + 60, viewport.Bottom - 60, hoverMilliseconds: 200);
        var closedByClick = await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3));
        ScreenCapture.Save("app-click-outside");
        Assert.True(closedByClick, "棚の外（作業中のアプリ）をクリックしても、棚が閉じませんでした。");
        Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"前面: {Desktop.ForegroundDescription()}");

        // もう一度開いて、Esc で閉じる
        shelf = await app.OpenShelfAsync(page);
        TestKeys.Down(TestKeys.VK_ESCAPE);
        await Task.Delay(50);
        TestKeys.Up(TestKeys.VK_ESCAPE);
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3)), "Esc を押しても、棚が閉じませんでした。");
        Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"前面: {Desktop.ForegroundDescription()}");
    }

    [Fact(WindowsOnly = true)]
    public async Task ClickingOutsideStillClosesAfterUsingTheMenus()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);
        var viewport = page.Viewport();
        var outside = (X: viewport.Left + 60, Y: viewport.Bottom - 60);

        // カードを右クリックして「コピーだけする」を押す（メニューは閉じて、棚は開いたまま）
        var card = FirstCardPoint(shelf);
        await TestMouse.RightClickAsync(card.X, card.Y);
        var menu = await WaitForMenuAsync(app, shelf);
        if (await Automation.WaitForNamedCenterAsync(menu, "コピーだけする", TimeSpan.FromSeconds(3)) is not { } copy)
        {
            ScreenCapture.Save("failed-app-menus");
            throw new AssertionException("右クリックのメニューに「コピーだけする」が見つかりません。");
        }

        await TestMouse.ClickAsync(copy.X, copy.Y, hoverMilliseconds: 300);
        await Task.Delay(800);

        // 「…」のメニューを開いたまま外をクリックすると、メニューだけが閉じる（棚は閉じない）
        if (await Automation.WaitForCenterAsync(shelf, "MoreButton", TimeSpan.FromSeconds(3)) is not { } more)
        {
            throw new AssertionException("棚に「…」のボタンが見つかりません。");
        }

        await TestMouse.ClickAsync(more.X, more.Y, hoverMilliseconds: 300);
        var moreMenu = await WaitForMenuAsync(app, shelf);
        await Task.Delay(400);
        await TestMouse.ClickAsync(outside.X, outside.Y, hoverMilliseconds: 200);
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(moreMenu), TimeSpan.FromSeconds(3)), "外をクリックしても、メニューが閉じませんでした。");
        await Task.Delay(800);
        Assert.True(TestNative.IsWindowVisible(shelf), "メニューを閉じるためのクリックで、棚まで閉じました。");

        // もう一度外をクリックすると、棚が閉じる
        await TestMouse.ClickAsync(outside.X, outside.Y, hoverMilliseconds: 200);
        var closed = await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3));
        if (!closed)
        {
            ScreenCapture.Save("failed-app-menus");
            PrintDemoLog();
        }

        Assert.True(closed, "メニューを使ったあと、棚の外をクリックしても閉じませんでした。");

        // 「…」の「設定」で閉じたあとも、開き直した棚は外のクリックで閉じる
        shelf = await app.OpenShelfAsync(page);
        if (await Automation.WaitForCenterAsync(shelf, "MoreButton", TimeSpan.FromSeconds(3)) is not { } more2)
        {
            throw new AssertionException("棚に「…」のボタンが見つかりません。");
        }

        await TestMouse.ClickAsync(more2.X, more2.Y, hoverMilliseconds: 300);
        var menu2 = await WaitForMenuAsync(app, shelf);
        if (await Automation.WaitForCenterAsync(menu2, "SettingsItem", TimeSpan.FromSeconds(3)) is not { } settingsItem)
        {
            throw new AssertionException("「…」のメニューに「設定」が見つかりません。");
        }

        await TestMouse.ClickAsync(settingsItem.X, settingsItem.Y, hoverMilliseconds: 300);
        var settings = await app.WaitForWindowAsync("Hikidana の設定", TimeSpan.FromSeconds(5));
        Assert.NotEqual((nint)0, settings, "「設定」を押しても、設定の画面が出ませんでした。");
        await Task.Delay(500);
        TestNative.SendMessage(settings, 0x0010, 0, 0);
        await Task.Delay(500);

        shelf = await app.OpenShelfAsync(page);
        await TestMouse.ClickAsync(outside.X, outside.Y, hoverMilliseconds: 200);
        var closedAgain = await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3));
        if (!closedAgain)
        {
            ScreenCapture.Save("failed-app-menus-settings");
            PrintDemoLog();
        }

        Assert.True(closedAgain, "設定を開いたあと、開き直した棚が外のクリックで閉じませんでした。");
    }

    /// <summary>このアプリのメニュー（題名の無い WPF のポップアップ）が出るまで待つ。</summary>
    private static async Task<nint> WaitForMenuAsync(RunningApp app, nint shelf)
    {
        var menu = await Desktop.WaitForWindowAsync(
            w => Desktop.ProcessIdOf(w) == (uint)app.ProcessId && Desktop.ClassOf(w).StartsWith("HwndWrapper", StringComparison.Ordinal)
                && w != shelf && Desktop.TitleOf(w).Length == 0 && TestNative.IsWindowVisible(w),
            TimeSpan.FromSeconds(3));
        if (menu == 0)
        {
            ScreenCapture.Save("failed-app-menus");
            throw new AssertionException("メニューが出ませんでした。");
        }

        return menu;
    }

    [Fact(WindowsOnly = true)]
    public async Task HotkeyOpensTheShelfForTheKeyboardAndEnterPastes()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        await app.FocusAsync(page);

        // Win+Alt+V で開くと、棚がキーボードを受け取る（検索の欄にいる）。キーは少し押したままにしてから離す
        TestKeys.Down(TestKeys.VK_LWIN);
        TestKeys.Down(TestKeys.VK_LMENU);
        TestKeys.Down(TestKeys.VK_V);
        TestKeys.Up(TestKeys.VK_V);
        var shelf = await app.WaitForWindowAsync("Hikidana", TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        TestKeys.Up(TestKeys.VK_LMENU);
        TestKeys.Up(TestKeys.VK_LWIN);
        Assert.NotEqual((nint)0, shelf, "Win+Alt+V を押しても、棚が開きませんでした。");
        var focused = await Desktop.WaitUntilAsync(() => TestNative.GetForegroundWindow() == shelf, TimeSpan.FromSeconds(3));
        await Task.Delay(300);
        ScreenCapture.Save("app-hotkey-open");
        Assert.True(focused, $"棚がキーボードを受け取っていません（前面: {Desktop.ForegroundDescription()}）。");

        // Enter で、いちばん上（見本のスクリーンショット）を、さっきまで使っていたアプリ（Edge）に貼る
        TestKeys.Down(TestKeys.VK_RETURN);
        TestKeys.Up(TestKeys.VK_RETURN);
        var paste = await page.NextPasteAsync(15000);
        Assert.Equal("paste", paste.Kind, paste.Raw);
        Assert.Equal(1, paste.Files.Count, paste.Raw);
        Assert.Equal("image.png", paste.Files[0].Name, paste.Raw);
        Assert.True(await Desktop.WaitUntilAsync(() => TestNative.GetForegroundWindow() == page.Window, TimeSpan.FromSeconds(3)), $"貼り付けたあと、Edge が前面に戻りません（前面: {Desktop.ForegroundDescription()}）。");
    }

    [Fact(WindowsOnly = true)]
    public async Task PickingTwoCardsPastesBothInThePickedOrder()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);
        var ids = DemoClipIds();
        Assert.True(ids.Count >= 2, "見本が足りません。");

        // 2 番目（テキスト）を先に、いちばん上（スクリーンショット）をあとに、Ctrl を押しながらクリックして選ぶ
        foreach (var id in new[] { ids[1], ids[0] })
        {
            if (await Automation.WaitForCenterAsync(shelf, id, TimeSpan.FromSeconds(3)) is not { } card)
            {
                throw new AssertionException($"カードが見つかりません（{id}）。");
            }

            TestKeys.Down(TestKeys.VK_LCONTROL);
            await TestMouse.ClickAsync(card.X, card.Y, hoverMilliseconds: 250);
            TestKeys.Up(TestKeys.VK_LCONTROL);
            await Task.Delay(200);
        }

        await Task.Delay(300);
        ScreenCapture.Save("app-pick-two");
        Assert.Equal(page.Window, TestNative.GetForegroundWindow(), $"選んでいる間に前面が変わりました: {Desktop.ForegroundDescription()}");
        if (await Automation.WaitForCenterAsync(shelf, "PastePickedButton", TimeSpan.FromSeconds(3)) is not { } button)
        {
            throw new AssertionException("「まとめて貼り付け」が出ていません。");
        }

        // 選んだ順に、テキスト、スクリーンショットの順で貼られる
        await TestMouse.ClickAsync(button.X, button.Y, hoverMilliseconds: 250);
        var first = await page.NextPasteAsync(15000);
        var second = await page.NextPasteAsync(15000);
        await Task.Delay(300);
        ScreenCapture.Save("app-paste-many");
        Assert.Equal("明日の打ち合わせは 10 時からです。会議室は 3 階の A です。", first.Text, first.Raw);
        Assert.Equal(1, second.Files.Count, second.Raw);
        Assert.Equal("image.png", second.Files[0].Name, second.Raw);
        Assert.Equal(1600, second.Width, second.Raw);
    }

    [Fact(WindowsOnly = true)]
    public async Task CopyingSomethingFlashesTheHandle()
    {
        using var app = await RunningApp.StartAsync();
        using var other = new ClipboardWatcher();
        other.Start();
        await Task.Delay(1500);
        var before = DemoClips().Count;

        // ほかのアプリがテキストをコピーすると、棚にしまわれて、取っ手から印が少し出る（画面写真で確かめる）
        Assert.Equal(WriteStatus.Ok, await other.WriteAsync([Hikidana.Core.Paste.PastePlanner.UnicodeText("取っ手の合図のテスト")]));
        var stored = await Desktop.WaitUntilAsync(() => DemoClips().Count == before + 1, TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        ScreenCapture.Save("app-capture-signal");
        Assert.True(stored, "コピーしたものが棚に入りませんでした。");
        Assert.False(app.HasExited);
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

        // 「設定」を押すと、設定の画面が開いて、棚は閉じる
        if (await Automation.WaitForCenterAsync(menu, "SettingsItem", TimeSpan.FromSeconds(3)) is not { } item)
        {
            throw new AssertionException("「…」のメニューに「設定」が見つかりません。");
        }

        await TestMouse.ClickAsync(item.X, item.Y, hoverMilliseconds: 300);
        var settings = await app.WaitForWindowAsync("Hikidana の設定", TimeSpan.FromSeconds(5));
        Assert.NotEqual((nint)0, settings, "「設定」を押しても、設定の画面が出ませんでした。");
        Assert.True(await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(shelf), TimeSpan.FromSeconds(3)), "設定の画面を開いても、棚が閉じませんでした。");
    }

    [Fact(WindowsOnly = true)]
    public async Task ClearingScreenshotsFromTheMenuKeepsTheRestAndCanBeUndone()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        var shelf = await app.OpenShelfAsync(page);
        var before = DemoClips();
        Assert.Equal(1, before.Count(c => c.Kind == "Screenshot" && !c.Pinned), string.Join(", ", before));

        // 「…」→「ピン留め以外のスクリーンショットを消す（1 件）」
        if (await Automation.WaitForCenterAsync(shelf, "MoreButton", TimeSpan.FromSeconds(3)) is not { } more)
        {
            throw new AssertionException("棚に「…」のボタンが見つかりません。");
        }

        await TestMouse.ClickAsync(more.X, more.Y, hoverMilliseconds: 300);
        var menu = await Desktop.WaitForWindowAsync(w => Desktop.ProcessIdOf(w) == (uint)app.ProcessId && Desktop.ClassOf(w).StartsWith("HwndWrapper", StringComparison.Ordinal) && w != shelf && Desktop.TitleOf(w).Length == 0, TimeSpan.FromSeconds(2));
        Assert.NotEqual((nint)0, menu, "「…」のメニューが出ませんでした。");
        if (await Automation.WaitForCenterAsync(menu, "ClearScreenshotsItem", TimeSpan.FromSeconds(3)) is not { } item)
        {
            throw new AssertionException("「…」のメニューに「ピン留め以外のスクリーンショットを消す」が見つかりません。");
        }

        await Task.Delay(300);
        ScreenCapture.Save("app-clear-menu");
        await TestMouse.ClickAsync(item.X, item.Y, hoverMilliseconds: 300);

        // スクリーンショットだけが消えて、ほかは残る。知らせが出る
        // （CI で、起動してすぐのメニューがクリックを受けないことがまれにあるので、メニューが開いたままなら、もう一度押す）
        bool Cleared() => DemoClips() is { Count: > 0 } now && now.All(c => c.Kind != "Screenshot");
        var cleared = await Desktop.WaitUntilAsync(Cleared, TimeSpan.FromSeconds(2));
        if (!cleared && TestNative.IsWindowVisible(menu) && await Automation.WaitForCenterAsync(menu, "ClearScreenshotsItem", TimeSpan.FromSeconds(1)) is { } again)
        {
            Console.WriteLine("       メニューがクリックを受けなかったので、もう一度押します。");
            ScreenCapture.Save("failed-app-clear-first");
            TestMouse.MoveTo(again.X - 20, again.Y);
            await TestMouse.ClickAsync(again.X, again.Y, hoverMilliseconds: 500);
            cleared = await Desktop.WaitUntilAsync(Cleared, TimeSpan.FromSeconds(3));
        }
        var after = DemoClips();
        if (!cleared)
        {
            ScreenCapture.Save("failed-app-clear");
            PrintDemoLog();
        }

        Assert.True(cleared, $"スクリーンショットが消えていません（前面: {Desktop.ForegroundDescription()}、棚: {TestNative.IsWindowVisible(shelf)}）: {string.Join(", ", after)}");
        Assert.Equal(before.Count - 1, after.Count, string.Join(", ", after));
        var toast = await app.WaitForWindowAsync("Hikidana の知らせ", TimeSpan.FromSeconds(3));
        Assert.NotEqual((nint)0, toast, "知らせが出ていません。");
        await Task.Delay(500);
        ScreenCapture.Save("app-clear-screenshots");

        // 知らせの「元に戻す」で戻る
        if (await Automation.WaitForCenterAsync(toast, "ActionButton", TimeSpan.FromSeconds(3)) is not { } undo)
        {
            throw new AssertionException("知らせに「元に戻す」が見つかりません。");
        }

        await TestMouse.ClickAsync(undo.X, undo.Y, hoverMilliseconds: 300);
        var restored = await Desktop.WaitUntilAsync(() => DemoClips().Count(c => c.Kind == "Screenshot") == 1, TimeSpan.FromSeconds(3));
        Assert.True(restored, $"元に戻せませんでした: {string.Join(", ", DemoClips())}");
        Assert.Equal(before.Count, DemoClips().Count);
    }

    [Fact(WindowsOnly = true)]
    public async Task SettingsMoveTheHandleToTheLeftEdgeAndRememberIt()
    {
        using var app = await RunningApp.StartAsync("--settings");
        var window = await app.WaitForWindowAsync("Hikidana の設定", TimeSpan.FromSeconds(10));
        Assert.NotEqual((nint)0, window, "--settings で起動しても、設定の画面が出ませんでした。");
        await Task.Delay(800);
        Assert.True(TestNative.GetWindowRect(app.Handle, out var before));
        Console.WriteLine($"       取っ手: ({before.Left}, {before.Top})-({before.Right}, {before.Bottom})");

        // 「左」のボタンを押すと、取っ手がすぐ左の端へ動く
        if (await Automation.WaitForCenterAsync(window, "EdgeLeft", TimeSpan.FromSeconds(5)) is not { } left)
        {
            ScreenCapture.Save("failed-app-settings");
            throw new AssertionException("設定の画面に「左」のボタンが見つかりません。");
        }

        await TestMouse.ClickAsync(left.X, left.Y, hoverMilliseconds: 300);
        TestNative.RECT moved = default;
        var movedLeft = await Desktop.WaitUntilAsync(() => TestNative.GetWindowRect(app.Handle, out moved) && moved.Right < before.Left - 200, TimeSpan.FromSeconds(3));
        await Task.Delay(500);
        ScreenCapture.Save("app-settings-left");
        Assert.True(movedLeft, $"取っ手が左の端へ動きませんでした（いま ({moved.Left}, {moved.Top})-({moved.Right}, {moved.Bottom})）。");
        Console.WriteLine($"       動いた取っ手: ({moved.Left}, {moved.Top})-({moved.Right}, {moved.Bottom})");

        // 左の「動作」を押してから、開き方を「マウスを乗せたとき」にする
        if (await Automation.WaitForCenterAsync(window, "NavBehavior", TimeSpan.FromSeconds(3)) is not { } behavior)
        {
            throw new AssertionException("設定の画面の左に「動作」が見つかりません。");
        }

        await TestMouse.ClickAsync(behavior.X, behavior.Y, hoverMilliseconds: 300);
        await Task.Delay(400);
        ScreenCapture.Save("app-settings-behavior");
        if (await Automation.WaitForCenterAsync(window, "OpenByHover", TimeSpan.FromSeconds(3)) is not { } hover)
        {
            throw new AssertionException("設定の画面に「取っ手にマウスを乗せたとき」が見つかりません。");
        }

        await TestMouse.ClickAsync(hover.X, hover.Y, hoverMilliseconds: 300);

        // どちらも設定のファイルに残る（次に起動したときも同じ）
        string? edge = null;
        var openOnHover = false;
        var saved = await Desktop.WaitUntilAsync(
            () =>
            {
                (edge, openOnHover) = DemoSettings();
                return string.Equals(edge, "left", StringComparison.OrdinalIgnoreCase) && openOnHover;
            },
            TimeSpan.FromSeconds(3));
        Assert.True(saved, $"設定のファイルに残っていません（edge: {edge}、openOnHover: {openOnHover}）。");
    }

    [Fact(WindowsOnly = true)]
    public async Task SettingsResizeTheHandleAndTheShelf()
    {
        using var app = await RunningApp.StartAsync("--settings", "position");
        var window = await app.WaitForWindowAsync("Hikidana の設定", TimeSpan.FromSeconds(10));
        Assert.NotEqual((nint)0, window, "--settings で起動しても、設定の画面が出ませんでした。");
        await Task.Delay(800);
        Assert.True(TestNative.GetWindowRect(app.Handle, out var before));

        // 取っ手を「大」にすると、すぐ長くなる
        if (await ScrollIntoViewAsync(window, "HandleLarge") is not { } large)
        {
            ScreenCapture.Save("failed-app-settings-sizes");
            throw new AssertionException("設定の画面に、取っ手の大きさの「大」が見つかりません。");
        }

        await TestMouse.ClickAsync(large.X, large.Y, hoverMilliseconds: 300);
        TestNative.RECT after = default;
        var grew = await Desktop.WaitUntilAsync(
            () => TestNative.GetWindowRect(app.Handle, out after) && after.Bottom - after.Top > (before.Bottom - before.Top) * 1.4,
            TimeSpan.FromSeconds(3));
        Assert.True(grew, $"取っ手が長くなりません（前 {before.Bottom - before.Top}、いま {after.Bottom - after.Top}）。");

        // 棚の長さを「小」にする。どちらも設定のファイルに残る
        if (await ScrollIntoViewAsync(window, "PanelSmall") is not { } small)
        {
            ScreenCapture.Save("failed-app-settings-sizes");
            throw new AssertionException("設定の画面に、棚の長さの「小」が見つかりません。");
        }

        await TestMouse.ClickAsync(small.X, small.Y, hoverMilliseconds: 300);
        var saved = await Desktop.WaitUntilAsync(() => DemoSetting("handleSize") == "large" && DemoSetting("panelSize") == "small", TimeSpan.FromSeconds(3));
        await Task.Delay(400);
        ScreenCapture.Save("app-settings-sizes");
        Assert.True(saved, $"設定のファイルに残っていません（handleSize: {DemoSetting("handleSize")}、panelSize: {DemoSetting("panelSize")}）。");

        // 棚を開くと、画面の半分くらいの長さで、取っ手の横に出る
        await TestMouse.ClickAsync(after.Right - 3, (after.Top + after.Bottom) / 2, hoverMilliseconds: 600);
        var shelf = await app.WaitForWindowAsync("Hikidana", TimeSpan.FromSeconds(5));
        Assert.NotEqual((nint)0, shelf, "取っ手をクリックしても、棚が開きませんでした。");
        await Task.Delay(600);
        ScreenCapture.Save("app-shelf-small");
        Assert.True(TestNative.GetWindowRect(shelf, out var panel));
        var screen = TestNative.GetSystemMetrics(1);
        var height = panel.Bottom - panel.Top;
        var middle = (after.Top + after.Bottom) / 2;
        Assert.True(height > screen * 0.3 && height < screen * 0.6, $"棚の長さが半分くらいになっていません（棚 {height}、画面 {screen}）。");
        Assert.True(panel.Top <= middle && panel.Bottom >= middle, $"棚が取っ手の横にありません（棚 {panel.Top}〜{panel.Bottom}、取っ手の真ん中 {middle}）。");
    }

    [Fact(WindowsOnly = true)]
    public async Task FullscreenAppHidesTheHandleUntilItEnds()
    {
        var page = await ChromiumPage.SharedAsync();
        using var app = await RunningApp.StartAsync();
        await app.FocusAsync(page);
        Assert.True(TestNative.IsWindowVisible(app.Handle), "はじめから取っ手が見えていません。");

        // テストのページの「全画面」を押す（動画を全画面で見るときと同じ、ページの全画面）
        var viewport = page.Viewport();
        await TestMouse.ClickAsync(viewport.Left + 24 + 70, viewport.Top + 84 + 22, hoverMilliseconds: 300);
        var screen = (Width: TestNative.GetSystemMetrics(0), Height: TestNative.GetSystemMetrics(1));
        try
        {
            var hidden = await Desktop.WaitUntilAsync(() => !TestNative.IsWindowVisible(app.Handle), TimeSpan.FromSeconds(5));
            await Task.Delay(600);
            ScreenCapture.Save("app-fullscreen");
            Assert.True(TestNative.GetWindowRect(page.Window, out var full));
            Assert.True(
                hidden,
                $"全画面にしても、取っ手が隠れませんでした（Edge ({full.Left}, {full.Top})-({full.Right}, {full.Bottom})、画面 {screen.Width}×{screen.Height}、前面: {Desktop.ForegroundDescription()}）。");
        }
        finally
        {
            // Esc で全画面をやめる（やめられなければ、もう一度「全画面」を押す）
            TestKeys.Down(TestKeys.VK_ESCAPE);
            await Task.Delay(60);
            TestKeys.Up(TestKeys.VK_ESCAPE);
            var ended = await Desktop.WaitUntilAsync(() => TestNative.GetWindowRect(page.Window, out var r) && r.Right - r.Left < screen.Width, TimeSpan.FromSeconds(3));
            if (!ended)
            {
                await TestMouse.ClickAsync(24 + 70, 84 + 22, hoverMilliseconds: 300);
                await Desktop.WaitUntilAsync(() => TestNative.GetWindowRect(page.Window, out var r) && r.Right - r.Left < screen.Width, TimeSpan.FromSeconds(3));
            }
        }

        var shown = await Desktop.WaitUntilAsync(() => TestNative.IsWindowVisible(app.Handle), TimeSpan.FromSeconds(5));
        Assert.True(shown, "全画面をやめても、取っ手が出てきません。");
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

    [Fact(WindowsOnly = true)]
    public async Task SettingsSwitchToDarkAtOnceAndTheShelfFollows()
    {
        using var app = await RunningApp.StartAsync("--settings", "appearance");
        var window = await app.WaitForWindowAsync("Hikidana の設定", TimeSpan.FromSeconds(10));
        Assert.NotEqual((nint)0, window, "--settings で起動しても、設定の画面が出ませんでした。");
        await Task.Delay(800);

        // 「色」の一覧が見えるところまで下へ送ってから、開いて「ダーク」を選ぶ
        if (await ScrollIntoViewAsync(window, "ThemeBox") is not { } box)
        {
            ScreenCapture.Save("failed-app-settings-theme");
            throw new AssertionException("設定の画面に「色」の一覧が見つかりません。");
        }

        await TestMouse.ClickAsync(box.X, box.Y, hoverMilliseconds: 300);
        await Task.Delay(500);
        if (await Automation.WaitForNamedCenterAsync(window, "ダーク", TimeSpan.FromSeconds(3)) is not { } dark)
        {
            ScreenCapture.Save("failed-app-settings-theme");
            throw new AssertionException("「色」の一覧に「ダーク」が見つかりません。");
        }

        await TestMouse.ClickAsync(dark.X, dark.Y, hoverMilliseconds: 300);
        string? theme = null;
        var saved = await Desktop.WaitUntilAsync(
            () =>
            {
                theme = DemoSetting("theme");
                return theme == "dark";
            },
            TimeSpan.FromSeconds(3));
        await Task.Delay(800);
        ScreenCapture.Save("app-settings-dark-switch");
        Assert.True(saved, $"色を変えても、設定のファイルが変わりません（theme: {theme}）。");

        // 棚を開くと、棚もダークになっている（画面写真で確かめる）。アプリは動き続けている
        Assert.True(TestNative.GetWindowRect(app.Handle, out var rect));
        await TestMouse.ClickAsync(rect.Right - 3, (rect.Top + rect.Bottom) / 2, hoverMilliseconds: 600);
        var shelf = await app.WaitForWindowAsync("Hikidana", TimeSpan.FromSeconds(5));
        Assert.NotEqual((nint)0, shelf, "色を変えたあと、棚が開きませんでした。");
        await Task.Delay(600);
        ScreenCapture.Save("app-shelf-after-dark-switch");
        Assert.False(app.HasExited, "色を変えたあと、アプリが終了しました。");
    }

    /// <summary>
    /// 設定の画面などで、部品が窓の中に見えるまでホイールで下へ送り、その真ん中（本当の画素）を返す。見つからなければ null。
    /// </summary>
    private static async Task<(int X, int Y)?> ScrollIntoViewAsync(nint window, string automationId)
    {
        Assert.True(TestNative.GetWindowRect(window, out var frame));
        for (var i = 0; i < 12; i++)
        {
            if (Automation.FindBounds(window, automationId) is { } rect && rect.Bottom > rect.Top)
            {
                if (rect.Top >= frame.Top + 40 && rect.Bottom <= frame.Bottom - 12)
                {
                    return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                }

                TestMouse.Wheel((frame.Left + frame.Right) / 2, (frame.Top + frame.Bottom) / 2, rect.Top < frame.Top + 40 ? 2 : -2);
            }

            await Task.Delay(250);
        }

        return null;
    }

    /// <summary>見本の記録（ログ）の終わりのほうを出す（失敗したときに、アプリの中で何が起きたかを見るため）。</summary>
    private static void PrintDemoLog()
    {
        try
        {
            var folder = Path.Combine(DemoRoot, "logs");
            var latest = Directory.Exists(folder) ? Directory.GetFiles(folder).OrderBy(File.GetLastWriteTimeUtc).LastOrDefault() : null;
            if (latest is null)
            {
                Console.WriteLine("       （見本の記録がありません）");
                return;
            }

            using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            foreach (var line in reader.ReadToEnd().Split('\n').TakeLast(30))
            {
                Console.WriteLine($"       記録: {line.TrimEnd()}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"       （見本の記録を読めません: {ex.Message}）");
        }
    }

    /// <summary>見本の棚にしまってあるものの Id（新しい順。索引のファイルを直接読む）。読めなければ空。</summary>
    private static IReadOnlyList<string> DemoClipIds()
    {
        try
        {
            using var stream = new FileStream(Path.Combine(DemoRoot, "clips.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("Items").EnumerateArray()
                .Select(item => item.TryGetProperty("Id", out var id) ? id.GetString() ?? string.Empty : string.Empty)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>見本の棚にしまってあるものの種類とピン留め（索引のファイルを直接読む）。読めなければ空。</summary>
    private static IReadOnlyList<(string Kind, bool Pinned)> DemoClips()
    {
        try
        {
            using var stream = new FileStream(Path.Combine(DemoRoot, "clips.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("Items").EnumerateArray()
                .Select(item => (
                    item.TryGetProperty("Kind", out var kind) ? kind.ToString() : string.Empty,
                    item.TryGetProperty("Pinned", out var pinned) && pinned.ValueKind == JsonValueKind.True))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>見本の設定のファイルの、文字の値（例: theme）。読めなければ null。</summary>
    private static string? DemoSetting(string name)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(DemoRoot, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>見本の設定のファイルの、棚の端と「マウスを乗せるだけで開く」。読めなければ (null, false)。</summary>
    private static (string? Edge, bool OpenOnHover) DemoSettings()
    {
        try
        {
            using var stream = new FileStream(Path.Combine(DemoRoot, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var edge = root.TryGetProperty("edge", out var e) ? e.GetString() : null;
            var hover = root.TryGetProperty("openOnHover", out var h) && h.ValueKind == JsonValueKind.True;
            return (edge, hover);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return (null, false);
        }
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

        public bool HasExited => process.HasExited;

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
