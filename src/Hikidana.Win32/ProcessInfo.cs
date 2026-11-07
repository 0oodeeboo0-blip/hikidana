using System.Diagnostics;
using System.Runtime.InteropServices;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>プロセスの名前や、管理者として動いているかを調べる。</summary>
public static unsafe class ProcessInfo
{
    private static readonly Lazy<bool> SelfElevated = new(() => IsElevatedProcess(NativeMethods.GetCurrentProcess(), ownHandle: false) ?? false);

    /// <summary>このアプリ自身が管理者として動いていれば true。</summary>
    public static bool IsSelfElevated => SelfElevated.Value;

    /// <summary>実行ファイルのフルパス。調べられなければ null。</summary>
    public static string? ImagePath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            var size = (uint)capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>実行ファイルの名前（例: chrome.exe）。</summary>
    public static string? ImageName(uint processId) => ImagePath(processId) is { } path ? Path.GetFileName(path) : null;

    /// <summary>アプリの表示名（実行ファイルの「ファイルの説明」。無ければ名前から .exe を除いたもの）。</summary>
    public static string? DisplayName(uint processId)
    {
        var path = ImagePath(processId);
        if (path is null)
        {
            return null;
        }

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description))
            {
                return description;
            }
        }
        catch (FileNotFoundException)
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// 管理者として動いていれば true、そうでなければ false。
    /// 調べられないとき（権限が足りないなど）は true とみなす（管理者のアプリは調べさせてくれないことが多いため）。
    /// </summary>
    public static bool IsElevated(uint processId)
    {
        if (processId == 0)
        {
            return false;
        }

        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return Marshal.GetLastPInvokeError() == 5; // ERROR_ACCESS_DENIED
        }

        return IsElevatedProcess(process, ownHandle: true) ?? true;
    }

    private static bool? IsElevatedProcess(nint process, bool ownHandle)
    {
        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token))
            {
                return null;
            }

            try
            {
                int elevated;
                return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, &elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            if (ownHandle)
            {
                NativeMethods.CloseHandle(process);
            }
        }
    }
}
