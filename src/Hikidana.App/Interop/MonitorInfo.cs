using System.Runtime.InteropServices;
using Hikidana.Core.Layout;

namespace Hikidana.App.Interop;

/// <summary>1 つの画面（モニター）。大きさは本当の画素。</summary>
internal sealed record MonitorInfo(nint Handle, string DeviceName, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary)
{
    /// <summary>メインの画面。</summary>
    public static MonitorInfo Primary() => FromHandle(Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTOPRIMARY));

    /// <summary>名前の画面。無ければ（外したなど）メインの画面。</summary>
    public static MonitorInfo Find(string? deviceName)
    {
        if (deviceName is not null)
        {
            foreach (var monitor in All())
            {
                if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return monitor;
                }
            }
        }

        return Primary();
    }

    /// <summary>つながっている画面すべて。</summary>
    public static unsafe IReadOnlyList<MonitorInfo> All()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            Native.EnumDisplayMonitors(0, 0, &OnMonitor, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        return handles.Select(FromHandle).ToList();
    }

    public static unsafe MonitorInfo FromHandle(nint monitor)
    {
        var info = new Native.MONITORINFOEX { cbSize = (uint)sizeof(Native.MONITORINFOEX) };
        Native.GetMonitorInfo(monitor, &info);
        var name = new string(info.szDevice);
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return new MonitorInfo(
            monitor,
            name,
            PixelRect.FromEdges(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
            PixelRect.FromEdges(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
            scale,
            (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0);
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnMonitor(nint monitor, nint hdc, Native.RECT* rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> handles)
        {
            handles.Add(monitor);
        }

        return 1;
    }
}
