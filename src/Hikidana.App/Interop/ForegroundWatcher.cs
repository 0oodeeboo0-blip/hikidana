using System.Runtime.InteropServices;
using Hikidana.Core.Diagnostics;

namespace Hikidana.App.Interop;

/// <summary>
/// 前面のウインドウが変わったことを知る（ほかのアプリをクリックしたら棚を閉じるため）。
/// 知らせは、作ったスレッド（画面のスレッド）で受ける。自分のプロセスのウインドウの変化は知らせない。
/// </summary>
internal sealed unsafe class ForegroundWatcher : IDisposable
{
    private static ForegroundWatcher? current;
    private nint hook;

    public ForegroundWatcher()
    {
        current = this;
        hook = Native.SetWinEventHook(
            Native.EVENT_SYSTEM_FOREGROUND,
            Native.EVENT_SYSTEM_FOREGROUND,
            0,
            &OnEvent,
            0,
            0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (hook == 0)
        {
            Log.Warn("前面のウインドウの変化を見張れませんでした。");
        }
    }

    /// <summary>前面になったウインドウ。</summary>
    public event Action<nint>? Changed;

    public void Dispose()
    {
        if (hook != 0)
        {
            Native.UnhookWinEvent(hook);
            hook = 0;
        }

        if (ReferenceEquals(current, this))
        {
            current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint time)
    {
        try
        {
            current?.Changed?.Invoke(hwnd);
        }
        catch (Exception ex)
        {
            Log.Error("前面のウインドウの変化を受けたときに例外", ex);
        }
    }
}
