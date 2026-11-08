using System.IO.Compression;
using System.Text;
using Hikidana.Core.Updates;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Updates;

public sealed class UpdateTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hikidana-update-" + Guid.NewGuid().ToString("N")[..8]);

    public UpdateTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void LatestReleaseIsReadFromTheGitHubAnswer()
    {
        var json = """
            {
              "tag_name": "v0.2.1",
              "html_url": "https://github.com/0oodeeboo0-blip/hikidana/releases/tag/v0.2.1",
              "draft": false,
              "prerelease": false,
              "assets": [
                { "name": "Hikidana-0.2.1-win-x64.zip.sha256", "size": 90, "browser_download_url": "https://example.test/sum" },
                { "name": "Hikidana-0.2.1-win-x64.zip", "size": 71000000, "browser_download_url": "https://example.test/zip" }
              ]
            }
            """;
        var release = ReleaseInfo.Parse(Encoding.UTF8.GetBytes(json))!;
        Assert.Equal("v0.2.1", release.Tag);
        Assert.Equal("0.2.1", release.DisplayVersion);
        Assert.Equal("https://example.test/zip", release.ZipUrl);
        Assert.Equal("https://example.test/sum", release.ChecksumUrl);
        Assert.Equal(71000000L, release.ZipSize);
        Assert.True(release.IsNewerThan("0.2.0+abc1234"));
        Assert.False(release.IsNewerThan("0.2.1+def5678"), "同じ版なら新しくない。");
        Assert.False(release.IsNewerThan("0.10.0"), "数として比べる（文字の順ではない）。");
    }

    [Fact]
    public void UnusableAnswersGiveNothing()
    {
        Assert.Null(ReleaseInfo.Parse("{ 壊れている"u8));
        Assert.Null(ReleaseInfo.Parse("""{ "tag_name": "v0.2.1", "assets": [] }"""u8), "配る zip が無い。");
        Assert.Null(ReleaseInfo.Parse("""{ "tag_name": "最新", "assets": [ { "name": "Hikidana-x-win-x64.zip", "browser_download_url": "https://example.test/zip" } ] }"""u8));
        Assert.Null(ReleaseInfo.Parse("""{ "tag_name": "v0.3.0", "draft": true, "assets": [ { "name": "Hikidana-0.3.0-win-x64.zip", "browser_download_url": "https://example.test/zip" } ] }"""u8));
    }

    [Fact]
    public void VersionsAreReadWithoutTheirExtras()
    {
        Assert.Equal(new Version(0, 2, 0), ReleaseInfo.ParseVersion("v0.2.0"));
        Assert.Equal(new Version(0, 2, 0), ReleaseInfo.ParseVersion("0.2.0+0123456789abcdef"));
        Assert.Equal(new Version(1, 4, 0), ReleaseInfo.ParseVersion("1.4"));
        Assert.Equal(new Version(0, 3, 0), ReleaseInfo.ParseVersion("0.3.0-beta.1"));
        Assert.Null(ReleaseInfo.ParseVersion("?"));
        Assert.Null(ReleaseInfo.ParseVersion("1"));
    }

    [Fact]
    public void ExeIsTakenOutOfTheZipAndSwappedIn()
    {
        var current = Path.Combine(dir, "Hikidana.exe");
        File.WriteAllBytes(current, Encoding.ASCII.GetBytes("MZ 古い版"));
        var zip = Path.Combine(dir, "update.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Hikidana/Hikidana.exe").Open(), Encoding.ASCII);
            writer.Write("MZ new version");
        }

        var exe = SelfUpdate.ExtractExe(zip, Path.Combine(dir, "app"));
        Assert.True(SelfUpdate.LooksLikeExe(exe));
        var old = SelfUpdate.Replace(current, exe);
        Assert.Equal("MZ new version", File.ReadAllText(current));
        Assert.Equal(current + ".old", old);
        Assert.True(File.Exists(old));

        // 次に起動したときに、古いほうを消す
        Assert.Equal(1, SelfUpdate.CleanupOld(current));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(current));
    }

    [Fact]
    public void FailedSwapPutsTheOldExeBack()
    {
        var current = Path.Combine(dir, "Hikidana.exe");
        File.WriteAllText(current, "MZ いまの版");
        Assert.Throws<FileNotFoundException>(() => SelfUpdate.Replace(current, Path.Combine(dir, "無い.exe")));
        Assert.Equal("MZ いまの版", File.ReadAllText(current));
        Assert.Equal(0, Directory.GetFiles(dir, "*.old").Length);
    }

    [Fact]
    public void ZipWithoutTheExeIsRejected()
    {
        var zip = Path.Combine(dir, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write("exe はありません");
        }

        Assert.Throws<InvalidDataException>(() => SelfUpdate.ExtractExe(zip, Path.Combine(dir, "app")));
    }

    [Fact]
    public void ChecksumFilesAreRead()
    {
        var file = Path.Combine(dir, "a.zip");
        File.WriteAllText(file, "中身");
        var hash = SelfUpdate.Sha256(file);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, SelfUpdate.ParseChecksum($"{hash.ToUpperInvariant()}  Hikidana-0.2.1-win-x64.zip\n"));
        Assert.Equal(hash, SelfUpdate.ParseChecksum(hash));
        Assert.Null(SelfUpdate.ParseChecksum("見つかりません"));
    }

    [Fact]
    public void OldChecksAreDoneAgain()
    {
        var now = new DateTimeOffset(2026, 11, 8, 11, 48, 0, TimeSpan.FromHours(9));
        Assert.True(UpdateSchedule.IsStale(null, now), "まだ確かめていない");
        Assert.False(UpdateSchedule.IsStale(now.AddMinutes(-3), now), "3 分前に確かめた");
        Assert.True(UpdateSchedule.IsStale(now.AddMinutes(-10), now), "10 分前");
        Assert.True(UpdateSchedule.IsStale(now.AddHours(-1.5), now), "10:15 に確かめて、いま 11:48");
        Assert.True(UpdateSchedule.IsStale(now.AddMinutes(5), now), "時計が戻った");
        Assert.True(UpdateSchedule.Interval <= TimeSpan.FromHours(1), "動いている間も、1 時間に 1 回は確かめる");
    }
}
