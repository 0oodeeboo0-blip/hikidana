using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hikidana.App.Interop;
using Hikidana.Core.Layout;
using Hikidana.Core.Storage;

namespace Hikidana.App.Settings;

/// <summary>
/// 設定の画面。見せることと、変えられたことを <see cref="ISettingsHost"/> に伝えることだけをする。
/// 変えたらすぐ棚に反映されて保存される。棚の側で変わったとき（取っ手のドラッグなど）は、見え方を合わせ直す。
/// </summary>
internal partial class SettingsWindow : Window
{
    /// <summary>しまっておく数の選び方（設定のファイルで、ほかの数にしてあれば足す）。</summary>
    private static readonly int[] MaxItemsChoices = [100, 300, 500, 1000, 2000];

    /// <summary>ショートカットの選び方（設定の書き方。空は「使わない」）。</summary>
    private static readonly string[] HotkeyChoices = ["win+alt+v", "ctrl+alt+v", "alt+shift+v", "ctrl+alt+h", ""];

    /// <summary>画面の図のいちばん大きな幅と高さ（DIP）。</summary>
    private const double DiagramMaxWidth = 240;
    private const double DiagramMaxHeight = 150;

    private readonly ISettingsHost host;
    private bool loading;
    private bool reloadQueued;
    private ShelfEdge? hoverEdge;
    private int shownMaxItems;

