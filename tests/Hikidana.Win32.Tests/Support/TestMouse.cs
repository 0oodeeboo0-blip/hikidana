using static Hikidana.Win32.Tests.Support.TestNative;

namespace Hikidana.Win32.Tests.Support;

/// <summary>テストでマウスを動かす（本物の入力として。ドラッグの仕組みにも届く）。座標はメインの画面の本当の画素。</summary>
internal static class TestMouse
{
    public static void MoveTo(int x, int y)
    {
        var width = Math.Max(2, GetSystemMetrics(SM_CXSCREEN));
        var height = Math.Max(2, GetSystemMetrics(SM_CYSCREEN));
        var dx = (int)Math.Round(x * 65535.0 / (width - 1));
        var dy = (int)Math.Round(y * 65535.0 / (height - 1));
        mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, dx, dy, 0, 0);
    }

    public static async Task ClickAsync(int x, int y, int hoverMilliseconds = 300)
    {
        MoveTo(x, y);
        await Task.Delay(hoverMilliseconds);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        await Task.Delay(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    /// <summary>押したまま、少しずつ動かして離す。</summary>
    public static async Task DragAsync((int X, int Y) from, (int X, int Y) to, int steps = 30, int stepMilliseconds = 25, int holdMilliseconds = 600)
    {
        MoveTo(from.X, from.Y);
        await Task.Delay(300);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        await Task.Delay(150);
        for (var i = 1; i <= steps; i++)
        {
            var x = from.X + ((to.X - from.X) * i / steps);
            var y = from.Y + ((to.Y - from.Y) * i / steps);
            MoveTo(x, y);
            await Task.Delay(stepMilliseconds);
        }

        // 落とす先に「ここに置ける」と思わせるため、少し止まって小さく動かす
        await Task.Delay(holdMilliseconds / 2);
        MoveTo(to.X - 1, to.Y);
        await Task.Delay(holdMilliseconds / 2);
        MoveTo(to.X, to.Y);
        await Task.Delay(100);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }
}
