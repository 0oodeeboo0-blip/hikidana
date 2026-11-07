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
        handle.Dragged += OnHandleDragged;
        handle.DragFinished += SaveSettings;
        handle.Dropped += OnDropped;
        shelf.DragRequested += OnDragRequested;
        shelf.Dropped += OnDropped;
        shelf.PasteRequested += OnPasteRequested;
        shelf.CopyRequested += OnCopyRequested;
        shelf.PinRequested += item => store.SetPinned(item.Clip.Id, !item.Pinned);
        shelf.DeleteRequested += OnDeleteRequested;
        shelf.CloseRequested += Close;
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

    public void Close()
    {
        if (!isOpen)
        {
            return;
        }

        shelf.Hide();
        handle.Show();
        isOpen = false;
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
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

        shelf.ShowCount(ordered.Count);
        shelf.ShowEmpty(ordered.Count == 0);
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

    /// <summary>いま前面にある、貼り付け先のウインドウ。タスクバーや自分のウインドウなら null。</summary>
    private static TargetWindow? CurrentTarget()
    {
        var target = TargetWindow.Foreground();
        if (target is null || target.ProcessId == (uint)Environment.ProcessId || NoTargetClasses.Contains(target.ClassName))
        {
            return null;
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
        // ほかのアプリやデスクトップをクリックしたら閉じる（同じアプリの中をクリックしただけなら閉じない）。
        // カードをドラッグしている間は、落とす先のアプリが前に出ても閉じない
        if (isOpen && !keepOpen && !dragging)
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
