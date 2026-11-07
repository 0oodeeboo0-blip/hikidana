using Hikidana.Core.Clips;
using Hikidana.Core.Text;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Text;

public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 18, 30, 0, TimeSpan.FromHours(9));

    [Fact]
    public void RecentTimes()
    {
        Assert.Equal("たった今", RelativeTime.Format(Now.AddSeconds(-20), Now));
        Assert.Equal("たった今", RelativeTime.Format(Now.AddSeconds(5), Now));
        Assert.Equal("5 分前", RelativeTime.Format(Now.AddMinutes(-5), Now));
        Assert.Equal("3 時間前", RelativeTime.Format(Now.AddHours(-3), Now));
    }

    [Fact]
    public void OlderTimes()
    {
        Assert.Equal("昨日 14:05", RelativeTime.Format(new DateTimeOffset(2026, 11, 6, 14, 5, 0, Now.Offset), Now));
        Assert.Equal("11月1日 9:00", RelativeTime.Format(new DateTimeOffset(2026, 11, 1, 9, 0, 0, Now.Offset), Now));
        Assert.Equal("2025年12月31日", RelativeTime.Format(new DateTimeOffset(2025, 12, 31, 23, 0, 0, Now.Offset), Now));
    }

    [Fact]
    public void EarlyThisMorningIsHoursAgoButLateLastNightIsYesterday()
    {
        var morning = new DateTimeOffset(2026, 11, 7, 0, 30, 0, Now.Offset);
        Assert.Equal("18 時間前", RelativeTime.Format(morning, Now));
        var lastNight = new DateTimeOffset(2026, 11, 6, 23, 50, 0, Now.Offset);
        Assert.Equal("昨日 23:50", RelativeTime.Format(lastNight, Now));
    }

    [Fact]
    public void LabelsAndSizes()
    {
        Assert.Equal("スクリーンショット", ClipLabels.KindName(ClipKind.Screenshot));
        Assert.Equal("ChatGPT", ClipLabels.AppName("ChatGPT.exe"));
        Assert.Equal("メモ帳", ClipLabels.AppName("notepad.exe"));
        Assert.Equal("MyApp", ClipLabels.AppName("MyApp.exe"));
        Assert.Null(ClipLabels.AppName(null));
        Assert.Equal("ドラッグで追加", ClipLabels.AppName(ClipLabels.DropSource));
        Assert.Equal("512 バイト", ClipLabels.Size(512));
        Assert.Equal("1.5 KB", ClipLabels.Size(1536));
        Assert.Equal("1.6 MB", ClipLabels.Size(1_677_722));
    }
}
