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

    /// <summary>最後に使ってから、この日数がたったものを自動で消す。0 なら消さない。</summary>
    public int AutoDeleteDays { get; set; }

    /// <summary>自動で消すもの: screenshots（スクリーンショットだけ）・all（ピン留め以外すべて）。</summary>
    public string AutoDeleteScope { get; set; } = "screenshots";

    /// <summary>棚に何か入ったら、取っ手から印を少し出して知らせる。</summary>
    public bool SignalOnCapture { get; set; } = true;

    /// <summary>新しい版が出ていないか、ときどき確かめる（GitHub の Releases）。</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>記録しないアプリ（実行ファイルの名前）。</summary>
    public List<string> ExcludedApps { get; set; } = [];

    /// <summary>初めて起動したときの案内を出した。</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>自動で消すまでの期間（消さないなら null）。</summary>
    public TimeSpan? AutoDeleteAge() => AutoDeleteDays > 0 ? TimeSpan.FromDays(AutoDeleteDays) : null;

    /// <summary>自動で消すものの範囲。</summary>
    public Storage.ClearScope AutoDeleteClearScope() => AutoDeleteScope == "all" ? Storage.ClearScope.All : Storage.ClearScope.Screenshots;

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
        AutoDeleteDays = Math.Clamp(AutoDeleteDays, 0, 365);
        AutoDeleteScope = AutoDeleteScope?.ToLowerInvariant() == "all" ? "all" : "screenshots";
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
