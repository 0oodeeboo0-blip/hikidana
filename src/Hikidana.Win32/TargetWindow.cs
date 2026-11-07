using Hikidana.Core.Paste;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>貼り付け先のウインドウ（棚を開く前に前面にあったもの）。</summary>
public sealed record TargetWindow(nint Handle, uint ProcessId, string? ProcessName, string? DisplayName, string ClassName, PasteTarget Kind)
{
    private static readonly HashSet<string> FileManagerClasses = new(StringComparer.Ordinal) { "CabinetWClass", "ExploreWClass", "Progman", "WorkerW" };
    private static readonly HashSet<string> TerminalClasses = new(StringComparer.Ordinal) { "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS" };
    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase) { "WindowsTerminal.exe", "OpenConsole.exe", "conhost.exe" };

    /// <summary>いま前面にあるウインドウ。無ければ null。</summary>
    public static TargetWindow? Foreground() => From(NativeMethods.GetForegroundWindow());

    /// <summary>ウインドウを調べる（子ウインドウなら、いちばん上の親にする）。</summary>
    public static TargetWindow? From(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != 0)
        {
            hwnd = root;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var className = ClassNameOf(hwnd);
        var processName = ProcessInfo.ImageName(pid);
        var kind = FileManagerClasses.Contains(className) && string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase)
            ? PasteTarget.FileManager
            : TerminalClasses.Contains(className) || processName is not null && TerminalProcesses.Contains(processName)
                ? PasteTarget.Terminal
                : PasteTarget.App;
        var displayName = kind != PasteTarget.FileManager
            ? ProcessInfo.DisplayName(pid)
            : className is "Progman" or "WorkerW" ? "デスクトップ" : "エクスプローラー";
        return new TargetWindow(hwnd, pid, processName, displayName, className, kind);
    }

    /// <summary>このウインドウがいま前面にあれば true。</summary>
    public bool IsForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == Handle || NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT) == Handle;
    }

    /// <summary>まだ開いているか。</summary>
    public bool Exists => NativeMethods.IsWindow(Handle);

    public override string ToString() => $"{DisplayName ?? ProcessName ?? "?"}（{ClassName}、{Kind}）";

    private static unsafe string ClassNameOf(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}
