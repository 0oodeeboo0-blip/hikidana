using System.IO;
using System.Net;
using System.Net.Http;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Updates;

namespace Hikidana.App.Updates;

/// <summary>
/// 新しい版を GitHub の Releases で確かめて、ダウンロードする（入れ替えは <see cref="SelfUpdate"/>）。
/// 見に行くのは GitHub の API だけで、送るのはアプリの名前と版（User-Agent）だけ。
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private readonly HttpClient http;
    private readonly string downloadRoot;

    public UpdateService(string dataRoot)
    {
        downloadRoot = Path.Combine(dataRoot, "update");
        http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Hikidana/{ReleaseInfo.ParseVersion(App.Version)?.ToString(3) ?? "0"}");
    }

    /// <summary>見つかった新しい版（無ければ null）。</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>最後に確かめた日時（まだなら null）。</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>最後に確かめたとき、うまくいかなかった（ネットにつながっていないなど）。</summary>
    public bool LastCheckFailed { get; private set; }

    /// <summary>新しい版があるか確かめる。あれば、その版を返す（無ければ null）。ネットにつながらないなどのときも null。</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseInfo.LatestUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, timeout.Token);
            LastChecked = DateTimeOffset.Now;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // まだ版を 1 つも出していない
                LastCheckFailed = false;
                Available = null;
                return null;
            }

            response.EnsureSuccessStatusCode();
            var release = ReleaseInfo.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            LastCheckFailed = false;
            Available = release is not null && release.IsNewerThan(App.Version) ? release : null;
            if (Available is { } found)
            {
                Log.Info($"新しい版があります: {found.Tag}（いま {App.Version}）");
            }

            return Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            LastChecked = DateTimeOffset.Now;
            LastCheckFailed = true;
            Log.Warn($"新しい版を確かめられませんでした: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 新しい版の zip をダウンロードして（チェックサムがあれば確かめて）、Hikidana.exe を取り出し、その場所を返す。
    /// うまくいかなければ例外（HttpRequestException、IOException、InvalidDataException など）。
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(downloadRoot, release.DisplayVersion);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, $"Hikidana-{release.DisplayVersion}{ReleaseInfo.ZipSuffix}");
        using (var response = await http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await response.Content.CopyToAsync(file, cancellationToken);
        }

        if (release.ChecksumUrl is { } checksumUrl)
        {
            var expected = SelfUpdate.ParseChecksum(await http.GetStringAsync(checksumUrl, cancellationToken));
            var actual = SelfUpdate.Sha256(zip);
            if (expected is null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ダウンロードした zip が、置いてあるものと違います（チェックサムが合いません）。");
            }
        }

        var exe = SelfUpdate.ExtractExe(zip, Path.Combine(folder, "app"));
        Log.Info($"新しい版をダウンロードしました: {release.Tag}（{new FileInfo(zip).Length} バイト）");
        return exe;
    }

    /// <summary>前にダウンロードしたもの（入れ替え終わったもの）を消す。</summary>
    public void CleanDownloads()
    {
        try
        {
            if (Directory.Exists(downloadRoot))
            {
                Directory.Delete(downloadRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("前にダウンロードした新しい版を、消せませんでした。", ex);
        }
    }

    public void Dispose() => http.Dispose();
}
