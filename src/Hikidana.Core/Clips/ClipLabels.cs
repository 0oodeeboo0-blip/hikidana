namespace Hikidana.Core.Clips;

/// <summary>画面に出す、種類の名前など。</summary>
public static class ClipLabels
{
    /// <summary>取っ手や棚へドラッグしてしまったときの「コピーしたアプリ」（どのアプリから来たかはわからない）。</summary>
    public const string DropSource = "drop";

    public static string KindName(ClipKind kind) => kind switch
    {
        ClipKind.Text => "テキスト",
        ClipKind.RichText => "書式つきテキスト",
        ClipKind.Code => "コード",
        ClipKind.Link => "リンク",
        ClipKind.Image => "画像",
        ClipKind.Screenshot => "スクリーンショット",
        ClipKind.Files => "ファイル",
        ClipKind.Color => "色",
        ClipKind.Table => "表",
        _ => "その他",
    };

    /// <summary>コピーしたアプリの、見せる名前（実行ファイルの名前から .exe を除いたもの。よく使うアプリは呼び名にする）。</summary>
    public static string? AppName(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(executable);
        return name.ToLowerInvariant() switch
        {
            "chrome" => "Chrome",
            "msedge" => "Edge",
            "firefox" => "Firefox",
            "chatgpt" => "ChatGPT",
            "claude" => "Claude",
            "code" => "VS Code",
            "devenv" => "Visual Studio",
            "explorer" => "エクスプローラー",
            "notepad" => "メモ帳",
            "winword" => "Word",
            "excel" => "Excel",
            "powerpnt" => "PowerPoint",
            "onenote" => "OneNote",
            "outlook" or "olk" => "Outlook",
            "snippingtool" or "screenclippinghost" or "screensketch" => "Snipping Tool",
            "slack" => "Slack",
            "teams" or "ms-teams" => "Teams",
            "windowsterminal" => "ターミナル",
            DropSource => "ドラッグで追加",
            _ => name,
        };
    }

    /// <summary>ファイルの大きさの書き方（例: 1.6 MB）。</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} バイト",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };
}
