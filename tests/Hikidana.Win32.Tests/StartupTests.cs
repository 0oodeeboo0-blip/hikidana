using Hikidana.Testing;
using Microsoft.Win32;

namespace Hikidana.Win32.Tests;

/// <summary>Windows にサインインしたら起動する設定（本物の HKCU の Run に、テスト用の名前で書いて消す）。</summary>
public sealed class StartupTests
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    [Fact(WindowsOnly = true)]
    public void EnableWritesTheQuotedPathAndDisableRemovesIt()
    {
        var startup = new StartupRegistration($"Hikidana.Test.{Guid.NewGuid():N}");
        try
        {
            Assert.False(startup.IsEnabled);
            startup.Enable(@"C:\Program Files\Hikidana\Hikidana.exe");
            Assert.True(startup.IsEnabled);
            Assert.Equal("\"C:\\Program Files\\Hikidana\\Hikidana.exe\"", RunValue(startup.ValueName));

            // 同じ場所なら書き直さない。exe を動かしたら書き直す
            Assert.False(startup.Refresh(@"C:\Program Files\Hikidana\Hikidana.exe"));
            Assert.True(startup.Refresh(@"D:\Tools\Hikidana\Hikidana.exe"));
            Assert.Equal("\"D:\\Tools\\Hikidana\\Hikidana.exe\"", RunValue(startup.ValueName));

            startup.Disable();
            Assert.False(startup.IsEnabled);
            Assert.Null(RunValue(startup.ValueName));

            // 切ってあるときは、場所を書き直さない（勝手に入れない）
            Assert.False(startup.Refresh(@"C:\Hikidana.exe"));
            Assert.Null(RunValue(startup.ValueName));
        }
        finally
        {
            Cleanup(startup.ValueName);
        }
    }

    [Fact(WindowsOnly = true)]
    public void TurnedOffInWindowsSettingsCountsAsOffUntilEnabledAgain()
    {
        var startup = new StartupRegistration($"Hikidana.Test.{Guid.NewGuid():N}");
        try
        {
            startup.Enable(@"C:\Hikidana\Hikidana.exe");

            // タスク マネージャーや「スタートアップ アプリ」で切ったときと同じ印（最初のバイトが 3）
            using (var key = Registry.CurrentUser.CreateSubKey(ApprovedKey, writable: true))
            {
                key.SetValue(startup.ValueName, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }

            Assert.True(startup.IsDisabledByUser);
            Assert.False(startup.IsEnabled);

            // 入れ直すと、切った印も消える
            startup.Enable(@"C:\Hikidana\Hikidana.exe");
            Assert.False(startup.IsDisabledByUser);
            Assert.True(startup.IsEnabled);
        }
        finally
        {
            Cleanup(startup.ValueName);
        }
    }

    private static string? RunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    private static void Cleanup(string name)
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue(name, throwOnMissingValue: false);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(name, throwOnMissingValue: false);
    }
}
