using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using Hikidana.App.Interop;
using Hikidana.Win32;

namespace Hikidana.App.Shelf;

/// <summary>棚のウインドウ。見せることと、押されたことを知らせることだけをする（決めるのは ShelfController）。</summary>
public partial class ShelfWindow : Window
{
    private const ushort LeftShift = 0xA0;
    private const ushort RightShift = 0xA1;

    /// <summary>これだけ動かしたら、クリックではなくドラッグとみなす（DIP）。</summary>
    private const double DragThreshold = 8;

    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;
    private static int openMenus;
    private readonly ICollectionView view;
    private Point? pressedAt;
    private ClipItem? pressedItem;
    private bool suppressClick;
    private ClipCategory? category;
    private bool pinnedOnly;
    private string query = string.Empty;
    private ClipItem? selected;
    private bool typing;
    private bool acrylic;

    static ShelfWindow()
    {
        // 棚のメニュー（右クリック・「…」）が開いている間は、マウスが棚の外（メニューの上）にあっても閉じないため
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((_, _) => openMenus++));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((_, _) => openMenus = Math.Max(0, openMenus - 1)));
    }

    public ShelfWindow()
    {
        InitializeComponent();
        CardList.ItemsSource = Items;
        view = CollectionViewSource.GetDefaultView(Items);
        view.Filter = Matches;
        Items.CollectionChanged += (_, _) => UpdateCounts();
    }

    /// <summary>カード（新しい順）。</summary>
    public ObservableCollection<ClipItem> Items { get; } = [];

    /// <summary>カードを押した。true なら Shift を押しながら（書式なし、画像はファイルとして）。</summary>
    public event Action<ClipItem, bool>? PasteRequested;

    public event Action<ClipItem, bool>? CopyRequested;

    public event Action<ClipItem>? PinRequested;

    public event Action<ClipItem>? DeleteRequested;

    public event Action? CloseRequested;

    public event Action<bool>? KeepOpenChanged;

    public event Action<bool>? PausedChanged;

    public event Action? OpenDataRequested;

    public event Action? OpenLogsRequested;

    public event Action? QuitRequested;

    /// <summary>「…」の「設定」。</summary>
    public event Action? SettingsRequested;

    /// <summary>「マウスを乗せるだけで開く」を入り切りした。</summary>
    public event Action<bool>? OpenOnHoverChanged;

    /// <summary>メニュー（右クリック・「…」）が開いている。</summary>
    public static bool IsMenuOpen => openMenus > 0;

    /// <summary>検索の欄をクリックした（キーボードで打てるよう、棚を前に出してほしい）。</summary>
    public event Action? TypingRequested;

    /// <summary>右クリックの「名前を付けて保存」。</summary>
    public event Action<ClipItem>? SaveRequested;

    /// <summary>カードのドラッグを始めた（落とし終わるまで戻らない）。</summary>
    public event Action<ClipItem, FrameworkElement>? DragRequested;

    /// <summary>ほかのアプリから、何かが落とされた。</summary>
    public event Action<IDataObject>? Dropped;

    /// <summary>件数と、空・見つからないの表示を、いまのカードに合わせる。</summary>
    public void UpdateCounts()
    {
        var total = Items.Count;
        var shown = IsFiltering ? view.Cast<object>().Count() : total;
        CountText.Text = total == 0 ? string.Empty : IsFiltering ? $"{shown} / {total} 件" : $"{total} 件";
        EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatchState.Visibility = total > 0 && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selected is not null && (!Items.Contains(selected) || !Matches(selected)))
        {
            Select(FirstVisible());
        }
    }

    /// <summary>キーボードで打てるようにする（棚を前に出して、検索の欄に入る）。</summary>
    public void BeginTyping()
    {
        typing = true;
        WindowHelper.Activate(this);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        if (selected is null)
        {
            Select(FirstVisible());
        }
    }

    /// <summary>検索と絞り込みをやめて、初めの見え方に戻す（閉じたとき）。</summary>
    public void ResetView()
    {
        typing = false;
        Select(null);
        if (query.Length > 0 || category is not null || pinnedOnly)
        {
            SearchBox.Text = string.Empty;
            FilterAll.IsChecked = true;
        }
    }

    private bool IsFiltering => query.Length > 0 || category is not null || pinnedOnly;

    public void ShowPaused(bool paused)
    {
        PausedBanner.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        PauseToggle.IsChecked = paused;
        PauseToggle.Content = paused ? "\uE768" : "\uE769";
        PauseToggle.ToolTip = paused ? "記録を再開する" : "記録を止める";
    }

    public void ShowOpenOnHover(bool on) => OpenOnHoverItem.IsChecked = on;

    public void ShowKeepOpen(bool keepOpen)
    {
        KeepOpenToggle.IsChecked = keepOpen;
        KeepOpenToggle.Content = keepOpen ? "\uE840" : "\uE718";
    }

    /// <summary>いちばん上まで戻す（開いたとき）。</summary>
    public void ScrollToTop()
    {
        if (CardList.Items.Count > 0)
        {
            (CardList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement)?.BringIntoView();
        }

        if (FindScrollViewer(CardList) is { } viewer)
        {
            viewer.ScrollToTop();
        }
    }

    /// <summary>
    /// 中身の大きさ（DIP）と、どちら側にくっつけるか。開くときにウインドウを端から伸ばすので、
    /// 中身は端と反対の側にくっつけておく（端から滑り出てくるように見える）。
    /// </summary>
    public void SetContentSize(double width, double height, Hikidana.Core.Layout.ShelfEdge edge)
    {
        Root.Width = width;
        Root.Height = height;
        Root.HorizontalAlignment = edge == Hikidana.Core.Layout.ShelfEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Root.VerticalAlignment = edge == Hikidana.Core.Layout.ShelfEdge.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    /// <summary>すりガラスにする（使えないときは、ふつうの色の背景）。ライトとダークが変わったときも呼ぶ。</summary>
    public void ApplyBackdrop()
    {
        var tint = Theme.Palette.IsDark ? Color.FromArgb(0xD1, 0x24, 0x24, 0x24) : Color.FromArgb(0xD6, 0xF3, 0xF3, 0xF3);
        if (Backdrop.IsAvailable && Backdrop.Apply(this, tint))
        {
            Background = Brushes.Transparent;
            acrylic = true;
            return;
        }

        if (acrylic)
        {
            Backdrop.Remove(this);
            acrylic = false;
        }

        SetResourceReference(BackgroundProperty, "Hkd.PanelBackground");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.MakeToolWindow(this, noActivate: true);
        WindowHelper.RoundCorners(this);
        ApplyBackdrop();
    }

    private static bool ShiftHeld() => InputSender.HeldModifiers().Any(k => k is LeftShift or RightShift);

    private static ClipItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ClipItem;

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        // カードの中のボタン（コピー・消す・ピン留め）からの知らせは、ここでは扱わない
        if (!ReferenceEquals(e.Source, sender) || ItemOf(sender) is not { } item)
        {
            return;
        }

        e.Handled = true;
        if (suppressClick)
        {
            // ドラッグのあとのクリックは、貼り付けにしない
            suppressClick = false;
            return;
        }

        PasteRequested?.Invoke(item, ShiftHeld());
    }

    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            PasteRequested?.Invoke(item, false);
        }
    }

    private void MenuAlternatePaste_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            PasteRequested?.Invoke(item, true);
        }
    }

    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            CopyRequested?.Invoke(item, false);
        }
    }

    private void MenuPin_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            PinRequested?.Invoke(item);
        }
    }

    private void MenuSave_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            SaveRequested?.Invoke(item);
        }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            DeleteRequested?.Invoke(item);
        }
    }

    private bool Matches(object value) =>
        value is ClipItem item
        && (pinnedOnly ? item.Pinned : category is null || item.Category == category)
        && (query.Length == 0 || Compare.IndexOf(item.SearchText, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth) >= 0);

    private void ApplyFilter()
    {
        view.Refresh();
        UpdateCounts();
        if (typing)
        {
            Select(FirstVisible());
        }

        ScrollToTop();
    }

    private ClipItem? FirstVisible() => view.Cast<ClipItem>().FirstOrDefault();

    private void Select(ClipItem? item)
    {
        if (ReferenceEquals(item, selected))
        {
            return;
        }

        if (selected is not null)
        {
            selected.IsSelected = false;
        }

        selected = item;
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        var index = view.Cast<ClipItem>().ToList().IndexOf(item);
        if (index >= 0 && FindPanel(CardList) is { } panel)
        {
            panel.BringIndexIntoViewPublic(index);
        }
    }

    private void MoveSelection(int step)
    {
        var visible = view.Cast<ClipItem>().ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var index = selected is null ? -1 : visible.IndexOf(selected);
        Select(visible[Math.Clamp(index + step, 0, visible.Count - 1)]);
    }

    private static VirtualizingStackPanel? FindPanel(DependencyObject root)
    {
        if (root is VirtualizingStackPanel panel)
        {
            return panel;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindPanel(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void SearchBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!typing)
        {
            TypingRequested?.Invoke();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter when (selected ?? FirstVisible()) is { } item:
                PasteRequested?.Invoke(item, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0)
                {
                    SearchBox.Text = string.Empty;
                }
                else
                {
                    CloseRequested?.Invoke();
                }

                e.Handled = true;
                break;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        if (typing)
        {
            SearchBox.Focus();
        }
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        // 読み込みの途中（まだ部品が揃っていない）でも呼ばれる
        if (!IsLoaded && sender != FilterAll)
        {
            return;
        }

        pinnedOnly = sender == FilterPinned;
        category = sender == FilterImages ? ClipCategory.Images
            : sender == FilterText ? ClipCategory.Text
            : sender == FilterLinks ? ClipCategory.Links
            : sender == FilterFiles ? ClipCategory.Files
            : null;
        if (view is not null)
        {
            ApplyFilter();
        }
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        suppressClick = false;
        pressedItem = IsInsideInnerButton(e.OriginalSource as DependencyObject, sender) ? null : ItemOf(sender);
        pressedAt = pressedItem is null ? null : e.GetPosition(this);
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (pressedAt is not { } start || pressedItem is not { } item)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            pressedAt = null;
            pressedItem = null;
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - start.X) < DragThreshold && Math.Abs(position.Y - start.Y) < DragThreshold)
        {
            return;
        }

        pressedAt = null;
        pressedItem = null;
        suppressClick = true;
        var card = (FrameworkElement)sender;
        try
        {
            DragRequested?.Invoke(item, card);
        }
        finally
        {
            // ドラッグの間に、ボタンが押されたままになっているので戻す
            card.ReleaseMouseCapture();
        }
    }

    /// <summary>押した所が、カードの中の小さなボタン（コピー・消す・ピン留め）なら true。</summary>
    private static bool IsInsideInnerButton(DependencyObject? source, object card)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, card); node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var accept = DropReader.CanRead(e.Data);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        // 中の部品の間を動いたときにも来るので、ウインドウの外に出たときだけ消す
        var position = e.GetPosition(this);
        if (position.X < 0 || position.Y < 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (DropReader.CanRead(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            Dropped?.Invoke(e.Data);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is { } item)
        {
            CopyRequested?.Invoke(item, ShiftHeld());
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is { } item)
        {
            DeleteRequested?.Invoke(item);
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is { } item)
        {
            PinRequested?.Invoke(item);
        }
    }

    private void KeepOpen_Click(object sender, RoutedEventArgs e)
    {
        var keep = KeepOpenToggle.IsChecked == true;
        ShowKeepOpen(keep);
        KeepOpenChanged?.Invoke(keep);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PausedChanged?.Invoke(PauseToggle.IsChecked == true);

    private void Resume_Click(object sender, RoutedEventArgs e) => PausedChanged?.Invoke(false);

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OpenOnHover_Click(object sender, RoutedEventArgs e) => OpenOnHoverChanged?.Invoke(OpenOnHoverItem.IsChecked);

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenDataRequested?.Invoke();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsRequested?.Invoke();

    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
