using Hikidana.Core.Clipboard;
using Hikidana.Core.Imaging;
using Hikidana.Core.Text;

namespace Hikidana.Core.Clips;

/// <summary>クリップボードから取り込み、棚にしまう前のもの。</summary>
public sealed class ClipDraft
{
    public required ClipKind Kind { get; init; }

    /// <summary>テキスト（テキストの種類のとき。画像やファイルでも、説明に使えるテキストがあれば入る）。</summary>
    public string? Text { get; init; }

    /// <summary>画像（スクリーンショット・画像のとき）。</summary>
    public PixelImage? Image { get; init; }

    /// <summary>元のアプリが置いた PNG（そのまま残す）。無ければ null（しまうときに作る）。</summary>
    public byte[]? OriginalPng { get; init; }

    /// <summary>ファイルとフォルダーのパス（ファイルのとき）。</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>色（色のとき）。</summary>
    public ColorValue? Color { get; init; }

    /// <summary>表の中身（表のとき。カードで見せる分だけ）。</summary>
    public IReadOnlyList<IReadOnlyList<string>>? TableRows { get; init; }

    /// <summary>貼り付けるときにそのまま戻す形（テキスト・表・書式つきのとき。HTML、RTF、Excel の形など）。</summary>
    public IReadOnlyList<ClipboardFormatData> Preserved { get; init; } = [];

    /// <summary>コピーしたアプリの実行ファイルの名前。</summary>
    public string? SourceApp { get; init; }

    /// <summary>コピー元のページの URL（ブラウザーから書式つきでコピーしたとき）。</summary>
    public string? SourceUrl { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
}
