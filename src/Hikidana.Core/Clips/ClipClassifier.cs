using System.Runtime.InteropServices;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Imaging;
using Hikidana.Core.Text;

namespace Hikidana.Core.Clips;

/// <summary>クリップボードの中身が何か（スクリーンショット・画像・ファイル・表・テキスト…）を決める。</summary>
public static class ClipClassifier
{
    /// <summary>テキストの形で残す中身の上限（1 つの形あたり）。</summary>
    public const int MaxPreservedFormatBytes = 16 * 1024 * 1024;

    /// <summary>テキストの形で残す中身の上限（合計）。</summary>
    public const int MaxPreservedTotalBytes = 48 * 1024 * 1024;

    /// <summary>スクリーンショットを撮るアプリ（クリップボードに書いたのがこれなら、スクリーンショットとみなす）。</summary>
    public static readonly IReadOnlySet<string> ScreenshotApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SnippingTool.exe", "ScreenClippingHost.exe", "ScreenSketch.exe", "ShareX.exe", "Greenshot.exe", "Lightshot.exe",
        "Snagit32.exe", "SnagitEditor.exe", "SnagitCapture.exe", "PicPick.exe", "flameshot.exe", "Snipaste.exe",
        "Gyazo.exe", "GyazoWin.exe", "FSCapture.exe", "Screenpresso.exe", "GameBar.exe",
    };

    /// <summary>コードを書くアプリとターミナル（ここからのコピーはコードとみなしやすくする）。</summary>
    public static readonly IReadOnlySet<string> CodeApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Code.exe", "Code - Insiders.exe", "Cursor.exe", "Windsurf.exe", "Zed.exe", "devenv.exe", "notepad++.exe", "sublime_text.exe",
        "idea64.exe", "rider64.exe", "pycharm64.exe", "webstorm64.exe", "clion64.exe", "goland64.exe", "phpstorm64.exe", "datagrip64.exe",
        "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe", "powershell.exe", "pwsh.exe", "cmd.exe", "powershell_ise.exe",
    };

    private static readonly string[] SpreadsheetFormats = [FormatName.Biff12, FormatName.Biff8, FormatName.Biff5, FormatName.XmlSpreadsheet, FormatName.Csv];

    /// <summary>しまうものに変える。何も使えるものが無ければ null。</summary>
    public static ClipDraft? Classify(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var owner = snapshot.OwnerProcess;
        var text = ReadText(snapshot);
        var hasText = !string.IsNullOrWhiteSpace(text);
        string? fragment = null;
        string? sourceUrl = null;
        if (snapshot.Find(FormatName.Html) is { } html && HtmlClipboard.TryGetFragment(html.Data, out var f, out var url))
        {
            fragment = f;
            sourceUrl = url;
        }

        // ファイル（エクスプローラーでのコピー）。画像ビューアーのように画像も一緒に置くアプリでも、ファイルを優先する
        if (snapshot.Find(StandardFormat.HDrop) is { } drop)
        {
            try
            {
                var files = DropFiles.Parse(drop.Data);
                if (files.Count > 0)
                {
                    return new ClipDraft
                    {
                        Kind = ClipKind.Files,
                        Files = files,
                        SourceApp = owner,
                        CapturedAt = snapshot.CapturedAt,
                    };
                }
            }
            catch (InvalidDataException)
            {
                // 壊れた CF_HDROP は無視して、ほかの形を見る
            }
        }

        var spreadsheet = SpreadsheetFormats.Any(snapshot.Has) || snapshot.Has(StandardFormat.Sylk);
        var fromScreenshotApp = owner is not null && ScreenshotApps.Contains(owner);

        // 画像。テキストが無いか、スクリーンショットのアプリか、テキストが画像の URL だけ（ブラウザーの「画像をコピー」）のとき
        var imageFirst = !spreadsheet && (!hasText
            || fromScreenshotApp
            || TextSniffer.IsSingleUrl(text, out _)
            || fragment is not null && HtmlClipboard.IsSingleImage(fragment));
        if (imageFirst && ReadImage(snapshot) is var (image, png) && image is not null)
        {
            // PrintScreen キーで撮ったものは、書いたアプリが分からない（かエクスプローラー）ことが多い
            var printScreen = !hasText && fragment is null && (owner is null || string.Equals(owner, "explorer.exe", StringComparison.OrdinalIgnoreCase));
            return new ClipDraft
            {
                Kind = fromScreenshotApp || printScreen ? ClipKind.Screenshot : ClipKind.Image,
                Image = image,
                OriginalPng = png,
                Text = hasText ? text : null,
                SourceApp = owner,
                SourceUrl = sourceUrl,
                CapturedAt = snapshot.CapturedAt,
            };
        }

        if (!hasText)
        {
            // テキストが無い表など。画像があれば画像として残す
            if (ReadImage(snapshot) is var (fallbackImage, fallbackPng) && fallbackImage is not null)
            {
                return new ClipDraft
                {
                    Kind = ClipKind.Image,
                    Image = fallbackImage,
                    OriginalPng = fallbackPng,
                    SourceApp = owner,
                    CapturedAt = snapshot.CapturedAt,
                };
            }

            return null;
        }

        var preserved = PreservedFormats(snapshot);
        var htmlTable = fragment?.Contains("<table", StringComparison.OrdinalIgnoreCase) == true;
        if ((spreadsheet || htmlTable && text!.Contains('\t'))
            && TableText.TryParse(text, out var rows, out _, out _, maxRows: 20, maxColumns: 8))
        {
            return new ClipDraft
            {
                Kind = ClipKind.Table,
                Text = text,
                TableRows = rows,
                Preserved = preserved,
                SourceApp = owner,
                SourceUrl = sourceUrl,
                CapturedAt = snapshot.CapturedAt,
            };
        }

        var kind = ClipKind.Text;
        ColorValue? color = null;
        if (TextSniffer.TryParseColor(text, out var c))
        {
            kind = ClipKind.Color;
            color = c;
        }
        else if (TextSniffer.IsSingleUrl(text, out _))
        {
            kind = ClipKind.Link;
        }
        else if (TextSniffer.LooksLikeCode(text, fromCodeEditor: owner is not null && CodeApps.Contains(owner)))
        {
            kind = ClipKind.Code;
        }
        else if (snapshot.Has(FormatName.Html) || snapshot.Has(FormatName.Rtf))
        {
            kind = ClipKind.RichText;
        }

        return new ClipDraft
        {
            Kind = kind,
            Text = text,
            Color = color,
            Preserved = preserved,
            SourceApp = owner,
            SourceUrl = sourceUrl,
            CapturedAt = snapshot.CapturedAt,
        };
    }

    /// <summary>CF_UNICODETEXT のテキスト（最初の NUL まで）。無ければ null。</summary>
    public static string? ReadText(ClipboardSnapshot snapshot)
    {
        if (snapshot.Find(StandardFormat.UnicodeText) is not { } unicode)
        {
            return null;
        }

        var chars = MemoryMarshal.Cast<byte, char>(unicode.Data.AsSpan(0, unicode.Data.Length & ~1));
        var end = chars.IndexOf('\0');
        return new string(end >= 0 ? chars[..end] : chars);
    }

    /// <summary>画像を読む。PNG を優先し、無ければ CF_DIBV5、CF_DIB の順。読めなければ (null, null)。</summary>
    public static (PixelImage? Image, byte[]? Png) ReadImage(ClipboardSnapshot snapshot)
    {
        foreach (var name in new[] { FormatName.Png, FormatName.MimePng })
        {
            if (snapshot.Find(name) is { } png && PngCodec.IsPng(png.Data))
            {
                try
                {
                    return (PngCodec.Decode(png.Data), png.Data);
                }
                catch (InvalidDataException)
                {
                    // 壊れていれば次の形
                }
            }
        }

        foreach (var id in new[] { StandardFormat.DibV5, StandardFormat.Dib })
        {
            if (snapshot.Find(id) is { } dib)
            {
                try
                {
                    return (DibCodec.Decode(dib.Data), null);
                }
                catch (InvalidDataException)
                {
                }
            }
        }

        return (null, null);
    }

    /// <summary>テキストの種類で、貼り付けるときにそのまま戻す形を選ぶ（HTML・RTF・Excel の形など）。</summary>
    public static IReadOnlyList<ClipboardFormatData> PreservedFormats(ClipboardSnapshot snapshot)
    {
        var result = new List<ClipboardFormatData>();
        long total = 0;
        foreach (var format in snapshot.Formats)
        {
            if (format.Id == StandardFormat.UnicodeText
                || StandardFormat.IsSynthesized(format.Id)
                || StandardFormat.IsHandleFormat(format.Id)
                || format.Id is StandardFormat.Dib or StandardFormat.DibV5 or StandardFormat.HDrop
                || string.Equals(format.Name, FormatName.Png, StringComparison.OrdinalIgnoreCase)
                || string.Equals(format.Name, FormatName.MimePng, StringComparison.OrdinalIgnoreCase)
                || FormatName.Ephemeral.Contains(format.Name)
                || format.Data.Length == 0
                || format.Data.Length > MaxPreservedFormatBytes
                || total + format.Data.Length > MaxPreservedTotalBytes)
            {
                continue;
            }

            total += format.Data.Length;
            result.Add(format);
        }

        return result;
    }
}
