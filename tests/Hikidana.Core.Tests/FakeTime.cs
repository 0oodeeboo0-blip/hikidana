namespace Hikidana.Core.Tests;

/// <summary>テスト用の時計。進めるまで止まっている。地域の時刻は UTC と同じ。</summary>
internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