    public SettingsWindow(ISettingsHost host)
    {
        this.host = host;
        InitializeComponent();

        // 小さな画面では、作業領域に収まる高さにする（中身はスクロールできる）
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height - 32));
        AppIcon.Source = LoadIcon(32);
        VersionText.Text = $"版 {VersionLabel(App.Version)}";

        host.SettingsChanged += OnSettingsChanged;
        Theme.Palette.Changed += OnPaletteChanged;
        Closed += (_, _) =>
        {
            host.SettingsChanged -= OnSettingsChanged;
            Theme.Palette.Changed -= OnPaletteChanged;
        };
        Activated += (_, _) => host.SettingsActivated();
        Load();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyFrame();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        AppIcon.Source = LoadIcon(32);
    }

    /// <summary>設定に合わせて、見え方を全部決め直す（変えても伝えない）。</summary>
    private void Load()
    {
        loading = true;
        try
        {
            var settings = host.Settings;
            EdgeChip(settings.Edge).IsChecked = true;

            var monitors = host.Monitors;
            MonitorCard.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            MonitorBox.Items.Clear();
            var current = host.Monitor;
            foreach (var monitor in monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y))
            {
                var item = new ComboBoxItem { Content = MonitorLabel(monitor), Tag = monitor };
                MonitorBox.Items.Add(item);
                if (string.Equals(monitor.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    MonitorBox.SelectedItem = item;
                }
            }

            CenterHandleButton.IsEnabled = Math.Abs(settings.HandlePosition - 0.5) > 0.001;
            OpenByHover.IsChecked = settings.OpenOnHover;
            OpenByClick.IsChecked = !settings.OpenOnHover;
            CloseAfterPasteSwitch.IsChecked = settings.CloseAfterPaste;
            ThemeBox.SelectedIndex = settings.Theme switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0,
            };

            if (MaxItemsBox.Items.Count == 0 || shownMaxItems != settings.MaxItems)
            {
                MaxItemsBox.Items.Clear();
                foreach (var count in MaxItemsChoices.Append(settings.MaxItems).Distinct().Order())
                {
                    MaxItemsBox.Items.Add(new ComboBoxItem { Content = $"{count:#,0} 件", Tag = count });
                }
            }

            SelectMaxItems(settings.MaxItems);
            MoveToTopSwitch.IsChecked = settings.MoveToTopOnPaste;
            PausedSwitch.IsChecked = settings.Paused;
            StartupSwitch.IsChecked = host.StartWithWindows;
            SignalSwitch.IsChecked = settings.SignalOnCapture;

            HotkeyBox.Items.Clear();
            foreach (var choice in HotkeyChoices.Contains(settings.OpenHotkey) ? HotkeyChoices : HotkeyChoices.Prepend(settings.OpenHotkey))
            {
                var label = choice.Length == 0 ? "使わない" : Hikidana.Core.Settings.Hotkey.Parse(choice)?.ToString() ?? choice;
                var item = new ComboBoxItem { Content = label, Tag = choice };
                HotkeyBox.Items.Add(item);
                if (choice == settings.OpenHotkey)
                {
                    HotkeyBox.SelectedItem = item;
                }
            }

            HotkeyWarning.Visibility = host.HotkeyWorks ? Visibility.Collapsed : Visibility.Visible;
            UpdateSwitch.IsChecked = settings.CheckForUpdates;
            UpdateStatusText.Text = host.UpdateStatus;
            InstallUpdateButton.Visibility = host.AvailableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
            if (host.AvailableUpdate is { } release)
            {
                InstallUpdateButton.Content = $"新しい版 {release.DisplayVersion} に入れ替える";
            }
            SelectAutoDelete(settings.AutoDeleteDays, settings.AutoDeleteScope);
        }
        finally
        {
            loading = false;
        }

        DrawDiagram();
    }

    /// <summary>自動で消す設定を、一覧で選んだ状態にする（一覧に無い日数なら足す）。</summary>
    private void SelectAutoDelete(int days, string scope)
    {
        var found = false;
        foreach (ComboBoxItem item in AutoDeleteDaysBox.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var value) && value == days)
            {
                AutoDeleteDaysBox.SelectedItem = item;
                found = true;
            }
        }

        if (!found)
        {
            var item = new ComboBoxItem { Content = $"{days} 日たったら", Tag = days.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            AutoDeleteDaysBox.Items.Add(item);
            AutoDeleteDaysBox.SelectedItem = item;
        }

        AutoDeleteScopeBox.SelectedIndex = scope == "all" ? 1 : 0;
        AutoDeleteScopeBox.IsEnabled = days > 0;
    }

    private (int Days, string Scope) ChosenAutoDelete() => (
        AutoDeleteDaysBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var days) ? days : 0,
        AutoDeleteScopeBox.SelectedItem is ComboBoxItem { Tag: string scope } ? scope : "screenshots");

    private void SelectMaxItems(int count)
    {
        shownMaxItems = count;
        foreach (ComboBoxItem item in MaxItemsBox.Items)
        {
            if (item.Tag is int value && value == count)
            {
                MaxItemsBox.SelectedItem = item;
            }
        }
    }

    private RadioButton EdgeChip(ShelfEdge edge) => edge switch
    {
        ShelfEdge.Left => EdgeLeft,
        ShelfEdge.Top => EdgeTop,
        ShelfEdge.Bottom => EdgeBottom,
        _ => EdgeRight,
    };

    /// <summary>「画面 2（2560 × 1440、メイン）」のような名前。番号は Windows の画面の名前（\\.\DISPLAY2）から。</summary>
    private static string MonitorLabel(MonitorInfo monitor)
    {
        var digits = new string(monitor.DeviceName.Where(char.IsAsciiDigit).ToArray());
        var name = digits.Length > 0 ? $"画面 {digits}" : "画面";
        var main = monitor.IsPrimary ? "、メイン" : string.Empty;
        return $"{name}（{monitor.Bounds.Width} × {monitor.Bounds.Height}{main}）";
    }

    /// <summary>「0.1.0（abc1234）」のような版の名前。</summary>
    private static string VersionLabel(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return version;
        }

        var commit = version[(plus + 1)..];
        return $"{version[..plus]}（{commit[..Math.Min(7, commit.Length)]}）";
    }

    /// <summary>アプリの記号を、いまの表示倍率に合う大きさの絵で読む（.ico には 16〜256 画素の絵が入っている）。</summary>
    private ImageSource? LoadIcon(double size)
    {
        try
        {
            var wanted = size * VisualTreeHelper.GetDpi(this).DpiScaleX;
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/Hikidana.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frames = decoder.Frames.OrderBy(f => f.PixelWidth).ToList();
            return frames.FirstOrDefault(f => f.PixelWidth >= wanted) ?? frames.LastOrDefault();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            Hikidana.Core.Diagnostics.Log.Warn("アプリの記号を読めませんでした。", ex);
            return null;
        }
    }

    /// <summary>Windows 11 なら背景を Mica にし、題名の帯をライト・ダークに合わせる。</summary>
    private void ApplyFrame()
    {
        Backdrop.SetDarkFrame(this, Theme.Palette.IsDark);
        if (Backdrop.ApplyMica(this))
        {
            Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "Hkd.SettingsBackground");
        }
    }

    private void OnPaletteChanged()
    {
        ApplyFrame();
        DrawDiagram();
    }

    /// <summary>
    /// 棚の側で設定が変わった。取っ手のドラッグ中などは続けて来るので、手が空いたときに 1 回だけ合わせ直す。
    /// </summary>
    private void OnSettingsChanged()
    {
        if (reloadQueued)
        {
            return;
        }

        reloadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            reloadQueued = false;
            Load();
        });
    }

    // 画面の図

    /// <summary>棚を出している画面と、取っ手・棚の場所を、縮めて描く。マウスが乗っている端には、出る場所を薄く描く。</summary>
    private void DrawDiagram()
    {
        var monitor = host.Monitor;
        var layout = host.Layout;
        var bounds = monitor.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        var ratio = Math.Min(DiagramMaxWidth / bounds.Width, DiagramMaxHeight / bounds.Height);
        var width = Math.Round(bounds.Width * ratio);
        var height = Math.Round(bounds.Height * ratio);
        Diagram.Width = width;
        Diagram.Height = height;
        Place(ScreenFrame, new Rect(0, 0, width, height));

        Rect Map(PixelRect r) => new(
            Math.Round((r.X - bounds.X) * ratio, 1),
            Math.Round((r.Y - bounds.Y) * ratio, 1),
            Math.Max(1, Math.Round(r.Width * ratio, 1)),
            Math.Max(1, Math.Round(r.Height * ratio, 1)));

        // タスクバー（画面と作業領域の差）
        var work = monitor.WorkArea;
        var bar = work.Bottom < bounds.Bottom ? PixelRect.FromEdges(bounds.X, work.Bottom, bounds.Right, bounds.Bottom)
            : work.Y > bounds.Y ? PixelRect.FromEdges(bounds.X, bounds.Y, bounds.Right, work.Y)
            : work.X > bounds.X ? PixelRect.FromEdges(bounds.X, bounds.Y, work.X, bounds.Bottom)
            : work.Right < bounds.Right ? PixelRect.FromEdges(work.Right, bounds.Y, bounds.Right, bounds.Bottom)
            : default;
        TaskbarBar.Visibility = bar.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (!bar.IsEmpty)
        {
            var rect = Map(bar);
            Place(TaskbarBar, new Rect(rect.X, rect.Y, rect.Width, Math.Max(3, rect.Height)));
        }

        Place(MiniPanel, Map(layout.Panel));
        Place(MiniHandle, HandleRect(layout, Map(layout.WorkArea), ratio));

        if (hoverEdge is { } edge && edge != layout.Edge)
        {
            var ghost = ShelfLayout.Compute(edge, work, monitor.Scale, host.Settings.HandlePosition);
            Place(GhostPanel, Map(ghost.Panel));
            GhostPanel.Visibility = Visibility.Visible;
        }
        else
        {
            GhostPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>図の取っ手（本物より少し大きく、端にぴったり付けて描く）。</summary>
    private static Rect HandleRect(ShelfLayout layout, Rect work, double ratio)
    {
        const double thickness = 4;
        var length = Math.Max(14, (layout.IsVertical ? layout.Handle.Height : layout.Handle.Width) * ratio * 1.4);
        var center = layout.IsVertical
            ? work.Y + ((layout.Handle.CenterY - layout.WorkArea.Y) * ratio)
            : work.X + ((layout.Handle.CenterX - layout.WorkArea.X) * ratio);
        var start = layout.IsVertical
            ? Math.Min(Math.Max(center - (length / 2), work.Y + 1), work.Bottom - 1 - length)
            : Math.Min(Math.Max(center - (length / 2), work.X + 1), work.Right - 1 - length);
        return layout.Edge switch
        {
            ShelfEdge.Right => new Rect(work.Right - thickness - 1, start, thickness, length),
            ShelfEdge.Left => new Rect(work.X + 1, start, thickness, length),
            ShelfEdge.Top => new Rect(start, work.Y + 1, length, thickness),
            _ => new Rect(start, work.Bottom - thickness - 1, length, thickness),
        };
    }

    private static void Place(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = Math.Max(0, rect.Width);
        element.Height = Math.Max(0, rect.Height);
    }

    /// <summary>図の上の点に、いちばん近い端（四隅から対角に分けた 4 つの三角のどれか）。</summary>
    private ShelfEdge NearestEdge(Point point)
    {
        var distances = new (ShelfEdge Edge, double Distance)[]
        {
            (ShelfEdge.Right, Diagram.Width - point.X),
            (ShelfEdge.Left, point.X),
            (ShelfEdge.Top, point.Y),
            (ShelfEdge.Bottom, Diagram.Height - point.Y),
        };
        return distances.MinBy(d => d.Distance).Edge;
    }

    private void Diagram_MouseMove(object sender, MouseEventArgs e)
    {
        var edge = NearestEdge(e.GetPosition(Diagram));
        if (edge != hoverEdge)
        {
            hoverEdge = edge;
            DrawDiagram();
        }
    }

    private void Diagram_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverEdge = null;
        DrawDiagram();
    }

    private void Diagram_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var edge = NearestEdge(e.GetPosition(Diagram));
        hoverEdge = null;
        if (edge != host.Settings.Edge)
        {
            // 棚の側から SettingsChanged が来て、ボタンも図も合わせ直される
            host.SetEdge(edge);
        }
        else
        {
            DrawDiagram();
        }
    }

    // 変えられたとき

    private void Edge_Checked(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }

        var edge = sender == EdgeLeft ? ShelfEdge.Left : sender == EdgeTop ? ShelfEdge.Top : sender == EdgeBottom ? ShelfEdge.Bottom : ShelfEdge.Right;
        if (edge != host.Settings.Edge)
        {
            host.SetEdge(edge);
        }

        DrawDiagram();
    }

    private void Monitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || MonitorBox.SelectedItem is not ComboBoxItem { Tag: MonitorInfo monitor })
        {
            return;
        }

        // メインの画面を選んだときは「メインの画面」として覚える（あとでメインを替えたら、そちらに出す）
        host.SetMonitor(monitor.IsPrimary ? null : monitor.DeviceName);
    }

    private void CenterHandle_Click(object sender, RoutedEventArgs e) => host.CenterHandle();

    private void OpenMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!loading)
        {
            host.SetOpenOnHover(OpenByHover.IsChecked == true);
        }
    }

    private void CloseAfterPaste_Click(object sender, RoutedEventArgs e) => host.SetCloseAfterPaste(CloseAfterPasteSwitch.IsChecked == true);

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme })
        {
            return;
        }

        // 一覧を閉じ終えてから色を入れ替える（入れ替えると、部品の見た目が全部作り直される）
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => host.SetTheme(theme));
    }

    private void MaxItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || MaxItemsBox.SelectedItem is not ComboBoxItem { Tag: int count } || count == shownMaxItems)
        {
            return;
        }

        var lost = host.CountOverLimit(count);
        if (lost > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"しまっておく数を {count:#,0} 件にすると、古いもの {lost:#,0} 件が棚から消えます（ピン留めしたものは消えません）。\n\n消してもよいですか？",
                "Hikidana の設定",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                loading = true;
                try
                {
                    SelectMaxItems(shownMaxItems);
                }
                finally
                {
                    loading = false;
                }

                return;
            }
        }

        shownMaxItems = count;
        host.SetMaxItems(count);
    }

    private void MoveToTop_Click(object sender, RoutedEventArgs e) => host.SetMoveToTop(MoveToTopSwitch.IsChecked == true);

    private void Signal_Click(object sender, RoutedEventArgs e) => host.SetSignalOnCapture(SignalSwitch.IsChecked == true);

    private void Hotkey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || HotkeyBox.SelectedItem is not ComboBoxItem { Tag: string choice } || choice == host.Settings.OpenHotkey)
        {
            return;
        }

        var works = host.SetHotkey(choice);
        HotkeyWarning.Visibility = works ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AutoDelete_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading)
        {
            return;
        }

        var (days, scope) = ChosenAutoDelete();
        var settings = host.Settings;
        AutoDeleteScopeBox.IsEnabled = days > 0;
        if (days == settings.AutoDeleteDays && (days == 0 || scope == settings.AutoDeleteScope))
        {
            return;
        }

        // もう日数を過ぎたものが、すぐ消えるときは確かめる
        var lost = host.CountAutoDelete(days, scope);
        if (lost > 0)
        {
            var what = scope == "all" ? "もの（ピン留め以外）" : "スクリーンショット";
            var answer = MessageBox.Show(
                this,
                $"最後に使ってから {days} 日より前の{what} {lost:#,0} 件が、いますぐ棚から消えます。\n\nよろしいですか？",
                "Hikidana の設定",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                loading = true;
                try
                {
                    SelectAutoDelete(settings.AutoDeleteDays, settings.AutoDeleteScope);
                }
                finally
                {
                    loading = false;
                }

                return;
            }
        }

        host.SetAutoDelete(days, scope);
    }

    private void Paused_Click(object sender, RoutedEventArgs e) => host.SetPaused(PausedSwitch.IsChecked == true);

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        var on = StartupSwitch.IsChecked == true;
        if (!host.SetStartWithWindows(on))
        {
            StartupSwitch.IsChecked = !on;
        }
    }

    private void ClearScreenshots_Click(object sender, RoutedEventArgs e) => ConfirmAndClear(ClearScope.Screenshots);

    private void ClearAll_Click(object sender, RoutedEventArgs e) => ConfirmAndClear(ClearScope.All);

    /// <summary>何件消えるかを見せて確かめてから、まとめて消す（ピン留めは残す）。</summary>
    private void ConfirmAndClear(ClearScope scope)
    {
        var count = host.CountClearable(scope);
        if (count == 0)
        {
            MessageBox.Show(
                this,
                scope == ClearScope.Screenshots ? "消せるスクリーンショットはありません（ピン留めしたものは消しません）。" : "消せるものはありません（ピン留めしたものは消しません）。",
                "Hikidana の設定",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var what = scope == ClearScope.Screenshots ? $"ピン留め以外のスクリーンショット {count:#,0} 件" : $"ピン留め以外の {count:#,0} 件";
        var answer = MessageBox.Show(
            this,
            $"{what}を、棚から消します。\n\n消したあと少しの間は、画面の端に出る知らせの「元に戻す」で戻せます。",
            "Hikidana の設定",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
        {
            host.Clear(scope);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "新しい版を確かめています…";
        try
        {
            await host.CheckForUpdateAsync();
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            Load();
        }
    }

    private void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        InstallUpdateButton.IsEnabled = false;
        host.InstallUpdate();
    }

    private void UpdateSwitch_Click(object sender, RoutedEventArgs e) => host.SetCheckForUpdates(UpdateSwitch.IsChecked == true);

    private void OpenData_Click(object sender, RoutedEventArgs e) => host.OpenDataFolder();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => host.OpenLogFolder();
}
