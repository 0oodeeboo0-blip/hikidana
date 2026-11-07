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
internal sealed class ShelfController : IDisposable
{
    private static readonly string[] NoTargetClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];

    private readonly ClipStore store;
    private readonly ClipboardWatcher watcher;
    private readonly PasteService paste;
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;
    private readonly HandleWindow handle = new();
    private readonly ShelfWindow shelf = new();
    private readonly ToastWindow toast = new();
    private readonly ForegroundWatcher foreground = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Dispatcher dispatcher;
    private ShelfLayout layout = null!;
    private bool isOpen;
    private bool keepOpen;
    private bool dragging;
    private bool typing;
    private TargetWindow? lastExternal;
    private TrayIcon? tray;
    private int refreshQueued;
    private bool disposed;

    public ShelfController(ClipStore store, ClipboardWatcher watcher, PasteService paste, AppSettings settings, SettingsStore settingsStore)
    {
        this.store = store;
        this.watcher = watcher;
        this.paste = paste;
        this.settings = settings;
        this.settingsStore = settingsStore;
        dispatcher = Dispatcher.CurrentDispatcher;

        handle.Clicked += Toggle;
        lastExternal = CurrentTarget();
        handle.Dragged += OnHandleDragged;
        handle.DragFinished += SaveSettings;
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

        store.Changed += OnStoreChanged;
        foreground.Changed += OnForegroundChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        clock.Tick += (_, _) => UpdateTimes();
        clock.Start();

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
            ShowToast(WelcomeMessage(settings.Edge), ToastKind.Info, TimeSpan.FromSeconds(10));
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
        shelf.Show();
        handle.Hide();
        isOpen = true;
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
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        clock.Stop();
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
        WindowHelper.Place(shelf, layout.Panel);
    }

    private void OnHandleDragged(double position)
    {
        settings.HandlePosition = position;
        var monitor = MonitorInfo.Find(settings.Monitor);
        layout = ShelfLayout.Compute(settings.Edge, monitor.WorkArea, monitor.Scale, position);
        WindowHelper.Place(handle, layout.Handle);
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

    private void SetPaused(bool paused)
    {
        settings.Paused = paused;
        watcher.Paused = paused;
        shelf.ShowPaused(paused);
        tray?.SetTooltip(TrayTooltip());
        SaveSettings();
        Log.Info(paused ? "記録を止めました。" : "記録を再開しました。");
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

    private static string WelcomeMessage(ShelfEdge edge)
    {
        var where = edge switch
        {
            ShelfEdge.Left => "左端",
            ShelfEdge.Top => "上端",
            ShelfEdge.Bottom => "下端",
            _ => "右端",
        };
        return $"Hikidana が動いています。画面の{where}の細い線（取っ手）をクリックすると、コピーしたものやスクリーンショットの棚が開きます。";
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
        if (dialog.ShowDialog(shelf.IsVisible ? shelf : null) != true)
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

    private string TrayTooltip() => settings.Paused ? "Hikidana（記録を止めています）" : "Hikidana";

    private IReadOnlyList<TrayMenuItem> TrayMenu() =>
    [
        new(1, isOpen ? "棚を閉じる" : "棚を開く", IsDefault: true),
        new(2, "記録を止める", Checked: settings.Paused),
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
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(Relayout);

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            dispatcher.BeginInvoke(Relayout);
        }
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
