using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hikidana.App.Interop;
using Hikidana.App.Settings;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Layout;
using Hikidana.Core.Settings;
using Hikidana.Core.Storage;
using Hikidana.Win32;

namespace Hikidana.App.Shelf;

/// <summary>設定の画面からの変更を受けて、すぐ棚に反映して保存する。</summary>
internal sealed partial class ShelfController : ISettingsHost
{
    private SettingsWindow? settingsWindow;

    /// <summary>見本で動かしているときの「サインインしたら起動する」（本物の設定は変えない）。</summary>
    private bool demoStartWithWindows;

    public event Action? SettingsChanged;

    AppSettings ISettingsHost.Settings => settings;

    ShelfLayout ISettingsHost.Layout => layout;

    MonitorInfo ISettingsHost.Monitor => MonitorInfo.Find(settings.Monitor);

    IReadOnlyList<MonitorInfo> ISettingsHost.Monitors => MonitorInfo.All();

    bool ISettingsHost.StartWithWindows
    {
        get
        {
            if (startup is null)
            {
                return demoStartWithWindows;
            }

            try
            {
                return startup.IsEnabled;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Log.Warn("サインインしたときの起動の設定を読めませんでした。", ex);
                return false;
            }
        }
    }

    /// <summary>設定の画面を開く（開いていれば前に出す）。開いている棚は閉じる。</summary>
    public void OpenSettings()
    {
        if (disposed)
        {
            return;
        }

        if (isOpen && !keepOpen)
        {
            Close();
        }

        if (settingsWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = new SettingsWindow(this);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        };
        settingsWindow = window;
        window.Show();
        window.Activate();
        Log.Info("設定の画面を開きました。");
    }

    void ISettingsHost.SetEdge(ShelfEdge edge)
    {
        if (settings.Edge == edge)
        {
            return;
        }

        settings.Edge = edge;
        Relayout();
        SaveSettings();
        Log.Info($"棚の端を変えました: {edge}（{layout.Handle}）");
        ShowToast($"棚を{EdgeName(edge)}に出すようにしました。取っ手はここです。", ToastKind.Info, TimeSpan.FromSeconds(4));
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetMonitor(string? deviceName)
    {
        if (string.Equals(settings.Monitor, deviceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.Monitor = deviceName;
        Relayout();
        SaveSettings();
        Log.Info($"棚を出す画面を変えました: {deviceName ?? "メインの画面"}（作業領域 {layout.WorkArea}、拡大率 {layout.Scale:0.##}）");
        ShowToast("この画面に棚を出すようにしました。取っ手はここです。", ToastKind.Info, TimeSpan.FromSeconds(4));
        RaiseSettingsChanged();
    }

    void ISettingsHost.CenterHandle()
    {
        settings.HandlePosition = 0.5;
        Relayout();
        SaveSettings();
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetOpenOnHover(bool on) => SetOpenOnHover(on, announce: false);

    void ISettingsHost.SetCloseAfterPaste(bool on)
    {
        settings.CloseAfterPaste = on;
        SaveSettings();
        Log.Info(on ? "貼り付けたら棚を閉じるようにしました。" : "貼り付けても棚を閉じないようにしました。");
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetTheme(string theme)
    {
        settings.Theme = theme;
        settings.Normalize();
        App.ApplyTheme(settings.Theme);
        SaveSettings();
        Log.Info($"色を変えました: {settings.Theme}");

        // WPF が標準の部品（Fluent）の色を入れ替え終わってから、棚のすりガラスをかけ直す
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!disposed)
            {
                shelf.ApplyBackdrop();
            }
        });
        RaiseSettingsChanged();
    }

    int ISettingsHost.CountOverLimit(int maxItems) => store.CountOverLimit(maxItems);

    void ISettingsHost.SetMaxItems(int maxItems)
    {
        settings.MaxItems = maxItems;
        settings.Normalize();
        int removed;
        try
        {
            removed = store.SetMaxItems(settings.MaxItems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("しまっておく数を変えたあとの片付けが、途中で止まりました。", ex);
            removed = 0;
        }

        SaveSettings();
        Log.Info($"しまっておく数を {settings.MaxItems} 件にしました（消したもの {removed} 件）。");
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetMoveToTop(bool on)
    {
        settings.MoveToTopOnPaste = on;
        paste.MoveToTop = on;
        SaveSettings();
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetPaused(bool paused) => SetPaused(paused);

    int ISettingsHost.CountClearable(ClearScope scope) => store.Count(scope);

    bool ISettingsHost.HotkeyWorks => hotkeyWorks;

    bool ISettingsHost.SetHotkey(string setting)
    {
        settings.OpenHotkey = setting;
        settings.Normalize();
        SaveSettings();
        var works = ApplyHotkey();
        RaiseSettingsChanged();
        return works;
    }

    int ISettingsHost.CountAutoDelete(int days, string scope) =>
        days <= 0 ? 0 : store.CountOlderThan(TimeSpan.FromDays(days), scope == "all" ? ClearScope.All : ClearScope.Screenshots);

    void ISettingsHost.SetAutoDelete(int days, string scope)
    {
        settings.AutoDeleteDays = days;
        settings.AutoDeleteScope = scope;
        settings.Normalize();
        int removed;
        try
        {
            removed = store.SetAutoDelete(settings.AutoDeleteAge(), settings.AutoDeleteClearScope());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("古いものを消している途中で止まりました。", ex);
            removed = 0;
        }

        SaveSettings();
        Log.Info(settings.AutoDeleteDays > 0
            ? $"{settings.AutoDeleteDays} 日たった {settings.AutoDeleteScope} を自動で消すようにしました（いま消したもの {removed} 件）。"
            : "古いものを自動で消さないようにしました。");
        RaiseSettingsChanged();
    }

    void ISettingsHost.SetSignalOnCapture(bool on)
    {
        settings.SignalOnCapture = on;
        SaveSettings();
        RaiseSettingsChanged();
    }

    void ISettingsHost.Clear(ClearScope scope) => Clear(scope);

    bool ISettingsHost.SetStartWithWindows(bool on)
    {
        if (startup is null)
        {
            demoStartWithWindows = on;
            Log.Info($"（見本）サインインしたら起動する: {on}");
            return true;
        }

        try
        {
            if (on)
            {
                startup.Enable(Environment.ProcessPath ?? throw new InvalidOperationException("この exe の場所がわかりません。"));
                Log.Info($"サインインしたら起動するようにしました（{startup.Command}）。");
            }
            else
            {
                startup.Disable();
                Log.Info("サインインしても起動しないようにしました。");
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            Log.Warn("サインインしたときの起動を変えられませんでした。", ex);
            ShowToast("サインインしたときの起動を変えられませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(6));
            return false;
        }
    }

    void ISettingsHost.OpenDataFolder() => OpenFolder(store.Root);

    void ISettingsHost.OpenLogFolder() => OpenFolder(Log.Folder ?? store.Root);

    void ISettingsHost.SettingsActivated()
    {
        if (isOpen && !keepOpen)
        {
            Close();
        }
    }

    private void RaiseSettingsChanged()
    {
        if (!disposed)
        {
            SettingsChanged?.Invoke();
        }
    }

    private static string EdgeName(ShelfEdge edge) => edge switch
    {
        ShelfEdge.Left => "左の端",
        ShelfEdge.Top => "上の端",
        ShelfEdge.Bottom => "下の端",
        _ => "右の端",
    };
}
