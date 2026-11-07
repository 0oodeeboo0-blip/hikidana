using Microsoft.Win32;

namespace Hikidana.Win32;

/// <summary>
/// Windows にサインインしたら起動する設定。いまの利用者の「Run」（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）に
/// exe の場所を書く。Windows の「スタートアップ アプリ」やタスク マネージャーで切られているとき
/// （Explorer\StartupApproved\Run の値の最初のバイトが奇数）は、切れているとみなす。
/// 管理者の権限は要らない。読み書きできないときは例外（UnauthorizedAccessException など）がそのまま出る。
/// </summary>
public sealed class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <param name="valueName">Run に書く値の名前（テストでは別の名前にする）。</param>
    public StartupRegistration(string valueName = "Hikidana")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ValueName = valueName;
    }

    public string ValueName { get; }

    /// <summary>Run に書いてあるコマンド。無ければ null。</summary>
    public string? Command
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    /// <summary>Windows のスタートアップ アプリの一覧で、利用者が切っている。</summary>
    public bool IsDisabledByUser
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
    }

    /// <summary>サインインしたら起動する（Run に書いてあって、切られていない）。</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Command) && !IsDisabledByUser;

    /// <summary>Run に書くコマンド（exe の場所を "" で囲む）。</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    /// <summary>サインインしたら <paramref name="exePath"/> を起動するようにする（スタートアップ アプリで切られていれば、入れ直す）。</summary>
    public void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            key.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
        }

        ClearApproval();
    }

    /// <summary>サインインしても起動しないようにする。</summary>
    public void Disable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        ClearApproval();
    }

    /// <summary>
    /// 起動するようにしてあるのに、書いてある exe の場所が <paramref name="exePath"/> と違う（exe を動かした・新しい版に替えた）なら、
    /// 書き直す。書き直したら true。
    /// </summary>
    public bool Refresh(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var command = Command;
        if (string.IsNullOrWhiteSpace(command) || string.Equals(command, CommandFor(exePath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, CommandFor(exePath), RegistryValueKind.String);
        return true;
    }

    /// <summary>スタートアップ アプリの入り切りの記録を消す（消すと「入」として扱われる）。</summary>
    private void ClearApproval()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
