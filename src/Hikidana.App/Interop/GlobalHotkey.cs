using System.Runtime.InteropServices;
using System.Windows.Interop;
using Hikidana.Core.Settings;

namespace Hikidana.App.Interop;

/// <summary>
/// どのアプリを使っていても効くショートカット（RegisterHotKey）。知らせを受けるための、見えないウインドウを持つ。
/// ほかのアプリが同じ組み合わせを使っていると登録できない（そのときは <see cref="Register"/> が false を返す）。
/// 画面のスレッドで作って使う。
/// </summary>
internal sealed partial class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 1;

    private readonly HwndSource source;

    // HwndSource は登録した関数を弱く持つことがあるので、ここで持ち続ける
    private readonly HwndSourceHook hook;
    private bool registered;

    public GlobalHotkey()
    {
        // 見えない、知らせを受けるだけのウインドウ（HWND_MESSAGE の子）
        source = new HwndSource(new HwndSourceParameters("Hikidana.Hotkey") { ParentWindow = new nint(-3), WindowStyle = 0 });
        hook = OnMessage;
        source.AddHook(hook);
    }

    /// <summary>ショートカットが押された（画面のスレッドで呼ばれる）。</summary>
    public event Action? Pressed;

    /// <summary>いま登録しているもの（無ければ null）。</summary>
    public Hotkey? Current { get; private set; }

    /// <summary>登録する（前のものは外す）。null なら外すだけ。ほかのアプリが使っていて登録できなければ false。</summary>
    public bool Register(Hotkey? hotkey)
    {
        Unregister();
        if (hotkey is not { } key)
        {
            return true;
        }

        if (!RegisterHotKey(source.Handle, HotkeyId, (uint)key.Modifiers | MOD_NOREPEAT, key.VirtualKey))
        {
            return false;
        }

        registered = true;
        Current = key;
        return true;
    }

    public void Unregister()
    {
        if (registered)
        {
            UnregisterHotKey(source.Handle, HotkeyId);
            registered = false;
        }

        Current = null;
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(hook);
        source.Dispose();
    }

    private nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
