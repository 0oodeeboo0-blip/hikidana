using System.Windows;
using System.Windows.Threading;
using Hikidana.App.Interop;
using Hikidana.Core.Layout;

namespace Hikidana.App.Shelf;

/// <summary>知らせの種類（Windows 11 の InfoBar と同じ色分け）。</summary>
public enum ToastKind
{
    Success,
    Info,
    Caution,
    Critical,
}

/// <summary>短い知らせ。</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer timer = new();
    private Action? action;

    public ToastWindow()
    {
        InitializeComponent();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Hide();
        };
        MouseEnter += (_, _) => timer.Stop();
        MouseLeave += (_, _) =>
        {
            if (IsVisible)
            {
                timer.Start();
            }
        };
    }

    /// <summary>知らせを出す。<paramref name="actionText"/> を渡すと、右にボタン（「元に戻す」など）を付ける。</summary>
    public void Present(string message, ToastKind kind, ShelfLayout layout, bool panelOpen, TimeSpan duration, string? actionText = null, Action? onAction = null)
    {
        Message.Text = message;
        var (glyph, color, background) = kind switch
        {
            ToastKind.Success => ("\uE73E", "Hkd.Success", "Hkd.SuccessBackground"),
            ToastKind.Caution => ("\uE7BA", "Hkd.Caution", "Hkd.CautionBackground"),
            ToastKind.Critical => ("\uE783", "Hkd.Critical", "Hkd.CriticalBackground"),
            _ => ("\uE946", "Hkd.Accent", "Hkd.InfoBackground"),
        };
        IconText.Text = glyph;
        IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, color);
        SetResourceReference(BackgroundProperty, background);

        action = onAction;
        ActionButton.Content = actionText;
        ActionButton.Visibility = actionText is null ? Visibility.Collapsed : Visibility.Visible;

        // 見せる前に中身の大きさを測って、ウインドウの大きさと置き場所を決める（ちらつかないように）
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Card.DesiredSize;
        var rect = layout.Toast((int)Math.Ceiling(size.Width * layout.Scale), (int)Math.Ceiling(size.Height * layout.Scale), panelOpen);
        WindowHelper.Place(this, rect);
        if (!IsVisible)
        {
            Show();
        }

        InvalidateMeasure();
        UpdateLayout();

        timer.Stop();
        timer.Interval = duration;
        timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.MakeToolWindow(this, noActivate: true);
        WindowHelper.RoundCorners(this);
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        timer.Stop();
        Hide();
        var run = action;
        action = null;
        run?.Invoke();
    }
}
