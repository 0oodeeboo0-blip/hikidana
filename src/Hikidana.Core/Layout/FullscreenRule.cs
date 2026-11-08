namespace Hikidana.Core.Layout;

/// <summary>
/// 前面のウインドウが「全画面」か（ゲームや、全画面で見ている動画、スライドショーなど）。
/// 全画面のあいだは、取っ手を隠して、マウスを乗せても開かないようにする。
/// </summary>
public static class FullscreenRule
{
    /// <summary>デスクトップやタスクバー（画面いっぱいでも、全画面のアプリではない）。</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    /// <summary>
    /// ウインドウが画面全体（タスクバーのところも含む）を覆っていれば true。
    /// 最大化したウインドウは全画面とみなさない（タスクバーを自動で隠す設定だと、最大化でも画面いっぱいになるため。
    /// ブラウザーなどは、全画面にするときに最大化をやめてから広げる）。
    /// </summary>
    /// <param name="window">ウインドウの四角（本当の画素）。</param>
    /// <param name="monitor">画面全体の四角（本当の画素）。</param>
    /// <param name="maximized">最大化している。</param>
    /// <param name="minimized">最小化している。</param>
    /// <param name="visible">見えている。</param>
    /// <param name="className">ウインドウのクラスの名前。</param>
    public static bool Covers(PixelRect window, PixelRect monitor, bool maximized, bool minimized, bool visible, string className) =>
        visible
        && !minimized
        && !maximized
        && !monitor.IsEmpty
        && !ShellClasses.Contains(className)
        && window.X <= monitor.X
        && window.Y <= monitor.Y
        && window.Right >= monitor.Right
        && window.Bottom >= monitor.Bottom;
}
