namespace Hikidana.Core.Layout;

/// <summary>小・中・大の 3 つから選ぶ大きさ（取っ手・棚・文字）。設定のファイルには small・medium・large と書く。</summary>
public enum SizeChoice
{
    Small,
    Medium,
    Large,
}

public static class SizeChoices
{
    /// <summary>設定の書き方（small・medium・large）から読む。読めなければ <paramref name="fallback"/>。</summary>
    public static SizeChoice Parse(string? value, SizeChoice fallback) => value?.Trim().ToLowerInvariant() switch
    {
        "small" => SizeChoice.Small,
        "medium" or "normal" => SizeChoice.Medium,
        "large" => SizeChoice.Large,
        _ => fallback,
    };

    /// <summary>設定のファイルに書く名前。</summary>
    public static string ToSetting(this SizeChoice size) => size switch
    {
        SizeChoice.Small => "small",
        SizeChoice.Large => "large",
        _ => "medium",
    };
}

/// <summary>取っ手の大きさ（DIP）。</summary>
/// <param name="Length">端に沿った長さ。</param>
/// <param name="Thickness">マウスを乗せて広がったときの厚み（ウインドウの厚み）。</param>
/// <param name="Hit">ふだん、マウスを受ける帯の厚み（大きいほど当てやすい）。</param>
/// <param name="PillThickness">ふだん見える線の太さ。</param>
/// <param name="PillLength">ふだん見える線の長さ。</param>
public readonly record struct HandleMetrics(double Length, double Thickness, double Hit, double PillThickness, double PillLength)
{
    public static HandleMetrics For(SizeChoice size) => size switch
    {
        SizeChoice.Small => new(72, 24, 6, 3, 40),
        SizeChoice.Large => new(144, 32, 16, 5, 84),
        _ => new(96, 28, 10, 4, 56),
    };
}

/// <summary>置き場所を決めるときの、大きさの選び方。</summary>
public sealed record ShelfLayoutOptions
{
    public static ShelfLayoutOptions Default { get; } = new();

    public SizeChoice HandleSize { get; init; } = SizeChoice.Medium;

    /// <summary>棚の、端に沿った長さ（小 = 半分、中 = 4 分の 3、大 = いっぱい）。</summary>
    public SizeChoice PanelSize { get; init; } = SizeChoice.Large;

    /// <summary>棚の中身の倍率（文字の大きさ）。棚の幅も、これに合わせて広げる。</summary>
    public double ContentScale { get; init; } = 1;

    /// <summary>文字の大きさの倍率。</summary>
    public static double ScaleFor(SizeChoice textSize) => textSize switch
    {
        SizeChoice.Small => 0.9,
        SizeChoice.Large => 1.15,
        _ => 1,
    };

    /// <summary>棚の長さ（使える長さに対して）。</summary>
    public static double LengthFraction(SizeChoice panelSize) => panelSize switch
    {
        SizeChoice.Small => 0.5,
        SizeChoice.Medium => 0.75,
        _ => 1,
    };
}
