using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    private Point? pressedAt;
    private ClipItem? pressedItem;
    private bool suppressClick;

    public ShelfWindow()
    {
        InitializeComponent();
        CardList.ItemsSource = Items;
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

    /// <summary>カードのドラッグを始めた（落とし終わるまで戻らない）。</summary>
    public event Action<ClipItem, FrameworkElement>? DragRequested;

    /// <summary>ほかのアプリから、何かが落とされた。</summary>
    public event Action<IDataObject>? Dropped;

    public void ShowCount(int count) => CountText.Text = count == 0 ? string.Empty : $"{count} 件";

    public void ShowEmpty(bool empty) => EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

    public void ShowPaused(bool paused)
    {
        PausedBanner.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        PauseToggle.IsChecked = paused;
        PauseToggle.Content = paused ? "\uE768" : "\uE769";
        PauseToggle.ToolTip = paused ? "記録を再開する" : "記録を止める";
    }

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.MakeToolWindow(this, noActivate: true);
        WindowHelper.RoundCorners(this);
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

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenDataRequested?.Invoke();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogsRequested?.Invoke();

    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
