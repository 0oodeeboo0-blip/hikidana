using System.Text.Json.Serialization;

namespace Hikidana.Core.Clips;

/// <summary>棚にしまってある 1 つのもの（索引に書く分）。中身（画像・長いテキスト・形）は別のファイルに置く。</summary>
public sealed class Clip
{
    /// <summary>しまった日時から作る ID（例: 20261107-181205-3fa2）。フォルダーの名前にもなる。</summary>
    public required string Id { get; init; }

    public required ClipKind Kind { get; set; }

    /// <summary>初めてしまった日時。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>並び順の日時。同じものをもう一度コピーしたとき（設定によっては貼り付けたときも）に新しくなる。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Pinned { get; set; }

    /// <summary>コピーしたアプリの実行ファイルの名前。</summary>
    public string? SourceApp { get; set; }

    /// <summary>コピー元のページの URL。</summary>
    public string? SourceUrl { get; set; }

    /// <summary>カードと検索のためのテキスト（長ければ先頭だけ）。</summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>テキストの文字数（テキストの種類のとき）。</summary>
    public int TextLength { get; set; }

    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    /// <summary>しまってある PNG の大きさ（バイト）。</summary>
    public long ImageBytes { get; set; }

    /// <summary>縮小画像があれば true（無ければ元の画像を使う）。</summary>
    public bool HasThumbnail { get; set; }

    public List<string> Files { get; set; } = [];

    /// <summary>色のとき、#RRGGBB。</summary>
    public string? ColorHex { get; set; }

    /// <summary>表のとき、見せる分の中身と、全体の行と列の数。</summary>
    public List<List<string>>? TableRows { get; set; }

    public int TableRowCount { get; set; }

    public int TableColumnCount { get; set; }

    /// <summary>そのまま戻す形の数（HTML・RTF・Excel の形など）。</summary>
    public int PreservedCount { get; set; }

    /// <summary>同じものかどうかを見分ける指紋。</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>画像の種類なら true。</summary>
    [JsonIgnore]
    public bool IsImage => Kind is ClipKind.Image or ClipKind.Screenshot;

    /// <summary>テキストを持つ種類なら true。</summary>
    [JsonIgnore]
    public bool HasText => Kind is ClipKind.Text or ClipKind.RichText or ClipKind.Code or ClipKind.Link or ClipKind.Color or ClipKind.Table;
}
