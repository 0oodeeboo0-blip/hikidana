using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hikidana.App.Interop;
using Hikidana.Win32;

namespace Hikidana.App.Shelf;

/// <summary>棚のウインドウ。見せることと、押されたことを知らせることだけをする（決めるのは ShelfController）。</summary>
public partial class ShelfWindow : Window
{
    private const ushort LeftShift = 0xA0;
    private const ushort RightShift = 0xA1;

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

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found)
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
        PasteRequested?.Invoke(item, ShiftHeld());
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
