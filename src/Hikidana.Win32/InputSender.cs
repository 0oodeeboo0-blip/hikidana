using System.Diagnostics;
using System.Runtime.InteropServices;
using Hikidana.Win32.Native;
using static Hikidana.Win32.Native.NativeMethods;

namespace Hikidana.Win32;

/// <summary>
/// キー操作を送る（SendInput）。押されたままの Shift・Ctrl・Alt・Windows キーがあると
/// Ctrl+V が別の操作（Ctrl+Shift+V や Win+V など）になってしまうので、離れるのを待ってから送る。
/// </summary>
public static class InputSender
{
    /// <summary>自分が送ったキー操作の印（dwExtraInfo）。キーの見張りを付けたときに見分けるため。</summary>
    public const nint ExtraInfo = 0x4869_4B64;

    private static readonly ushort[] Modifiers = [VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN];

    /// <summary>いま押されている Shift・Ctrl・Alt・Windows キー（左右を分けて）。</summary>
    public static IReadOnlyList<ushort> HeldModifiers()
    {
        var held = new List<ushort>();
        foreach (var key in Modifiers)
        {
            if (GetAsyncKeyState(key) < 0)
            {
                held.Add(key);
            }
        }

        return held;
    }

    /// <summary>Shift・Ctrl・Alt・Windows キーがどれも離れるのを待つ。時間内に離れれば true。</summary>
    public static async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (HeldModifiers().Count == 0)
            {
                return true;
            }

            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                return false;
            }

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 押されたままのキーを、離したことにする。
    /// Alt や Windows キーだけを離すと、メニューやスタートが開くことがあるので、その前に意味の無いキーを挟む。
    /// </summary>
    public static bool ReleaseModifiers(IReadOnlyList<ushort> keys)
    {
        if (keys.Count == 0)
        {
            return true;
        }

        var inputs = new List<INPUT>(keys.Count + 2);
        if (keys.Any(k => k is VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN))
        {
            inputs.Add(Key(VK_MASK, up: false));
            inputs.Add(Key(VK_MASK, up: true));
        }

        foreach (var key in keys)
        {
            inputs.Add(Key(key, up: true));
        }

        return Send(CollectionsMarshal.AsSpan(inputs));
    }

    /// <summary>
    /// 割り当ての無いキーを 1 回押したことにする（どのアプリも何もしない）。
    /// Windows は、最後に入力をしたプロセスにだけ前面のウインドウの切り替えを許すので、その前に使う。
    /// </summary>
    public static bool TapMaskKey() => Send([Key(VK_MASK, up: false), Key(VK_MASK, up: true)]);

    /// <summary>Ctrl+V を送る（いま前面にあるウインドウに届く）。Windows が受け付ければ true。</summary>
    public static bool SendCtrlV() =>
        Send([Key(VK_CONTROL, up: false), Key(VK_V, up: false), Key(VK_V, up: true), Key(VK_CONTROL, up: true)]);

    /// <summary>1 つのキーの操作。仮想キーと、いまのキー配列でのスキャンコードの両方を入れる（どちらで読むアプリにも効くように）。</summary>
    private static INPUT Key(ushort virtualKey, bool up)
    {
        var scan = MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC_EX);
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if ((scan & 0xFF00) is 0xE000 or 0xE100)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = (ushort)(scan & 0xFF),
                    dwFlags = flags,
                    dwExtraInfo = ExtraInfo,
                },
            },
        };
    }

    private static unsafe bool Send(ReadOnlySpan<INPUT> inputs)
    {
        fixed (INPUT* pointer = inputs)
        {
            return SendInput((uint)inputs.Length, pointer, sizeof(INPUT)) == inputs.Length;
        }
    }
}
