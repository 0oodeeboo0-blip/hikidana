using Hikidana.App.Interop;
using Hikidana.Core.Layout;
using Hikidana.Core.Settings;

namespace Hikidana.App.Settings;

/// <summary>
/// 設定の画面から、棚へ変更を伝える口（ShelfController が受ける）。どれも画面のスレッドで呼ぶ。
/// 変えたらすぐ棚に反映して、設定のファイルに保存する。
/// </summary>
internal interface ISettingsHost
{
    /// <summary>いまの設定（読むだけ。変えるときは下の Set で始まるものを呼ぶ）。</summary>
    AppSettings Settings { get; }

    /// <summary>いまの取っ手と棚の置き場所。</summary>
    ShelfLayout Layout { get; }

    /// <summary>棚を出している画面（設定の画面が外されていれば、メインの画面）。</summary>
    MonitorInfo Monitor { get; }

    /// <summary>つながっている画面すべて。</summary>
    IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>Windows にサインインしたら起動する。</summary>
    bool StartWithWindows { get; }

    /// <summary>
    /// 設定の画面の外で、設定や置き場所が変わった（取っ手をドラッグした、棚から記録を止めた、画面をつなぎ替えたなど）。
    /// 設定の画面は、これを受けて見え方を合わせ直す。
    /// </summary>
    event Action? SettingsChanged;

    void SetEdge(ShelfEdge edge);

    /// <summary>棚を出す画面。null ならメインの画面。</summary>
    void SetMonitor(string? deviceName);

    /// <summary>取っ手を端の真ん中に戻す。</summary>
    void CenterHandle();

    void SetOpenOnHover(bool on);

    void SetCloseAfterPaste(bool on);

    /// <summary>system・light・dark。</summary>
    void SetTheme(string theme);

    /// <summary>しまっておく数を <paramref name="maxItems"/> にしたら消えるものの数。</summary>
    int CountOverLimit(int maxItems);

    void SetMaxItems(int maxItems);

    void SetMoveToTop(bool on);

    void SetPaused(bool paused);

    /// <summary>サインインしたら起動するかを変える。変えられなければ false（知らせも出す）。</summary>
    bool SetStartWithWindows(bool on);

    void OpenDataFolder();

    void OpenLogFolder();

    /// <summary>設定の画面が前に出た（開いている棚は閉じる）。</summary>
    void SettingsActivated();
}
