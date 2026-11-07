namespace Hikidana.Core.Clips;

/// <summary>棚にしまったものの種類。カードの見た目と、貼り付けるときの形を決める。</summary>
public enum ClipKind
{
    /// <summary>ふつうのテキスト。</summary>
    Text,

    /// <summary>書式つきのテキスト（HTML や RTF もある）。</summary>
    RichText,

    /// <summary>プログラムやコマンド。等幅で見せる。</summary>
    Code,

    /// <summary>URL だけのテキスト。</summary>
    Link,

    /// <summary>画像（スクリーンショット以外）。</summary>
    Image,

    /// <summary>スクリーンショット（Snipping Tool などが書いた画像）。</summary>
    Screenshot,

    /// <summary>ファイルとフォルダー（元の場所を指す）。</summary>
    Files,

    /// <summary>色（#RRGGBB や rgb()）。</summary>
    Color,

    /// <summary>表（Excel などの範囲）。</summary>
    Table,
}
