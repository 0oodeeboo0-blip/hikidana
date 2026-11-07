namespace Hikidana.Core.Text;

/// <summary>「たった今」「5 分前」「昨日 14:20」のような、いつかの書き方。</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "たった今";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} 分前";
        }

        var whenLocal = when.ToOffset(now.Offset);
        if (whenLocal.Date == now.Date)
        {
            return $"{(int)elapsed.TotalHours} 時間前";
        }

        if (whenLocal.Date == now.Date.AddDays(-1))
        {
            return $"昨日 {whenLocal:H:mm}";
        }

        return whenLocal.Year == now.Year
            ? $"{whenLocal.Month}月{whenLocal.Day}日 {whenLocal:H:mm}"
            : $"{whenLocal.Year}年{whenLocal.Month}月{whenLocal.Day}日";
    }
}
