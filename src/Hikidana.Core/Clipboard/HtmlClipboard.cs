using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hikidana.Core.Clipboard;

/// <summary>
/// クリップボードの「HTML Format」。先頭に Version・StartHTML・EndHTML・StartFragment・EndFragment などが並び、
/// 位置は UTF-8 のバイトの数で書かれる。
/// </summary>
public static partial class HtmlClipboard
{
    /// <summary>コピーされた部分（StartFragment から EndFragment まで）と、元のページの URL を取り出す。</summary>
    public static bool TryGetFragment(ReadOnlySpan<byte> data, out string fragment, out string? sourceUrl)
    {
        fragment = string.Empty;
        sourceUrl = null;
        var end = data.IndexOf((byte)0);
        if (end >= 0)
        {
            data = data[..end];
        }

        var headerLength = Math.Min(data.Length, 1024);
        var header = Encoding.ASCII.GetString(data[..headerLength]);
        var start = ReadOffset(header, "StartFragment");
        var stop = ReadOffset(header, "EndFragment");
        var source = Field().Match(header);
        while (source.Success)
        {
            if (string.Equals(source.Groups["key"].Value, "SourceURL", StringComparison.OrdinalIgnoreCase))
            {
                sourceUrl = source.Groups["value"].Value.Trim();
            }

            source = source.NextMatch();
        }

        if (start < 0 || stop < start || stop > data.Length)
        {
            // 位置が無いか壊れていれば、印のコメントで探す
            var all = Encoding.UTF8.GetString(data);
            var a = all.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            var b = all.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (a < 0 || b < a)
            {
                return false;
            }

            fragment = all[(a + "<!--StartFragment-->".Length)..b];
            return true;
        }

        fragment = Encoding.UTF8.GetString(data[start..stop]);
        return true;
    }

    /// <summary>コピーされた部分が画像 1 つだけ（&lt;img&gt;。リンクや段落で囲まれていてもよい）なら true。</summary>
    public static bool IsSingleImage(string fragment)
    {
        var withoutComments = Comment().Replace(fragment, string.Empty);
        var images = ImageTag().Matches(withoutComments);
        if (images.Count != 1)
        {
            return false;
        }

        var rest = ImageTag().Replace(withoutComments, string.Empty);
        rest = WrapperTag().Replace(rest, string.Empty);
        rest = System.Net.WebUtility.HtmlDecode(rest);
        return string.IsNullOrWhiteSpace(rest);
    }

    /// <summary>HTML の断片から「HTML Format」の中身を作る。</summary>
    public static byte[] Build(string fragment, string? sourceUrl = null)
    {
        const string prefix = "<html><body>\r\n<!--StartFragment-->";
        const string suffix = "<!--EndFragment-->\r\n</body></html>";
        string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
            "Version:0.9\r\n" +
            $"StartHTML:{startHtml:D10}\r\n" +
            $"EndHTML:{endHtml:D10}\r\n" +
            $"StartFragment:{startFragment:D10}\r\n" +
            $"EndFragment:{endFragment:D10}\r\n" +
            (sourceUrl is null ? string.Empty : $"SourceURL:{sourceUrl}\r\n");

        var headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        var text = Header(headerLength, endHtml, startFragment, endFragment) + prefix + fragment + suffix;
        return Encoding.UTF8.GetBytes(text);
    }

    private static int ReadOffset(string header, string key)
    {
        var match = Regex.Match(header, key + @":\s*(-?\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z]+):(?<value>[^\r\n]*)\r?$", RegexOptions.Multiline)]
    private static partial Regex Field();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"</?(a|p|div|span|picture|source|figure|br|meta|html|body|head|b|i|u|strong|em)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex WrapperTag();
}
