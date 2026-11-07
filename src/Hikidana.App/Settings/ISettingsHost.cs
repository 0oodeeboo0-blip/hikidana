using Hikidana.App.Interop;
using Hikidana.Core.Layout;
using Hikidana.Core.Settings;
using Hikidana.Core.Storage;

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

    /// <summary>まとめて消すと消えるものの数（ピン留めは数えない）。</summary>
    int CountClearable(ClearScope scope);

    /// <summary>いまのショートカットが使えている（ほかのアプリと重なっていない）。</summary>
    bool HotkeyWorks { get; }

    /// <summary>ショートカットを変える（設定の書き方。空なら使わない）。ほかのアプリが使っていて使えなければ false。</summary>
    bool SetHotkey(string setting);

    /// <summary>自動で消す設定を <paramref name="days"/> 日・<paramref name="scope"/>（screenshots・all）にしたら、すぐ消えるものの数。</summary>
    int CountAutoDelete(int days, string scope);

    /// <summary>自動で消す設定を変える（0 日なら消さない）。もう期間を過ぎたものは、すぐ消える。</summary>
    void SetAutoDelete(int days, string scope);

    /// <summary>棚に入ったら取っ手で知らせるかを変える。</summary>
    void SetSignalOnCapture(bool on);

    /// <summary>見つかっている新しい版（無ければ null）。</summary>
    Hikidana.Core.Updates.ReleaseInfo? AvailableUpdate { get; }

    /// <summary>新しい版について、いまの様子の説明（「いまの版が最新です」など）。</summary>
    string UpdateStatus { get; }

    /// <summary>新しい版を、いま確かめる。</summary>
    Task CheckForUpdateAsync();

    /// <summary>新しい版に入れ替えて、起動し直す。</summary>
    void InstallUpdate();

    /// <summary>新しい版を自動で確かめるかを変える。</summary>
    void SetCheckForUpdates(bool on);

    /// <summary>まとめて消す（ピン留めは残す）。知らせの「元に戻す」で戻せる。</summary>
    void Clear(ClearScope scope);

    /// <summary>サインインしたら起動するかを変える。変えられなければ false（知らせも出す）。</summary>
    bool SetStartWithWindows(bool on);

    void OpenDataFolder();

    void OpenLogFolder();

    /// <summary>設定の画面が前に出た（開いている棚は閉じる）。</summary>
    void SettingsActivated();
}
