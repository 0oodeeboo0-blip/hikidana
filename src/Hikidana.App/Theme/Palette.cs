using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Hikidana.App.Theme;

/// <summary>
/// 画面の色（案A「なじむ」）。ライトとダーク、Windows のアクセント色に合わせて、
/// アプリの資源に Hkd. で始まる名前のブラシを入れる。画面は DynamicResource で使うので、入れ替えるとすぐ変わる。
/// </summary>
internal static class Palette
{
    private static string theme = "system";

    /// <summary>いまダークで表示している。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>色を入れ直した（ライトとダークやアクセント色が変わった）。</summary>
    public static event Action? Changed;

    /// <summary>色を決めて入れる。<paramref name="setting"/> は system・light・dark。</summary>
    public static void Apply(Application app, string setting)
    {
        theme = setting;
        IsDark = setting switch
        {
            "dark" => true,
            "light" => false,
            _ => !AppsUseLightTheme(),
        };

        var accent = Accent(IsDark);
        var resources = app.Resources;
        if (IsDark)
        {
            Set(resources, "Hkd.PanelBackground", 0xFF242424);
            Set(resources, "Hkd.PanelBorder", 0x33FFFFFF);
            Set(resources, "Hkd.CardBackground", 0x0EFFFFFF);
            Set(resources, "Hkd.CardBackgroundHover", 0x17FFFFFF);
            Set(resources, "Hkd.CardBackgroundPressed", 0x0AFFFFFF);
            Set(resources, "Hkd.CardBorder", 0x12FFFFFF);
            Set(resources, "Hkd.CardBorderHover", 0x1FFFFFFF);
            Set(resources, "Hkd.TextPrimary", 0xFFFFFFFF);
            Set(resources, "Hkd.TextSecondary", 0xFFD1D1D1);
            Set(resources, "Hkd.TextTertiary", 0xFFA8A8A8);
            Set(resources, "Hkd.SubtleHover", 0x0FFFFFFF);
            Set(resources, "Hkd.SubtlePressed", 0x0AFFFFFF);
            Set(resources, "Hkd.Divider", 0x15FFFFFF);
            Set(resources, "Hkd.ImageBackdrop", 0x0AFFFFFF);
            Set(resources, "Hkd.SuccessBackground", 0xFF393D1B);
            Set(resources, "Hkd.Success", 0xFF6CCB5F);
            Set(resources, "Hkd.CautionBackground", 0xFF433519);
            Set(resources, "Hkd.Caution", 0xFFFCE100);
            Set(resources, "Hkd.CriticalBackground", 0xFF442726);
            Set(resources, "Hkd.Critical", 0xFFFF99A4);
            Set(resources, "Hkd.InfoBackground", 0xFF2C2C2C);
            Set(resources, "Hkd.HandleTab", 0xFF2C2C2C);
            Set(resources, "Hkd.HandleStripEdge", 0x66000000);

            // 設定の画面（Windows 11 の設定と同じ考え方の色）
            Set(resources, "Hkd.SettingsBackground", 0xFF202020);
            Set(resources, "Hkd.ControlStrongStroke", 0x8BFFFFFF);
            Set(resources, "Hkd.ToggleFillOff", 0x19000000);
            Set(resources, "Hkd.ToggleFillOffHover", 0x0BFFFFFF);
            Set(resources, "Hkd.ToggleKnobOff", 0xC5FFFFFF);
            Set(resources, "Hkd.FocusStroke", 0xFFFFFFFF);
            Set(resources, "Hkd.ScreenBezel", 0xFF0B0B0B);
            Set(resources, "Hkd.ScreenBezelEdge", 0x38FFFFFF);
            Set(resources, "Hkd.ScreenStand", 0xFF555555);
            Set(resources, "Hkd.ScreenTaskbar", 0xE61C1C1C);
            Set(resources, "Hkd.MiniPanel", 0xFF2C2C2C);
            Set(resources, "Hkd.MiniCard", 0x26FFFFFF);
            Set(resources, "Hkd.GhostFill", 0x2EFFFFFF);
        }
        else
        {
            Set(resources, "Hkd.PanelBackground", 0xFFF3F3F3);
            Set(resources, "Hkd.PanelBorder", 0x1F000000);
            Set(resources, "Hkd.CardBackground", 0xBDFFFFFF);
            Set(resources, "Hkd.CardBackgroundHover", 0xFFFFFFFF);
            Set(resources, "Hkd.CardBackgroundPressed", 0xFFF9F9F9);
            Set(resources, "Hkd.CardBorder", 0x0F000000);
            Set(resources, "Hkd.CardBorderHover", 0x1A000000);
            Set(resources, "Hkd.TextPrimary", 0xFF1A1A1A);
            Set(resources, "Hkd.TextSecondary", 0xFF4F4F4F);
            Set(resources, "Hkd.TextTertiary", 0xFF666666);
            Set(resources, "Hkd.SubtleHover", 0x0A000000);
            Set(resources, "Hkd.SubtlePressed", 0x06000000);
            Set(resources, "Hkd.Divider", 0x14000000);
            Set(resources, "Hkd.ImageBackdrop", 0x08000000);
            Set(resources, "Hkd.SuccessBackground", 0xFFDFF6DD);
            Set(resources, "Hkd.Success", 0xFF0F7B0F);
            Set(resources, "Hkd.CautionBackground", 0xFFFFF4CE);
            Set(resources, "Hkd.Caution", 0xFF9D5D00);
            Set(resources, "Hkd.CriticalBackground", 0xFFFDE7E9);
            Set(resources, "Hkd.Critical", 0xFFC42B1C);
            Set(resources, "Hkd.InfoBackground", 0xFFFBFBFB);
            Set(resources, "Hkd.HandleTab", 0xFFFBFBFB);
            Set(resources, "Hkd.HandleStripEdge", 0x99FFFFFF);

            Set(resources, "Hkd.SettingsBackground", 0xFFF3F3F3);
            Set(resources, "Hkd.ControlStrongStroke", 0x72000000);
            Set(resources, "Hkd.ToggleFillOff", 0x06000000);
            Set(resources, "Hkd.ToggleFillOffHover", 0x0F000000);
            Set(resources, "Hkd.ToggleKnobOff", 0x9E000000);
            Set(resources, "Hkd.FocusStroke", 0xE4000000);
            Set(resources, "Hkd.ScreenBezel", 0xFF2B2B2B);
            Set(resources, "Hkd.ScreenBezelEdge", 0x00000000);
            Set(resources, "Hkd.ScreenStand", 0xFFC2C2C2);
            Set(resources, "Hkd.ScreenTaskbar", 0xD9F3F3F3);
            Set(resources, "Hkd.MiniPanel", 0xFFFAFAFA);
            Set(resources, "Hkd.MiniCard", 0x17000000);
            Set(resources, "Hkd.GhostFill", 0x99FFFFFF);
        }

        Set(resources, "Hkd.Accent", accent);
        Set(resources, "Hkd.AccentText", accent);
        Set(resources, "Hkd.OnAccent", IsDark ? Colors.Black : Colors.White);
        Set(resources, "Hkd.AccentHover", Color.FromArgb(0xE6, accent.R, accent.G, accent.B));
        Set(resources, "Hkd.AccentPressed", Color.FromArgb(0xCC, accent.R, accent.G, accent.B));
        Set(resources, "Hkd.AccentSoft", Color.FromArgb(0x2E, accent.R, accent.G, accent.B));

        // 設定の画面の「画面の図」の壁紙: アクセント色をうすく混ぜたグラデーション（Windows 11 の壁紙のような色合い）
        var desk = IsDark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Colors.White;
        var wallpaper = new LinearGradientBrush(Mix(desk, accent, IsDark ? 0.28 : 0.14), Mix(desk, accent, IsDark ? 0.62 : 0.5), new Point(0, 0), new Point(1, 1));
        wallpaper.Freeze();
        resources["Hkd.ScreenWallpaper"] = wallpaper;
        Changed?.Invoke();
    }

    /// <summary>Windows の設定が変わったときに、色を入れ直す。</summary>
    public static void Watch(Application app)
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app, theme));
            }
        };
    }

    /// <summary>Windows がアプリをライトで表示する設定なら true。</summary>
    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// アクセント色。Windows がアクセント色から作る 8 色（AccentPalette）のうち、
    /// ライトでは少し濃い色、ダークでは少し明るい色を使う（Windows 11 のアプリと同じ）。
    /// </summary>
    public static Color Accent(bool dark)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
            {
                var i = dark ? 1 : 4;
                return Color.FromRgb(palette[i * 4], palette[(i * 4) + 1], palette[(i * 4) + 2]);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }

        return dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
    }

    /// <summary>2 つの色を混ぜる（<paramref name="amount"/> が 0 なら <paramref name="from"/>、1 なら <paramref name="to"/>）。</summary>
    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static void Set(ResourceDictionary resources, string key, uint argb) =>
        Set(resources, key, Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }
}
