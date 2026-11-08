using Hikidana.Core.Layout;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Layout;

public sealed class FullscreenRuleTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void WindowCoveringTheWholeScreenIsFullscreen()
    {
        Assert.True(FullscreenRule.Covers(Monitor, Monitor, maximized: false, minimized: false, visible: true, "Chrome_WidgetWin_1"));

        // 少しはみ出していても（枠のぶん）全画面
        Assert.True(FullscreenRule.Covers(new PixelRect(-8, -8, 1936, 1096), Monitor, false, false, true, "UnityWndClass"));

        // 2 つ目の画面（左にある）
        var second = new PixelRect(-2560, 0, 2560, 1440);
        Assert.True(FullscreenRule.Covers(second, second, false, false, true, "ApplicationFrameWindow"));
        Assert.False(FullscreenRule.Covers(second, Monitor, false, false, true, "ApplicationFrameWindow"), "ほかの画面の全画面");
    }

    [Fact]
    public void OrdinaryWindowsAreNotFullscreen()
    {
        // 最大化（タスクバーを自動で隠す設定だと、画面いっぱいになる）
        Assert.False(FullscreenRule.Covers(new PixelRect(-8, -8, 1936, 1096), Monitor, maximized: true, minimized: false, visible: true, "Chrome_WidgetWin_1"));

        // 作業領域だけの大きさ（タスクバーは見えている）
        Assert.False(FullscreenRule.Covers(new PixelRect(0, 0, 1920, 1032), Monitor, false, false, true, "Notepad"));

        // デスクトップとタスクバー、最小化、見えないもの
        Assert.False(FullscreenRule.Covers(Monitor, Monitor, false, false, true, "Progman"));
        Assert.False(FullscreenRule.Covers(Monitor, Monitor, false, false, true, "WorkerW"));
        Assert.False(FullscreenRule.Covers(Monitor, Monitor, false, minimized: true, true, "Notepad"));
        Assert.False(FullscreenRule.Covers(Monitor, Monitor, false, false, visible: false, "Notepad"));
        Assert.False(FullscreenRule.Covers(Monitor, default, false, false, true, "Notepad"));
    }
}
