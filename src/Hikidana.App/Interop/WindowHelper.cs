using System.Windows;
using System.Windows.Interop;
using Hikidana.Core.Layout;

namespace Hikidana.App.Interop;

/// <summary>ウインドウの性質と置き場所を、Windows の API で直接決める。</summary>
internal static class WindowHelper
{
    // HwndSource は登録した関数を弱く持つことがあるので、ここで持ち続ける
    private static readonly HwndSourceHook NoActivateHook = OnNoActivateMessage;

    public static nint HandleOf(Window window) => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>
    /// タスクバーにも Alt+Tab にも出ない道具のウインドウにする。<paramref name="noActivate"/> なら、
    /// クリックしても前面にならない（作業中のアプリからフォーカスを奪わない）。
    /// </summary>
    public static void MakeToolWindow(Window window, bool noActivate)
    {
        var hwnd = HandleOf(window);
        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        style |= Native.WS_EX_TOOLWINDOW;
        if (noActivate)
        {
            style |= Native.WS_EX_NOACTIVATE;
        }

        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (nint)style);
        if (noActivate)
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(NoActivateHook);
        }
    }

    /// <summary>角を丸くする（Windows 11）。</summary>
    public static unsafe void RoundCorners(Window window, bool small = false)
    {
        var value = small ? Native.DWMWCP_ROUNDSMALL : Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(HandleOf(window), Native.DWMWA_WINDOW_CORNER_PREFERENCE, &value, sizeof(uint));
    }

    /// <summary>
    /// 前面にして、キーボードを受け取れるようにする（クリックで前面にならないウインドウでも）。
    /// 利用者がこのウインドウをクリックした直後に呼ぶ（Windows は、最後に入力を受けたプロセスにだけ前面の切り替えを許す）。
    /// </summary>
    public static void Activate(Window window)
    {
        Native.SetForegroundWindow(HandleOf(window));
        window.Activate();
    }

    /// <summary>ほかのアプリのウインドウを前面に戻す。</summary>
    public static bool BringToFront(nint hwnd) => hwnd != 0 && Native.SetForegroundWindow(hwnd);

    /// <summary>本当の画素で置く（いちばん手前に、前面にはしないで）。</summary>
    public static void Place(Window window, PixelRect rect) =>
        Native.SetWindowPos(HandleOf(window), Native.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height, Native.SWP_NOACTIVATE);

    /// <summary>大きさは変えずに、本当の画素の位置へ動かす。</summary>
    public static void Move(Window window, int x, int y) =>
        Native.SetWindowPos(HandleOf(window), Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOSIZE);

    /// <summary>マウスの位置（本当の画素）。</summary>
    public static (int X, int Y) CursorPosition()
    {
        Native.GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    private static nint OnNoActivateMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return Native.MA_NOACTIVATE;
        }

        return 0;
    }
}
