namespace Hikidana.Core.Updates;

/// <summary>新しい版を、いつ確かめるか。</summary>
public static class UpdateSchedule
{
    /// <summary>起動してから、はじめに確かめるまで（起動してすぐは混み合うので、少し待つ）。</summary>
    public static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);

    /// <summary>新しい版に入れ替えて起動し直したあと、はじめに確かめるまで。</summary>
    public static readonly TimeSpan FirstCheckAfterUpdate = TimeSpan.FromSeconds(5);

    /// <summary>スリープから戻ってから確かめるまで（ネットにつながるのを待つ）。</summary>
    public static readonly TimeSpan AfterResume = TimeSpan.FromSeconds(30);

    /// <summary>動いている間に確かめる間隔（GitHub は、ログインせずに 1 時間に 60 回まで聞ける。1 回なら十分に少ない）。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>設定の画面を開いたとき、前に確かめてからこれだけたっていれば、もう一度確かめる。</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>前に確かめたのが古い（まだ確かめていない、時計が戻ったときも）なら true。</summary>
    public static bool IsStale(DateTimeOffset? lastChecked, DateTimeOffset now) =>
        lastChecked is not { } last || last > now || now - last >= StaleAfter;
}
