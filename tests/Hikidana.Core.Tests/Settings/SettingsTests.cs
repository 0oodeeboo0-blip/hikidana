using Hikidana.Core.Layout;
using Hikidana.Core.Settings;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hikidana-settings-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = new SettingsStore(Path.Combine(dir, "settings.json")).Load();

        Assert.Equal(ShelfEdge.Right, settings.Edge);
        Assert.Equal(0.5, settings.HandlePosition);
        Assert.True(settings.CloseAfterPaste);
        Assert.False(settings.OpenOnHover);
        Assert.Equal("system", settings.Theme);
        Assert.Equal("win+alt+v", settings.OpenHotkey);
        Assert.Equal(0, settings.AutoDeleteDays);
        Assert.Null(settings.AutoDeleteAge());
        Assert.Equal(Hikidana.Core.Storage.ClearScope.Screenshots, settings.AutoDeleteClearScope());
        Assert.True(settings.SignalOnCapture);
    }

    [Fact]
    public void NewSettingsSurviveAndOldFilesGetDefaults()
    {
        var path = Path.Combine(dir, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { OpenHotkey = "", AutoDeleteDays = 7, AutoDeleteScope = "all", SignalOnCapture = false });
        var loaded = store.Load();
        Assert.Equal(string.Empty, loaded.OpenHotkey, "空は「使わない」のまま。");
        Assert.Equal(TimeSpan.FromDays(7), loaded.AutoDeleteAge());
        Assert.Equal(Hikidana.Core.Storage.ClearScope.All, loaded.AutoDeleteClearScope());
        Assert.False(loaded.SignalOnCapture);

        // 前の版の設定のファイル（新しい項目が無い）は、初めの値になる
        File.WriteAllText(path, "{ \"version\": 1, \"edge\": \"left\" }");
        var old = store.Load();
        Assert.Equal("win+alt+v", old.OpenHotkey);
        Assert.Equal(0, old.AutoDeleteDays);
        Assert.True(old.SignalOnCapture);
    }

    [Fact]
    public void OddHotkeysAndDaysAreFixed()
    {
        var settings = new AppSettings { OpenHotkey = "Alt + Shift + V", AutoDeleteDays = 9999, AutoDeleteScope = "everything" }.Normalize();
        Assert.Equal("alt+shift+v", settings.OpenHotkey);
        Assert.Equal(365, settings.AutoDeleteDays);
        Assert.Equal("screenshots", settings.AutoDeleteScope);
        Assert.Equal("win+alt+v", new AppSettings { OpenHotkey = "shift+v" }.Normalize().OpenHotkey, "使えない組み合わせは、初めの値に戻す。");
        Assert.Equal(string.Empty, new AppSettings { OpenHotkey = "none" }.Normalize().OpenHotkey);
    }

    [Fact]
    public void SavedSettingsComeBack()
    {
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        store.Save(new AppSettings { Edge = ShelfEdge.Left, HandlePosition = 0.2, Monitor = @"\\.\DISPLAY2", ExcludedApps = ["KeePass.exe"], Paused = true, OpenOnHover = true });

        var loaded = store.Load();
        Assert.Equal(ShelfEdge.Left, loaded.Edge);
        Assert.Equal(0.2, loaded.HandlePosition);
        Assert.Equal(@"\\.\DISPLAY2", loaded.Monitor);
        Assert.SequenceEqual(["KeePass.exe"], loaded.ExcludedApps);
        Assert.True(loaded.Paused);
        Assert.True(loaded.OpenOnHover);
        Assert.Contains("\"edge\": \"Left\"", File.ReadAllText(store.Path));
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void BrokenFileIsSetAside()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ 壊れている");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(ShelfEdge.Right, settings.Edge);
        Assert.False(File.Exists(path));
        Assert.Equal(1, Directory.GetFiles(dir, "settings.broken-*.json").Length);
    }

    [Fact]
    public void OddValuesAreFixed()
    {
        var settings = new AppSettings
        {
            Edge = (ShelfEdge)42,
            HandlePosition = double.PositiveInfinity,
            Theme = "Dark",
            MaxItems = 0,
            Monitor = " ",
            ExcludedApps = ["a.exe", "A.EXE", " ", "b.exe "],
        }.Normalize();

        Assert.Equal(ShelfEdge.Right, settings.Edge);
        Assert.Equal(0.5, settings.HandlePosition);
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(10, settings.MaxItems);
        Assert.Null(settings.Monitor);
        Assert.SequenceEqual(["a.exe", "b.exe"], settings.ExcludedApps);
    }
}
