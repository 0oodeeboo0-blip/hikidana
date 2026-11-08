using Hikidana.Core.Layout;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>前面のウインドウが全画面か（ゲームや、全画面で見ている動画など）を調べる。</summary>
public static class Fullscreen
{
    /// <summary>
    /// いま前面にあるウインドウ（このプロセスのものは除く）が、<paramref name="monitor"/>（画面全体、本当の画素）を覆っていれば true。
    /// </summary>
    public static bool IsForegroundFullscreen(PixelRect monitor)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != 0)
        {
            hwnd = root;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return FullscreenRule.Covers(
            PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom),
            monitor,
            NativeMethods.IsZoomed(hwnd),
            NativeMethods.IsIconic(hwnd),
            NativeMethods.IsWindowVisible(hwnd),
            TargetWindow.ClassNameOf(hwnd));
    }
}
