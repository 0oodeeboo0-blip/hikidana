using System.Globalization;
using System.Text.RegularExpressions;

namespace Hikidana.Core.Text;

/// <summary>色。カードの色見本と、貼り付けるときの文字にする。</summary>
public readonly record struct ColorValue(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>#RRGGBB（透明度があれば #RRGGBBAA）。</summary>
    public string Hex => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>rgb(r, g, b)（透明度があれば rgba）。</summary>
    public string Rgb => A == 255
        ? $"rgb({R}, {G}, {B})"
        : string.Create(CultureInfo.InvariantCulture, $"rgba({R}, {G}, {B}, {Math.Round(A / 255.0, 2)})");
}

/// <summary>
/// コピーされたテキストが何か（URL・色・コード）を見分ける。
/// 迷ったら「ふつうのテキスト」にする（見分けを誤るより、見分けない方がましなので）。
/// </summary>
public static partial class TextSniffer
{
    /// <summary>前後の空白を除いて、http または https の URL が 1 つだけのとき true。</summary>
    public static bool IsSingleUrl(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length > 4096 || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host) || !parsed.Host.Contains('.') && parsed.Host != "localhost")
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>#RGB、#RRGGBB、#RRGGBBAA、rgb()、rgba() のどれか 1 つだけのとき true。</summary>
    public static bool TryParseColor(string? text, out ColorValue color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.Length > 64)
        {
            return false;
        }

        var hex = HexColor().Match(s);
        if (hex.Success)
        {
            var digits = hex.Groups[1].Value;
            if (digits.Length == 3)
            {
                digits = string.Concat(digits.Select(c => new string(c, 2)));
            }

            var r = byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var a = digits.Length == 8 ? byte.Parse(digits.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)255;
            color = new ColorValue(r, g, b, a);
            return true;
        }

        var rgb = RgbColor().Match(s);
        if (rgb.Success)
        {
            if (!TryChannel(rgb.Groups["r"].Value, out var r) || !TryChannel(rgb.Groups["g"].Value, out var g) || !TryChannel(rgb.Groups["b"].Value, out var b))
            {
                return false;
            }

            byte a = 255;
            if (rgb.Groups["a"].Success)
            {
                if (!double.TryParse(rgb.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha) || alpha < 0 || alpha > 1)
                {
                    return false;
                }

                a = (byte)Math.Round(alpha * 255);
            }

            color = new ColorValue(r, g, b, a);
            return true;
        }

        return false;
    }

    /// <summary>
    /// プログラムやコマンドらしいとき true。手がかり（行末の ; や {}、字下げ、よくある書き方）が 2 つ以上そろったときだけ。
    /// コピー元がエディターなら、手がかり 1 つで十分とみなす。
    /// </summary>
    public static bool LooksLikeCode(string? text, bool fromCodeEditor = false)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000)
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0)
        {
            return false;
        }

        // 日本語の文章が多ければコードとはみなさない（コメントだけ日本語のコードは、ほかの手がかりで拾う）
        var letters = text.Count(c => !char.IsWhiteSpace(c));
        var japanese = text.Count(IsJapanese);
        if (letters > 0 && japanese * 3 > letters)
        {
            return false;
        }

        var signals = 0;
        if (lines.Count(l => l.TrimEnd().EndsWith(';') || l.TrimEnd().EndsWith('{') || l.Trim() == "}") * 3 >= lines.Length && lines.Length >= 2)
        {
            signals++;
        }

        if (lines.Length >= 2 && lines.Skip(1).Count(l => l.StartsWith("  ", StringComparison.Ordinal) || l.StartsWith('\t')) * 2 >= lines.Length - 1)
        {
            signals++;
        }

        if (CodeToken().IsMatch(text))
        {
            signals++;
        }

        if (ShellToken().IsMatch(text))
        {
            signals++;
        }

        if (JsonLike(text))
        {
            signals += 2;
        }

        return signals >= 2 || (fromCodeEditor && signals >= 1);
    }

    private static bool JsonLike(string text)
    {
        var t = text.Trim();
        return t.Length >= 2 && (t[0] == '{' && t[^1] == '}' || t[0] == '[' && t[^1] == ']') && t.Contains('"') && t.Contains(':');
    }

    private static bool IsJapanese(char c) =>
        c is >= '぀' and <= 'ヿ' // ひらがな・カタカナ
        or >= '一' and <= '鿿' // 漢字
        or >= '＀' and <= '￯'; // 全角の記号

    private static bool TryChannel(string value, out byte channel)
    {
        channel = 0;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > 255)
        {
            return false;
        }

        channel = (byte)n;
        return true;
    }

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^rgba?\(\s*(?<r>\d{1,3})\s*[,\s]\s*(?<g>\d{1,3})\s*[,\s]\s*(?<b>\d{1,3})\s*(?:[,/]\s*(?<a>[0-9]*\.?[0-9]+)\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbColor();

    [GeneratedRegex(@"(=>|::|\+\+|&&|\|\||!=|==|<\/\w+>|\b(function|return|class|import|using|namespace|public|private|static|const|let|var|def|lambda|SELECT|FROM|WHERE|#include)\b)")]
    private static partial Regex CodeToken();

    [GeneratedRegex(@"(^|\s)(Get|Set|New|Remove|Invoke|Start|Stop|Measure|Select|Where)-[A-Z][A-Za-z]+|\$_|\$env:|(^|\s)(git|npm|dotnet|pip|cd|ls|dir|sudo|winget)\s+-{0,2}\w|\s\|\s", RegexOptions.Multiline)]
    private static partial Regex ShellToken();
}
