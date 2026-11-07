using Hikidana.Core.Paste;
using Hikidana.Testing;
using Hikidana.Win32.Tests.Support;

namespace Hikidana.Win32.Tests;

/// <summary>貼り付け先のウインドウやプロセスの見分け方。</summary>
public sealed class WindowTests
{
    [Fact(WindowsOnly = true)]
    public void DesktopIsTreatedAsFileManager()
    {
        var desktop = TestNative.FindWindowEx(0, 0, "Progman", null);
        if (desktop == 0)
        {
            throw new SkipException("デスクトップのウインドウがありません。");
        }

        var target = TargetWindow.From(desktop)!;
        Assert.Equal(PasteTarget.FileManager, target.Kind, target.ToString());
        Assert.Equal("デスクトップ", target.DisplayName);
    }

    [Fact(WindowsOnly = true)]
    public void OwnProcessIsDescribed()
    {
        var pid = (uint)Environment.ProcessId;
        var name = ProcessInfo.ImageName(pid);
        Console.WriteLine($"       このプロセス: {name}（{ProcessInfo.DisplayName(pid)}）、管理者: {ProcessInfo.IsSelfElevated}");
        Assert.True(name is not null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), name);
        Assert.NotNull(ProcessInfo.DisplayName(pid));
        Assert.Equal(ProcessInfo.IsSelfElevated, ProcessInfo.IsElevated(pid));
    }

    [Fact(WindowsOnly = true)]
    public async Task ChromiumWindowIsAnApp()
    {
        var page = await ChromiumPage.SharedAsync();
        var target = TargetWindow.From(page.Window)!;
        Assert.Equal(PasteTarget.App, target.Kind, target.ToString());
        Assert.Equal("msedge.exe", target.ProcessName, target.ToString());
        Assert.Equal("Microsoft Edge", target.DisplayName, target.ToString());
    }

    [Fact(WindowsOnly = true)]
    public void NoModifierKeyIsHeld() => Assert.Equal(0, InputSender.HeldModifiers().Count);
}
