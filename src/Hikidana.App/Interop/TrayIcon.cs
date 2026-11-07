using System.Windows.Interop;
using Hikidana.Core.Diagnostics;

namespace Hikidana.App.Interop;

/// <summary>メニューの 1 行。<see cref="Id"/> が 0 なら区切りの線。</summary>
internal sealed record TrayMenuItem(int Id, string? Text, bool Checked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(0, null);
}

/// <summary>
/// 通知領域（タスクトレイ）のアイコン。クリックで棚を開き、右クリックでメニューを出す。
/// エクスプローラーが再起動したとき（TaskbarCreated）は、アイコンを出し直す。
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = Native.WM_APP + 0x31;
    private const uint IconId = 1;
    private const int ApplicationIconResource = 32512;
    private readonly HwndSource window;
    private readonly HwndSourceHook hook;
    private readonly uint taskbarCreated;
    private nint icon;
    private string tooltip;
    private bool added;

    public TrayIcon(string tooltip)
    {
        this.tooltip = tooltip;
        taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        // 見えないウインドウで知らせを受ける（いちばん上のウインドウでないと TaskbarCreated が届かない）
        window = new HwndSource(new HwndSourceParameters("Hikidana のタスクトレイ")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP（見せない）
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
        });
        hook = WndProc;
        window.AddHook(hook);
        icon = LoadIcon();
        Add();
    }

    /// <summary>アイコンをクリックした（キーボードで選んだときも）。</summary>
    public event Action? Clicked;

    /// <summary>右クリックで出すメニュー。選ばれたものの番号を返す仕組みに渡す。</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuItems { get; set; }

    /// <summary>メニューで選ばれた。</summary>
    public event Action<int>? MenuItemSelected;

    /// <summary>マウスを乗せたときの説明を変える。</summary>
    public void SetTooltip(string text)
    {
        tooltip = text;
        if (added)
        {
            var data = Data(Native.NIF_TIP | Native.NIF_SHOWTIP);
            Native.Shell_NotifyIcon(Native.NIM_MODIFY, &data);
        }
    }

    public void Dispose()
    {
        if (added)
        {
            var data = Data(0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, &data);
            added = false;
        }

        if (icon != 0)
        {
            Native.DestroyIcon(icon);
            icon = 0;
        }

        window.RemoveHook(hook);
        window.Dispose();
    }

    private void Add()
    {
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        added = Native.Shell_NotifyIcon(Native.NIM_ADD, &data);
        if (!added)
        {
            Log.Warn("タスクトレイにアイコンを出せませんでした。");
            return;
        }

        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, &data);
    }

    private Native.NOTIFYICONDATAW Data(uint flags)
    {
        var data = new Native.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Native.NOTIFYICONDATAW),
            hWnd = window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = icon,
        };
        var text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        for (var i = 0; i < text.Length; i++)
        {
            data.szTip[i] = text[i];
        }

        data.szTip[text.Length] = '\0';
        return data;
    }

    /// <summary>アプリのアイコンを、通知領域の大きさで読む。</summary>
    private nint LoadIcon()
    {
        var dpi = Native.GetDpiForWindow(window.Handle);
        dpi = dpi == 0 ? 96 : dpi;
        var cx = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);
        var cy = Native.GetSystemMetricsForDpi(Native.SM_CYSMICON, dpi);
        if (Native.LoadIconWithScaleDown(Native.GetModuleHandle(null), ApplicationIconResource, cx, cy, out var loaded) == 0 && loaded != 0)
        {
            return loaded;
        }

        // 見つからなければ、exe の最初のアイコンを使う
        nint small = 0;
        if (Environment.ProcessPath is { } exe && Native.ExtractIconEx(exe, 0, null, &small, 1) > 0 && small != 0)
        {
            return small;
        }

        return 0;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)taskbarCreated)
        {
            // エクスプローラーが再起動した
            added = false;
            Add();
            return 0;
        }

        if (msg != (int)CallbackMessage)
        {
            return 0;
        }

        handled = true;
        var eventCode = (int)(lParam & 0xFFFF);
        switch (eventCode)
        {
            case Native.NIN_SELECT:
            case Native.NIN_KEYSELECT:
                Clicked?.Invoke();
                break;
            case Native.WM_CONTEXTMENU:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }

        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        if (MenuItems?.Invoke() is not { } items)
        {
            return;
        }

        var menu = Native.CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.Id == 0)
                {
                    Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
                    continue;
                }

                Native.AppendMenu(menu, Native.MF_STRING | (item.Checked ? Native.MF_CHECKED : 0), (nuint)item.Id, item.Text);
                if (item.IsDefault)
                {
                    Native.SetMenuDefaultItem(menu, (uint)item.Id, 0);
                }
            }

            // メニューの外をクリックしたら閉じるよう、いったん前面にする（Windows の決まり）
            Native.SetForegroundWindow(window.Handle);
            var chosen = Native.TrackPopupMenuEx(menu, Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY, x, y, window.Handle, 0);
            Native.PostMessage(window.Handle, Native.WM_NULL, 0, 0);
            if (chosen > 0)
            {
                MenuItemSelected?.Invoke(chosen);
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }
}
