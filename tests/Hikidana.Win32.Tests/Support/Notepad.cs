using System.Diagnostics;
using Hikidana.Testing;

namespace Hikidana.Win32.Tests.Support;

/// <summary>テストのために開いたメモ帳。</summary>
internal sealed class Notepad : IDisposable
{
    private Notepad(nint window, nint editor)
    {
        Window = window;
        Editor = editor;
    }

    public nint Window { get; }

    /// <summary>文字を入れる欄（昔のメモ帳は Edit、新しいメモ帳は RichEditD2DPT）。</summary>
    public nint Editor { get; }

    public string Text => Desktop.ControlText(Editor);

    public static async Task<Notepad> OpenAsync()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(path))
        {
            throw new SkipException("メモ帳がありません。");
        }

        var before = Desktop.TopLevelWindows().ToHashSet();
        using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = false }))
        {
        }

        var window = await Desktop.WaitForWindowAsync(w => !before.Contains(w) && Desktop.ClassOf(w) == "Notepad", TimeSpan.FromSeconds(20));
        if (window == 0)
        {
            throw new SkipException("メモ帳のウインドウが出てきませんでした。");
        }

        var editor = Desktop.FindDescendant(window, c => Desktop.ClassOf(c) is "Edit" or "RichEditD2DPT");
        var notepad = new Notepad(window, editor);
        if (editor == 0)
        {
            notepad.Dispose();
            throw new SkipException("メモ帳の入力欄が見つかりませんでした。");
        }

        Console.WriteLine($"       メモ帳: {Desktop.Describe(window)}、入力欄 {Desktop.ClassOf(editor)}");
        return notepad;
    }

    public void Dispose()
    {
        try
        {
            using var process = Process.GetProcessById((int)Desktop.ProcessIdOf(Window));
            process.Kill();
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // もう終わっている
        }
        catch (InvalidOperationException)
        {
        }
    }
}
