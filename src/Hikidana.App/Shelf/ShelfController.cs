using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hikidana.App.Interop;
using Hikidana.Core.Clips;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Layout;
using Hikidana.Core.Paste;
using Hikidana.Core.Settings;
using Hikidana.Core.Storage;
using Hikidana.Win32;
using Microsoft.Win32;

namespace Hikidana.App.Shelf;

/// <summary>
/// 取っ手・棚・知らせのウインドウをまとめて動かす。開く・閉じる、置き場所、貼り付け、設定の保存。
/// どれも画面のスレッドで呼ぶ。
/// </summary>
internal sealed partial class ShelfController : IDisposable
{
    private static readonly string[] NoTargetClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];

    private readonly ClipStore store;
    private readonly ClipboardWatcher watcher;
    private readonly PasteService paste;
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;
    private readonly StartupRegistration? startup;
    private readonly HandleWindow handle = new();
    private readonly ShelfWindow shelf = new();
    private readonly ToastWindow toast = new();
    private readonly ForegroundWatcher foreground = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer pruneTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly GlobalHotkey hotkey = new();
    private readonly Dispatcher dispatcher;
    private ShelfLayout layout = null!;
    private bool isOpen;
    private bool keepOpen;
    private bool dragging;
    private bool typing;
    private TargetWindow? lastExternal;
    private TrayIcon? tray;
    private EventHandler? openingFrame;
    private readonly DismissWatcher dismiss;
    private int refreshQueued;
    private bool relayoutQueued;
    private bool dialogOpen;
    private bool hotkeyWorks = true;
    private bool disposed;

    /// <param name="startup">サインインしたら起動する設定。見本で動かすときは null（本物の設定を変えない）。</param>
    public ShelfController(ClipStore store, ClipboardWatcher watcher, PasteService paste, AppSettings settings, SettingsStore settingsStore, StartupRegistration? startup)
    {
        this.store = store;
        this.watcher = watcher;
        this.paste = paste;
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.startup = startup;
        dispatcher = Dispatcher.CurrentDispatcher;

        // 棚の外のクリックと Esc で閉じる（棚が開いている間だけ見張る）
        dismiss = new DismissWatcher
        {
            IsBusy = () => dragging || dialogOpen || ShelfWindow.IsMenuOpen,
            TakesEscape = () => isOpen && !keepOpen,
        };
        dismiss.ClickedOutside += OnClickedOutside;
        dismiss.EscapePressed += () => Close(restoreFocus: true);

        handle.Clicked += Toggle;
        handle.HoverDwelled += Open;
        handle.OpenOnHover = settings.OpenOnHover;
        lastExternal = CurrentTarget();
        shelf.OpenOnHoverChanged += on => SetOpenOnHover(on);
        shelf.MouseEnter += (_, _) => leaveTimer.Stop();
        shelf.MouseLeave += (_, _) =>
        {
            if (settings.OpenOnHover && isOpen)
            {
                leaveTimer.Start();
            }
        };
        leaveTimer.Tick += (_, _) => CloseIfLeft();
        handle.Dragged += OnHandleDragged;
        handle.DragFinished += SaveSettings;

        // 表示倍率の違う画面へ動かすと、WPF がウインドウの大きさを変えてしまうので、置き直す
        handle.DpiChanged += (_, _) => QueueRelayout();
        shelf.DpiChanged += (_, _) => QueueRelayout();
        handle.Dropped += OnDropped;
        shelf.DragRequested += OnDragRequested;
        shelf.SaveRequested += OnSaveRequested;
        shelf.Dropped += OnDropped;
        shelf.PasteRequested += OnPasteRequested;
        shelf.CopyRequested += OnCopyRequested;
        shelf.PinRequested += item => store.SetPinned(item.Clip.Id, !item.Pinned);
        shelf.DeleteRequested += OnDeleteRequested;
        shelf.CloseRequested += () => Close(restoreFocus: true);
        shelf.TypingRequested += BeginTyping;
        shelf.KeepOpenChanged += keep => keepOpen = keep;
        shelf.PausedChanged += SetPaused;
        shelf.OpenDataRequested += () => OpenFolder(store.Root);
        shelf.OpenLogsRequested += () => OpenFolder(Log.Folder ?? store.Root);
        shelf.SettingsRequested += OpenSettings;
        shelf.ClearRequested += Clear;
        shelf.PasteManyRequested += OnPasteManyRequested;
        shelf.DeleteManyRequested += OnDeleteManyRequested;
        hotkey.Pressed += OnHotkey;
        shelf.QuitRequested += () => Application.Current.Shutdown();
    }

    public bool IsOpen => isOpen;

    /// <summary>見せ始める（取っ手を出す）。<paramref name="open"/> なら棚も開く。</summary>
    public void Start(bool open)
    {
        // 見せる前にウインドウを作って置き場所を決める（出たときに跳ねないように）
        WindowHelper.HandleOf(handle);
        WindowHelper.HandleOf(shelf);
        WindowHelper.HandleOf(toast);
        Relayout();
        Refresh();
        shelf.ShowPaused(settings.Paused);
        shelf.ShowKeepOpen(keepOpen);
        shelf.ShowOpenOnHover(settings.OpenOnHover);

        store.Changed += OnStoreChanged;
        foreground.Changed += OnForegroundChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        clock.Tick += (_, _) => UpdateTimes();
        clock.Start();
        pruneTimer.Tick += (_, _) => PruneOld();
        pruneTimer.Start();
        ApplyHotkey();
        Theme.Palette.Changed += OnPaletteChanged;

        tray = new TrayIcon(TrayTooltip());
        tray.Clicked += Toggle;
        tray.MenuItems = TrayMenu;
        tray.MenuItemSelected += OnTrayMenuItem;

        handle.Show();
        if (open)
        {
            Open();
        }
        else if (!settings.WelcomeShown)
        {
            // 初めてのとき: 取っ手の場所を知らせる
            settings.WelcomeShown = true;
            SaveSettings();
            ShowToast(WelcomeMessage(settings.Edge, hotkeyWorks ? hotkey.Current : null), ToastKind.Info, TimeSpan.FromSeconds(10));
        }
        else if (!hotkeyWorks)
        {
            ShowHotkeyTaken();
        }

        Log.Info($"棚を用意しました（{settings.Edge}、位置 {settings.HandlePosition:0.00}、作業領域 {layout.WorkArea}、拡大率 {layout.Scale:0.##}）");
    }

    public void Open()
    {
        if (isOpen || disposed)
        {
            return;
        }

        Relayout();
        UpdateTimes();
        shelf.ScrollToTop();
        toast.Hide();
        isOpen = true;
        AnimateOpening();
        handle.Hide();
        dismiss.Start();
        if (settings.OpenOnHover)
        {
            // 棚に入らずに離れていったときも閉じるよう、見張り始める
            leaveTimer.Start();
        }
    }

    /// <summary>
    /// 閉じる。<paramref name="restoreFocus"/> なら、検索で棚にキーボードを渡していたとき、元のアプリに戻す
    /// （ほかのアプリをクリックして閉じたときや、貼り付けで閉じたときは戻さない）。
    /// </summary>
    public void Close(bool restoreFocus = false)
    {
        if (!isOpen)
        {
            return;
        }

        var wasTyping = typing;
        typing = false;
        dismiss.Stop();
        leaveTimer.Stop();
        StopOpening();
        shelf.Hide();
        shelf.ResetView();
        handle.Show();
        isOpen = false;
        if (wasTyping && restoreFocus && lastExternal is { Exists: true } target)
        {
            WindowHelper.BringToFront(target.Handle);
        }
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close(restoreFocus: true);
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// 棚の外をクリックした（作業中のアプリの中でも）: 開いたままにしていなければ閉じる。
    /// クリックはそのままアプリへ届くので、入力欄をクリックすれば、閉じると同時にそこへ文字を打てる。
    /// </summary>
    private void OnClickedOutside()
    {
        if (isOpen && !keepOpen && !dragging)
        {
            Close();
        }
    }

    /// <summary>検索の欄をクリックした: 棚を前に出して、キーボードを受け取る（貼り付け先は、その前のアプリのまま）。</summary>
    private void BeginTyping()
    {
        if (CurrentTarget() is { } target)
        {
            lastExternal = target;
        }

        typing = true;
        shelf.BeginTyping();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        store.Changed -= OnStoreChanged;
        Theme.Palette.Changed -= OnPaletteChanged;
        StopOpening();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        clock.Stop();
        leaveTimer.Stop();
        pruneTimer.Stop();
        dismiss.Dispose();
        hotkey.Dispose();
        settingsWindow?.Close();
        tray?.Dispose();
        foreground.Dispose();
        toast.Close();
        shelf.Close();
        handle.Close();
    }

    /// <summary>画面と設定から、置き場所を決め直す。</summary>
    private void Relayout()
    {
        var monitor = MonitorInfo.Find(settings.Monitor);
        layout = ShelfLayout.Compute(settings.Edge, monitor.WorkArea, monitor.Scale, settings.HandlePosition);
        handle.Apply(layout);
        WindowHelper.Place(handle, layout.Handle);
        shelf.SetContentSize(layout.Panel.Width / layout.Scale, layout.Panel.Height / layout.Scale, layout.Edge);
        if (openingFrame is null)
        {
            // 開いている途中なら、動きの方で置く
            WindowHelper.Place(shelf, layout.Panel);
        }
    }

    /// <summary>開き始めの大きさ（最後の大きさに対して）。</summary>
    private const double OpeningStart = 0.35;

    /// <summary>開くときの長さ。</summary>
    private static readonly TimeSpan OpeningDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>端から滑り出てくるように開く（Windows の「アニメーション効果」が切ってあれば、すぐ開く）。</summary>
    private void AnimateOpening()
    {
        StopOpening();
        if (!SystemParameters.ClientAreaAnimation)
        {
            WindowHelper.Place(shelf, layout.Panel);
            shelf.Show();
            return;
        }

        WindowHelper.Place(shelf, layout.PanelOpening(OpeningStart));
        shelf.Show();
        var started = Stopwatch.GetTimestamp();
        openingFrame = (_, _) =>
        {
            var t = Math.Min(1, Stopwatch.GetElapsedTime(started) / OpeningDuration);
            var eased = 1 - Math.Pow(1 - t, 3);
            WindowHelper.Place(shelf, layout.PanelOpening(OpeningStart + ((1 - OpeningStart) * eased)));
            if (t >= 1)
            {
                StopOpening();
            }
        };
        System.Windows.Media.CompositionTarget.Rendering += openingFrame;
    }

    private void StopOpening()
    {
        if (openingFrame is not null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= openingFrame;
            openingFrame = null;
        }
    }

    /// <summary>あとで（いまの処理が終わってから）置き場所を決め直す。続けて呼ばれても 1 回だけ。</summary>
    private void QueueRelayout()
    {
        if (relayoutQueued || disposed)
        {
            return;
        }

        relayoutQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            relayoutQueued = false;
            if (!disposed)
            {
                Relayout();
            }
        });
    }

    private void OnHandleDragged(double position)
    {
        settings.HandlePosition = position;
        var monitor = MonitorInfo.Find(settings.Monitor);
        layout = ShelfLayout.Compute(settings.Edge, monitor.WorkArea, monitor.Scale, position);
        WindowHelper.Place(handle, layout.Handle);
        RaiseSettingsChanged();
    }

    private void SaveSettings()
    {
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("設定を保存できませんでした。", ex);
        }
    }

    /// <summary>「マウスを乗せるだけで開く」を入り切りする。<paramref name="announce"/> なら知らせも出す（設定の画面からのときは出さない）。</summary>
    private void SetOpenOnHover(bool on, bool announce = true)
    {
        settings.OpenOnHover = on;
        handle.OpenOnHover = on;
        shelf.ShowOpenOnHover(on);
        SaveSettings();
        Log.Info(on ? "取っ手にマウスを乗せるだけで開くようにしました。" : "取っ手をクリックしたときに開くようにしました。");
        if (announce)
        {
            ShowToast(
                on ? "取っ手にマウスを乗せるだけで、棚が開くようにしました。棚からマウスを離すと閉じます。" : "取っ手をクリックしたときに、棚が開くようにしました。",
                ToastKind.Info,
                TimeSpan.FromSeconds(5));
        }

        RaiseSettingsChanged();
    }

    /// <summary>
    /// マウスを乗せるだけで開くときは、棚からマウスが離れたら閉じる。
    /// ただし、メニューを開いている間、検索で打っている間、ドラッグの間、「開いたままにする」のときは閉じない。
    /// </summary>
    private void CloseIfLeft()
    {
        leaveTimer.Stop();
        if (!isOpen || !settings.OpenOnHover || keepOpen || typing || dragging || ShelfWindow.IsMenuOpen)
        {
            return;
        }

        var (x, y) = WindowHelper.CursorPosition();
        if (layout.Panel.Contains(x, y))
        {
            return;
        }

        if (layout.PanelReach.Contains(x, y))
        {
            // 棚と画面の端のすきま（取っ手のあたり）にいる: まだ閉じずに、少ししてからまた見る
            leaveTimer.Start();
            return;
        }

        Close();
    }

    private void SetPaused(bool paused)
    {
        settings.Paused = paused;
        watcher.Paused = paused;
        shelf.ShowPaused(paused);
        tray?.SetTooltip(TrayTooltip());
        SaveSettings();
        Log.Info(paused ? "記録を止めました。" : "記録を再開しました。");
        RaiseSettingsChanged();
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        // 続けて変わることが多いので、まとめて 1 回だけ描き直す
        if (Interlocked.Exchange(ref refreshQueued, 1) == 0)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
        }
    }

    /// <summary>しまってあるものに合わせて、カードを並べ直す（変わっていないカードは、そのまま使う）。</summary>
    private void Refresh()
    {
        Interlocked.Exchange(ref refreshQueued, 0);
        if (disposed)
        {
            return;
        }

        // 新しい順のまま（いちばん上が、いま撮ったスクリーンショットやコピーしたもの）。ピン留めは印が付くだけ
        var ordered = store.Items;
        var now = DateTimeOffset.Now;
        var existing = new Dictionary<string, ClipItem>(StringComparer.Ordinal);
        foreach (var item in shelf.Items)
        {
            existing.TryAdd(item.Clip.Id, item);
        }

        var desired = new List<ClipItem>(ordered.Count);
        foreach (var clip in ordered)
        {
            if (existing.TryGetValue(clip.Id, out var old) && old.Matches(clip))
            {
                desired.Add(old);
            }
            else
            {
                var item = new ClipItem(clip, now);
                LoadThumbnail(item);
                desired.Add(item);
            }
        }

        var items = shelf.Items;
        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            if (i < items.Count && ReferenceEquals(items[i], want))
            {
                continue;
            }

            var at = -1;
            for (var j = i + 1; j < items.Count; j++)
            {
                if (ReferenceEquals(items[j], want))
                {
                    at = j;
                    break;
                }
            }

            if (at >= 0)
            {
                items.Move(at, i);
            }
            else
            {
                items.Insert(i, want);
            }
        }

        while (items.Count > desired.Count)
        {
            items.RemoveAt(items.Count - 1);
        }

        shelf.UpdateCounts();
    }

    private void UpdateTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var item in shelf.Items)
        {
            item.UpdateTime(now);
        }
    }

    /// <summary>縮小画像を、画面のスレッドの外で読み込む。</summary>
    private void LoadThumbnail(ClipItem item)
    {
        if (!item.IsImage || (store.ThumbnailPath(item.Clip) ?? store.ImagePath(item.Clip)) is not { } path)
        {
            return;
        }

        var width = (int)Math.Ceiling(item.ImageDisplayWidth * (layout?.Scale ?? 1));
        _ = Task.Run<BitmapImage?>(() =>
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path);
                if (width > 0 && width < item.Clip.ImageWidth)
                {
                    image.DecodePixelWidth = width;
                }

                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                Log.Warn($"縮小画像を読めませんでした（{item.Clip.Id}）。", ex);
                return null;
            }
        }).ContinueWith(
            task =>
            {
                if (task.Result is { } image)
                {
                    item.Thumbnail = image;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// 貼り付け先のウインドウ。ふだんは、いま前面にあるもの（棚はクリックしても前面にならない）。
    /// 検索で棚が前面にいるときは、その前に前面にあったもの。タスクバーなどなら null。
    /// </summary>
    private TargetWindow? CurrentTarget()
    {
        var target = TargetWindow.Foreground();
        if (target is null || target.ProcessId == (uint)Environment.ProcessId || NoTargetClasses.Contains(target.ClassName))
        {
            // 棚が前にいる（検索中）か、タスクバーをクリックしたあと（タスクトレイから開いたなど）: その前のアプリへ
            return lastExternal is { Exists: true } previous ? previous : null;
        }

        return target;
    }

    private async void OnPasteRequested(ClipItem item, bool alternate)
    {
        var target = CurrentTarget();
        var mode = alternate ? (item.IsImage ? PasteMode.AsFile : PasteMode.PlainText) : PasteMode.Normal;
        if (settings.CloseAfterPaste && !keepOpen)
        {
            Close();
        }

        try
        {
            var result = await paste.PasteAsync(item.Clip, target, mode);
            Log.Info($"貼り付け: {item.Clip.Kind}、{mode}、先 {target?.ToString() ?? "なし"}、結果 {result.Outcome}{(result.Detail is null ? string.Empty : $"（{result.Detail}）")}");
            ShowResult(result);
        }
        catch (Exception ex)
        {
            Log.Error("貼り付けの途中で例外", ex);
            ShowToast("貼り付けられませんでした。もう一度お試しください。", ToastKind.Critical, TimeSpan.FromSeconds(6));
        }
    }

    private async void OnCopyRequested(ClipItem item, bool alternate)
    {
        var mode = alternate ? (item.IsImage ? PasteMode.AsFile : PasteMode.PlainText) : PasteMode.Normal;
        try
        {
            var result = await paste.CopyAsync(item.Clip, mode);
            Log.Info($"コピー: {item.Clip.Kind}、{mode}、結果 {result.Outcome}");
            ShowResult(result);
        }
        catch (Exception ex)
        {
            Log.Error("コピーの途中で例外", ex);
            ShowToast("コピーできませんでした。もう一度お試しください。", ToastKind.Critical, TimeSpan.FromSeconds(6));
        }
    }

    /// <summary>
    /// まとめて消す（ピン留めは残す）。消したあと、知らせの「元に戻す」で戻せる。
    /// Windows の Win+V の「すべてクリア」と同じく、確かめずにすぐ消す（そのかわり戻せる）。
    /// </summary>
    private void Clear(ClearScope scope)
    {
        IReadOnlyList<Clip> removed;
        try
        {
            removed = store.DeleteMany(scope);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("まとめて消せませんでした。", ex);
            ShowToast("消せませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(6));
            return;
        }

        if (removed.Count == 0)
        {
            ShowToast(scope == ClearScope.Screenshots ? "消せるスクリーンショットはありません。" : "消せるものはありません。", ToastKind.Info, TimeSpan.FromSeconds(3));
            return;
        }

        Log.Info($"まとめて消しました: {scope}、{removed.Count} 件");
        var message = scope == ClearScope.Screenshots
            ? $"ピン留め以外のスクリーンショットを {removed.Count} 件消しました。"
            : $"ピン留め以外の {removed.Count} 件を消しました。";
        ShowToast(message, ToastKind.Info, TimeSpan.FromSeconds(8), "元に戻す", () => RestoreRemoved(removed));
    }

    private void OnDeleteRequested(ClipItem item)
    {
        var removed = store.Delete(item.Clip.Id);
        if (removed is not null)
        {
            ShowToast("消しました。", ToastKind.Info, TimeSpan.FromSeconds(6), "元に戻す", () => store.Restore(removed));
        }
    }

    private void ShowResult(PasteResult result)
    {
        var (kind, seconds) = result.Outcome switch
        {
            PasteOutcome.Pasted => (ToastKind.Success, 2.2),
            PasteOutcome.CopiedOnly when result.TargetName is not null => (ToastKind.Caution, 6.0),
            PasteOutcome.CopiedOnly => (ToastKind.Info, 3.5),
            _ => (ToastKind.Critical, 7.0),
        };
        ShowToast(result.Message, kind, TimeSpan.FromSeconds(seconds));
    }

    private void ShowToast(string message, ToastKind kind, TimeSpan duration, string? actionText = null, Action? action = null)
    {
        if (disposed)
        {
            return;
        }

        toast.Present(message, kind, layout, isOpen, duration, actionText, action);
    }

    private static string WelcomeMessage(ShelfEdge edge, Hotkey? shortcut)
    {
        var where = edge switch
        {
            ShelfEdge.Left => "左端",
            ShelfEdge.Top => "上端",
            ShelfEdge.Bottom => "下端",
            _ => "右端",
        };
        var keys = shortcut is { } key ? $"{key} でも開けます。" : string.Empty;
        return $"Hikidana が動いています。画面の{where}の細い線（取っ手）をクリックすると、コピーしたものやスクリーンショットの棚が開きます。{keys}";
    }

    /// <summary>設定のショートカットを登録し直す。ほかのアプリが使っていて登録できなければ false。</summary>
    private bool ApplyHotkey()
    {
        var key = Hotkey.Parse(settings.OpenHotkey);
        hotkeyWorks = hotkey.Register(key);
        if (!hotkeyWorks)
        {
            Log.Warn($"ショートカット {key} を登録できませんでした（ほかのアプリが使っているようです）。");
        }
        else
        {
            Log.Info(key is { } k ? $"ショートカット: {k}" : "ショートカットは使いません。");
        }

        return hotkeyWorks;
    }

    private void ShowHotkeyTaken() =>
        ShowToast($"ショートカット {Hotkey.Parse(settings.OpenHotkey)} は、ほかのアプリが使っているため使えません。設定の画面で、別のキーを選べます。", ToastKind.Caution, TimeSpan.FromSeconds(8));

    /// <summary>
    /// ショートカットが押された: いま使っているアプリを貼り付け先として覚えてから、棚を開いてキーボードを渡す
    /// （文字で探して ↑↓ で選び、Enter で貼り付け、Esc で閉じて元のアプリに戻る）。キーボードで使っている途中なら閉じる。
    /// </summary>
    private void OnHotkey()
    {
        if (disposed)
        {
            return;
        }

        if (isOpen && typing)
        {
            Close(restoreFocus: true);
            return;
        }

        if (CurrentTarget() is { } target)
        {
            lastExternal = target;
        }

        if (!isOpen)
        {
            Open();
        }

        BeginTyping();
        Log.Info("ショートカットで棚を開きました。");
    }

    /// <summary>コピーやスクリーンショットを棚にしまった（どのスレッドからでも呼べる）。棚が閉じていれば、取っ手で知らせる。</summary>
    public void NotifyRecorded(Clip clip)
    {
        dispatcher.BeginInvoke(() =>
        {
            if (!disposed && !isOpen && settings.SignalOnCapture)
            {
                handle.Flash(ClipItem.GlyphFor(clip.Kind));
            }
        });
    }

    /// <summary>自動で片付ける設定なら、期間を過ぎたものを消す（ときどき呼ぶ。画面のスレッドの外で消す）。</summary>
    private void PruneOld()
    {
        if (settings.AutoDeleteDays <= 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var removed = store.Prune();
                if (removed > 0)
                {
                    Log.Info($"古いものを {removed} 件、自動で消しました。");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("古いものを自動で消せませんでした。", ex);
            }
        });
    }

    /// <summary>選んだものを、選んだ順に続けて貼り付ける（1 つずつクリップボードに置いて Ctrl+V）。</summary>
    private async void OnPasteManyRequested(IReadOnlyList<ClipItem> items)
    {
        var target = CurrentTarget();
        var clips = items.Select(i => i.Clip).ToList();
        shelf.ClearPicks();
        if (settings.CloseAfterPaste && !keepOpen)
        {
            Close();
        }

        if (clips.Count > 1)
        {
            ShowToast($"{clips.Count} 件を続けて貼り付けています…", ToastKind.Info, TimeSpan.FromSeconds(30));
        }

        var pasted = 0;
        Clip? previous = null;
        try
        {
            foreach (var clip in clips)
            {
                if (previous is not null)
                {
                    // 貼り付け先が前のものを読み終えるのを待ってから、クリップボードを入れ替える（画像は読むのに時間がかかる）
                    await Task.Delay(previous.IsImage || clip.IsImage ? MultiPasteImageGap : MultiPasteTextGap);
                }

                var result = await paste.PasteAsync(clip, target, PasteMode.Normal);
                if (result.Outcome != PasteOutcome.Pasted)
                {
                    Log.Info($"まとめて貼り付け: {pasted} 件目のあとで止まりました（{result.Outcome}）。");
                    ShowResult(result);
                    return;
                }

                pasted++;
                previous = clip;
            }

            Log.Info($"まとめて貼り付け: {pasted} 件、先 {target?.ToString() ?? "なし"}");
            ShowToast($"{pasted} 件を続けて貼り付けました。", ToastKind.Success, TimeSpan.FromSeconds(2.5));
        }
        catch (Exception ex)
        {
            Log.Error("まとめて貼り付けている途中で例外", ex);
            ShowToast($"{pasted} 件を貼り付けたところで、止まりました。", ToastKind.Critical, TimeSpan.FromSeconds(6));
        }
    }

    /// <summary>まとめて貼るとき、画像のあとで空ける時間（貼り付け先が画像を読み終えるまで）。</summary>
    private static readonly TimeSpan MultiPasteImageGap = TimeSpan.FromMilliseconds(900);

    /// <summary>まとめて貼るとき、テキストのあとで空ける時間。</summary>
    private static readonly TimeSpan MultiPasteTextGap = TimeSpan.FromMilliseconds(350);

    /// <summary>選んだものを、まとめて消す（知らせの「元に戻す」で戻せる）。</summary>
    private void OnDeleteManyRequested(IReadOnlyList<ClipItem> items)
    {
        IReadOnlyList<Clip> removed;
        try
        {
            removed = store.DeleteMany(items.Select(i => i.Clip.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("選んだものを消せませんでした。", ex);
            ShowToast("消せませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(6));
            return;
        }

        if (removed.Count > 0)
        {
            Log.Info($"選んだものを {removed.Count} 件消しました。");
            ShowToast($"選んだ {removed.Count} 件を消しました。", ToastKind.Info, TimeSpan.FromSeconds(8), "元に戻す", () => RestoreRemoved(removed));
        }
    }

    /// <summary>まとめて消したものを元に戻す（知らせの「元に戻す」）。</summary>
    private void RestoreRemoved(IReadOnlyList<Clip> removed)
    {
        try
        {
            var restored = store.RestoreMany(removed);
            Log.Info($"消したものを戻しました: {restored} / {removed.Count} 件");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("元に戻せませんでした。", ex);
            ShowToast("元に戻せませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(6));
        }
    }

    private void OnForegroundChanged(nint window)
    {
        // 貼り付け先の候補として覚えておく（検索で棚が前に出たあとも、ここへ貼る）
        if (TargetWindow.From(window) is { } target && target.ProcessId != (uint)Environment.ProcessId && !NoTargetClasses.Contains(target.ClassName))
        {
            lastExternal = target;
        }

        // ほかのアプリやデスクトップをクリックしたら閉じる（同じアプリの中をクリックしただけなら閉じない）。
        // カードをドラッグしている間は、落とす先のアプリが前に出ても閉じない。
        // タスクバーのときも閉じない（タスクトレイのアイコンから開いたときに、すぐ閉じてしまわないように）
        if (isOpen && !keepOpen && !dragging && !(TargetWindow.From(window) is { } now && NoTargetClasses.Contains(now.ClassName)))
        {
            Close();
        }
    }

    /// <summary>カードをほかのアプリへドラッグする（落とし終わるまで戻らない）。</summary>
    private void OnDragRequested(ClipItem item, FrameworkElement source)
    {
        DragPlan plan;
        try
        {
            plan = DragPlanner.Plan(store, item.Clip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("ドラッグの準備ができませんでした。", ex);
            ShowToast("ドラッグの準備ができませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(5));
            return;
        }

        if (plan.IsEmpty)
        {
            ShowToast(plan.Note ?? "渡せるものがありません。", ToastKind.Caution, TimeSpan.FromSeconds(5));
            return;
        }

        DragDropEffects effect;
        dragging = true;
        try
        {
            effect = DragDrop.DoDragDrop(source, DragData.Create(plan), DragData.Effects(plan));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warn("ドラッグの途中で失敗しました。", ex);
            effect = DragDropEffects.None;
        }
        finally
        {
            dragging = false;
        }

        Log.Info($"ドラッグ: {item.Clip.Kind}、結果 {effect}");
        if (effect == DragDropEffects.None)
        {
            return;
        }

        if (settings.MoveToTopOnPaste)
        {
            try
            {
                store.Touch(item.Clip.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("並べ替えを保存できませんでした。", ex);
            }
        }

        if (settings.CloseAfterPaste && !keepOpen)
        {
            Close();
        }

        if (plan.Note is { } note)
        {
            ShowToast(note, ToastKind.Caution, TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>右クリックの「名前を付けて保存」。画像は PNG、そのほかはテキストのファイルにする。</summary>
    private void OnSaveRequested(ClipItem item)
    {
        var clip = item.Clip;
        var stamp = clip.CreatedAt.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var dialog = new SaveFileDialog
        {
            Title = "名前を付けて保存",
            FileName = clip.IsImage ? $"{(clip.Kind == ClipKind.Screenshot ? "スクリーンショット" : "画像")} {stamp}.png" : $"{ClipLabels.KindName(clip.Kind)} {stamp}.txt",
            Filter = clip.IsImage ? "PNG 画像 (*.png)|*.png" : "テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            DefaultExt = clip.IsImage ? ".png" : ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(clip.IsImage ? Environment.SpecialFolder.MyPictures : Environment.SpecialFolder.MyDocuments),
        };
        bool? chosen;
        dialogOpen = true;
        try
        {
            chosen = dialog.ShowDialog(shelf.IsVisible ? shelf : null);
        }
        finally
        {
            dialogOpen = false;
        }

        if (chosen != true)
        {
            return;
        }

        var path = dialog.FileName;
        try
        {
            if (clip.IsImage)
            {
                File.Copy(store.ImagePath(clip) ?? throw new FileNotFoundException("画像のファイルが見つかりません。"), path, overwrite: true);
            }
            else
            {
                File.WriteAllText(path, store.ReadText(clip) ?? clip.Preview, new System.Text.UTF8Encoding(false));
            }

            Log.Info($"保存: {clip.Kind}");
            ShowToast("保存しました。", ToastKind.Success, TimeSpan.FromSeconds(6), "フォルダーを開く", () => OpenFolderSelecting(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("保存できませんでした。", ex);
            ShowToast($"保存できませんでした。{ex.Message}", ToastKind.Critical, TimeSpan.FromSeconds(7));
        }
    }

    private static void OpenFolderSelecting(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("フォルダーを開けませんでした。", ex);
        }
    }

    /// <summary>ほかのアプリから取っ手や棚に落とされたものを、しまう。</summary>
    private void OnDropped(IDataObject data)
    {
        // 落とされたものは、落とされた瞬間にしか読めないので、まず読む。見分けてしまうのは、画面のスレッドの外で
        Hikidana.Core.Clipboard.ClipboardSnapshot? snapshot;
        try
        {
            snapshot = DropReader.Read(data);
        }
        catch (Exception ex)
        {
            Log.Warn("落とされたものを読めませんでした。", ex);
            snapshot = null;
        }

        if (snapshot is null)
        {
            ShowToast("しまえるものが見つかりませんでした。", ToastKind.Caution, TimeSpan.FromSeconds(4));
            return;
        }

        _ = Task.Run(() =>
        {
            var draft = ClipClassifier.Classify(snapshot!);
            return draft is null ? null : store.Add(draft);
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    Log.Warn("落とされたものを、しまえませんでした。", task.Exception);
                    ShowToast("しまえませんでした。", ToastKind.Critical, TimeSpan.FromSeconds(5));
                }
                else if (task.Result is { } clip)
                {
                    Log.Info($"ドロップでしまいました: {clip.Kind}");
                    ShowToast("棚にしまいました。", ToastKind.Success, TimeSpan.FromSeconds(2.2));
                }
                else
                {
                    ShowToast("しまえるものが見つかりませんでした。", ToastKind.Caution, TimeSpan.FromSeconds(4));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnPaletteChanged() => shelf.ApplyBackdrop();

    private string TrayTooltip() => settings.Paused ? "Hikidana（記録を止めています）" : "Hikidana";

    private IReadOnlyList<TrayMenuItem> TrayMenu() =>
    [
        new(1, isOpen ? "棚を閉じる" : "棚を開く", IsDefault: true),
        new(7, "設定"),
        TrayMenuItem.Separator,
        new(2, "記録を止める", Checked: settings.Paused),
        new(6, "マウスを乗せるだけで開く", Checked: settings.OpenOnHover),
        TrayMenuItem.Separator,
        new(3, "しまってある場所を開く"),
        new(4, "記録（ログ）の場所を開く"),
        TrayMenuItem.Separator,
        new(5, "Hikidana を終了"),
    ];

    private void OnTrayMenuItem(int id)
    {
        switch (id)
        {
            case 1:
                Toggle();
                break;
            case 2:
                SetPaused(!settings.Paused);
                break;
            case 3:
                OpenFolder(store.Root);
                break;
            case 4:
                OpenFolder(Log.Folder ?? store.Root);
                break;
            case 5:
                Application.Current.Shutdown();
                break;
            case 6:
                SetOpenOnHover(!settings.OpenOnHover);
                break;
            case 7:
                OpenSettings();
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(RelayoutAndAnnounce);

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            dispatcher.BeginInvoke(RelayoutAndAnnounce);
        }
    }

    /// <summary>画面の並びや作業領域が変わった: 置き直して、設定の画面の図も合わせ直す。</summary>
    private void RelayoutAndAnnounce()
    {
        if (disposed)
        {
            return;
        }

        Relayout();
        RaiseSettingsChanged();
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"フォルダーを開けませんでした: {folder}", ex);
        }
    }
}
