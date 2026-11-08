using System.Text.Json;
using System.Text.Json.Serialization;
using Hikidana.Core.Layout;

namespace Hikidana.Core.Settings;

/// <summary>設定。%LOCALAPPDATA%\Hikidana\settings.json に置く。</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>棚を置く端。</summary>
    public ShelfEdge Edge { get; set; } = ShelfEdge.Right;

    /// <summary>取っ手の位置（端に沿って 0〜1。0.5 で真ん中）。</summary>
    public double HandlePosition { get; set; } = 0.5;

    /// <summary>棚を出す画面（Windows の画面の名前。例: \\.\DISPLAY2）。無ければメインの画面。</summary>
    public string? Monitor { get; set; }

    /// <summary>画面の色: system（Windows に合わせる）・light・dark。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// 取っ手にマウスを乗せるだけで棚を開く（少しのあいだ乗せたままにしたとき）。このときは、棚からマウスが離れると閉じる。
    /// false なら、取っ手をクリックしたときに開く。
    /// </summary>
    public bool OpenOnHover { get; set; }

    /// <summary>貼り付けたら棚を閉じる。</summary>
    public bool CloseAfterPaste { get; set; } = true;

    /// <summary>貼り付けたもの・コピーしたものを、いちばん上へ動かす。</summary>
    public bool MoveToTopOnPaste { get; set; } = true;

    /// <summary>記録を止めている。</summary>
    public bool Paused { get; set; }

    /// <summary>しまっておく数（ピン留めは数えない）。</summary>
    public int MaxItems { get; set; } = 300;

    /// <summary>棚を開くショートカット（例: win+alt+v）。空なら使わない。</summary>
    public string OpenHotkey { get; set; } = Hotkey.Default;

    /// <summary>最後に使ってから、この時間がたったものを自動で消す。0 なら消さない。</summary>
    public int AutoDeleteHours { get; set; }

    /// <summary>前の版の書き方（日数）。読んだら <see cref="AutoDeleteHours"/> に移す（書くときは書かない）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AutoDeleteDays { get; set; }

    /// <summary>自動で消すもの: screenshots（スクリーンショットだけ）・all（ピン留め以外すべて）。</summary>
    public string AutoDeleteScope { get; set; } = "screenshots";

    /// <summary>起動したときに消すもの: off（消さない）・screenshots（ピン留め以外のスクリーンショット）・all（ピン留め以外すべて）。</summary>
    public string ClearOnStart { get; set; } = "off";

    /// <summary>棚に何か入ったら、取っ手から印を少し出して知らせる。</summary>
    public bool SignalOnCapture { get; set; } = true;

    /// <summary>知らせの印: kind（しまったものの種類）・check（チェック）・plain（印なし。色が光るだけ）。</summary>
    public string SignalStyle { get; set; } = "kind";

    /// <summary>取っ手の大きさ: small・medium・large（大きいほど、マウスで当てやすい）。</summary>
    public string HandleSize { get; set; } = "medium";

    /// <summary>棚の長さ（端に沿って）: small（半分）・medium（4 分の 3）・large（いっぱい）。</summary>
    public string PanelSize { get; set; } = "large";

    /// <summary>棚の文字の大きさ: small・medium・large（棚の中身をまとめて縮める・広げる）。</summary>
    public string TextSize { get; set; } = "medium";

    /// <summary>全画面のアプリ（ゲームや全画面の動画）が前にあるあいだは、取っ手を隠す（ショートカットでは開ける）。</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>棚を開け閉めしたときと、貼り付けたときに、小さな音を鳴らす。</summary>
    public bool Sounds { get; set; }

    /// <summary>新しい版が出ていないか、ときどき確かめる（GitHub の Releases）。</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>記録しないアプリ（実行ファイルの名前）。</summary>
    public List<string> ExcludedApps { get; set; } = [];

    /// <summary>初めて起動したときの案内を出した。</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>自動で消すまでの期間（消さないなら null）。</summary>
    public TimeSpan? AutoDeleteAge() => AutoDeleteHours > 0 ? TimeSpan.FromHours(AutoDeleteHours) : null;

    /// <summary>自動で消すものの範囲。</summary>
    public Storage.ClearScope AutoDeleteClearScope() => AutoDeleteScope == "all" ? Storage.ClearScope.All : Storage.ClearScope.Screenshots;

    /// <summary>起動したときに消すものの範囲（消さないなら null）。</summary>
    public Storage.ClearScope? ClearOnStartScope() => ClearOnStart switch
    {
        "screenshots" => Storage.ClearScope.Screenshots,
        "all" => Storage.ClearScope.All,
        _ => null,
    };

    /// <summary>取っ手と棚の大きさと、文字の大きさ。</summary>
    public ShelfLayoutOptions LayoutOptions() => new()
    {
        HandleSize = SizeChoices.Parse(HandleSize, SizeChoice.Medium),
        PanelSize = SizeChoices.Parse(PanelSize, SizeChoice.Large),
        ContentScale = ShelfLayoutOptions.ScaleFor(SizeChoices.Parse(TextSize, SizeChoice.Medium)),
    };

    /// <summary>おかしな値を直す。</summary>
    public AppSettings Normalize()
    {
        if (!Enum.IsDefined(Edge))
        {
            Edge = ShelfEdge.Right;
        }

        HandlePosition = double.IsFinite(HandlePosition) ? Math.Clamp(HandlePosition, 0, 1) : 0.5;
        Theme = Theme?.ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };
        MaxItems = Math.Clamp(MaxItems, 10, 5000);
        OpenHotkey = string.IsNullOrWhiteSpace(OpenHotkey) || OpenHotkey.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Hotkey.Parse(OpenHotkey)?.ToSetting() ?? Hotkey.Default;
        if (AutoDeleteHours <= 0 && AutoDeleteDays > 0)
        {
            // 前の版は日数で覚えていた
            AutoDeleteHours = Math.Min(AutoDeleteDays, 365) * 24;
        }

        AutoDeleteDays = 0;
        AutoDeleteHours = Math.Clamp(AutoDeleteHours, 0, 365 * 24);
        AutoDeleteScope = AutoDeleteScope?.ToLowerInvariant() == "all" ? "all" : "screenshots";
        ClearOnStart = ClearOnStart?.ToLowerInvariant() switch
        {
            "screenshots" => "screenshots",
            "all" => "all",
            _ => "off",
        };
        SignalStyle = SignalStyle?.ToLowerInvariant() switch
        {
            "check" => "check",
            "plain" => "plain",
            _ => "kind",
        };
        HandleSize = SizeChoices.Parse(HandleSize, SizeChoice.Medium).ToSetting();
        PanelSize = SizeChoices.Parse(PanelSize, SizeChoice.Large).ToSetting();
        TextSize = SizeChoices.Parse(TextSize, SizeChoice.Medium).ToSetting();
        ExcludedApps = (ExcludedApps ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(Monitor))
        {
            Monitor = null;
        }

        return this;
    }
}

/// <summary>設定を読み書きする。壊れたファイルは脇によけて、初めの設定から始める。</summary>
public sealed class SettingsStore
{
    private readonly Lock gate = new();

    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static string DefaultPath => System.IO.Path.Combine(Storage.ClipStore.DefaultRoot, "settings.json");

    public AppSettings Load()
    {
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllBytes(Path), SettingsJsonContext.Default.AppSettings);
                return (settings ?? new AppSettings()).Normalize();
            }
            catch (JsonException)
            {
                var broken = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(Path, broken, overwrite: true);
                return new AppSettings();
            }
        }
    }

    /// <summary>書く（途中で止まっても壊れないよう、別のファイルに書いてから入れ替える）。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = Path + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(settings.Normalize(), SettingsJsonContext.Default.AppSettings));
            File.Move(temporary, Path, overwrite: true);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
