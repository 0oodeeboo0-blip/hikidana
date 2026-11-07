using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hikidana.Core.Updates;

/// <summary>GitHub の Releases に置いた版（いちばん新しいもの）。</summary>
public sealed record ReleaseInfo(string Tag, Version Version, string PageUrl, string ZipUrl, long ZipSize, string? ChecksumUrl, bool Prerelease)
{
    /// <summary>新しい版を確かめる所（GitHub の API。ログインは要らない）。</summary>
    public const string LatestUrl = "https://api.github.com/repos/0oodeeboo0-blip/hikidana/releases/latest";

    /// <summary>配る zip の名前の終わり（例: Hikidana-0.2.0-win-x64.zip）。</summary>
    public const string ZipSuffix = "-win-x64.zip";

    /// <summary>zip の SHA-256 を書いたファイルの名前の終わり（例: Hikidana-0.2.0-win-x64.zip.sha256）。</summary>
    public const string ChecksumSuffix = ".zip.sha256";

    /// <summary>
    /// GitHub の API の答え（releases/latest の JSON）を読む。版の形が違う・配る zip が無いなどで使えなければ null。
    /// </summary>
    public static ReleaseInfo? Parse(ReadOnlySpan<byte> json)
    {
        GitHubRelease? release;
        try
        {
            release = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release is null || release.Draft || ParseVersion(release.TagName) is not { } version)
        {
            return null;
        }

        var assets = release.Assets ?? [];
        var zip = assets.FirstOrDefault(a => a.Name?.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrEmpty(a.DownloadUrl));
        if (zip is null)
        {
            return null;
        }

        var checksum = assets.FirstOrDefault(a => a.Name?.EndsWith(ChecksumSuffix, StringComparison.OrdinalIgnoreCase) == true);
        return new ReleaseInfo(release.TagName!, version, release.PageUrl ?? string.Empty, zip.DownloadUrl!, zip.Size, checksum?.DownloadUrl, release.Prerelease);
    }

    /// <summary>
    /// 版の文字（v0.2.0、0.2.0、0.2.0+abc1234、0.2.0-beta.1 など）から、数の部分（0.2.0）を取り出す。読めなければ null。
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var core = text.Trim().TrimStart('v', 'V');
        var end = core.IndexOfAny(['-', '+', ' ']);
        if (end >= 0)
        {
            core = core[..end];
        }

        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>いま動いている版（<paramref name="current"/>。例: 0.1.0+abc1234）より新しければ true。</summary>
    public bool IsNewerThan(string current) => ParseVersion(current) is not { } now || Version > now;

    /// <summary>「0.2.0」のような、人に見せる版の名前。</summary>
    public string DisplayVersion => Version.ToString(3);
}

/// <summary>GitHub の API の、版（release）の答えのうち使うところ。</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? PageUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

/// <summary>版に付けたファイル。</summary>
public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
