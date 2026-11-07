using System.Diagnostics;
using static Hikidana.Win32.Tests.Support.TestNative;

namespace Hikidana.Win32.Tests.Support;

/// <summary>ほかのアプリのウインドウを探したり、文字を読んだりする。</summary>
internal static class Desktop
{
    public static unsafe string TitleOf(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* pointer = buffer)
        {
            var count = GetWindowText(hwnd, pointer, buffer.Length);
            return new string(buffer, 0, Math.Max(0, count));
        }
    }

    public static unsafe string ClassOf(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>入力欄などの中の文字（WM_GETTEXT）。</summary>
    public static unsafe string ControlText(nint hwnd)
    {
        var length = (int)SendMessage(hwnd, WM_GETTEXTLENGTH, 0, 0);
        var buffer = new char[length + 1];
        fixed (char* pointer = buffer)
        {
            var count = (int)SendMessage(hwnd, WM_GETTEXT, buffer.Length, (nint)pointer);
            return new string(buffer, 0, Math.Clamp(count, 0, length));
        }
    }

    public static IEnumerable<nint> TopLevelWindows()
    {
        nint window = 0;
        while ((window = FindWindowEx(0, window, null, null)) != 0)
        {
            yield return window;
        }
    }

    /// <summary>子ウインドウを深さ優先で探す。</summary>
    public static nint FindDescendant(nint parent, Func<nint, bool> match)
    {
        nint child = 0;
        while ((child = FindWindowEx(parent, child, null, null)) != 0)
        {
            if (match(child))
            {
                return child;
            }

            var found = FindDescendant(child, match);
            if (found != 0)
            {
                return found;
            }
        }

        return 0;
    }

    /// <summary>条件に合う、見えているいちばん上のウインドウが出るまで待つ。出なければ 0。</summary>
    public static async Task<nint> WaitForWindowAsync(Func<nint, bool> match, TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            foreach (var window in TopLevelWindows())
            {
                if (IsWindowVisible(window) && match(window))
                {
                    return window;
                }
            }

            await Task.Delay(100);
        }

        return 0;
    }

    /// <summary>条件が満たされるまで待つ。満たされれば true。</summary>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    public static uint ProcessIdOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static string Describe(nint hwnd) => hwnd == 0 ? "（なし）" : $"「{TitleOf(hwnd)}」（{ClassOf(hwnd)}、プロセス {ProcessIdOf(hwnd)}）";

    public static string ForegroundDescription() => Describe(GetForegroundWindow());
}
