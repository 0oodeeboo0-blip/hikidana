using Hikidana.Core.Storage;

namespace Hikidana.Win32.Tests.Support;

/// <summary>テストのあいだだけ使うフォルダー。終わったら消す。</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hikidana-tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(string name)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>このフォルダーの中の棚。</summary>
    public ClipStore Store(string name = "store")
    {
        var store = new ClipStore(Sub(name));
        store.Load();
        return store;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
