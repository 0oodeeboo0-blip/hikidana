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
