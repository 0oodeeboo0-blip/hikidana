using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Hikidana.App.Interop;
using Hikidana.Core.Layout;

namespace Hikidana.App.Shelf;

/// <summary>画面の端の取っ手。</summary>
public partial class HandleWindow : Window
{
    /// <summary>これだけ動かしたら、クリックではなくドラッグとみなす（DIP）。</summary>
    private const double DragThreshold = 6;

    /// <summary>乗せたままにしたら開くまでの時間（通りかかっただけでは開かないように）。</summary>
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer hoverTimer = new() { Interval = HoverDelay };

    /// <summary>棚に入ったことを知らせる印を、出しておく長さ。</summary>
    private readonly DispatcherTimer flashTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool flashing;
    private ShelfLayout? layout;
    private string chevron = string.Empty;
    private bool dropping;
    private bool pressed;
    private bool dragging;
    private int pressedAt;
    private int grabOffset;

    public HandleWindow()
    {
        InitializeComponent();
        hoverTimer.Tick += (_, _) =>
        {
            hoverTimer.Stop();
            if (OpenOnHover && Root.IsMouseOver && !pressed && !dropping)
            {
                HoverDwelled?.Invoke();
            }
        };
        flashTimer.Tick += (_, _) =>
        {
            flashTimer.Stop();
            FadeFlash();
        };
    }

    /// <summary>
    /// 棚に何か入ったことを知らせる: つまみを少しの間アクセント色で出して、種類の印（<paramref name="glyph"/>）を見せ、引っ込める。
    /// マウスを乗せている・押している・何かを落とそうとしているときは出さない。
    /// </summary>
    public void Flash(string glyph)
    {
        if (pressed || dropping || Root.IsMouseOver || !IsVisible)
        {
            return;
        }

        flashing = true;
        Chevron.Text = glyph;
        Tab.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Hkd.Accent");
        Chevron.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Hkd.OnAccent");
        Expand(true);
        Tab.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        flashTimer.Stop();
        flashTimer.Start();
    }

    /// <summary>知らせの印を、ゆっくり消す。</summary>
    private void FadeFlash()
    {
        if (!flashing)
        {
            return;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
        fade.Completed += (_, _) =>
        {
            if (flashing)
            {
                EndFlash(collapse: !Root.IsMouseOver && !pressed && !dropping);
            }
        };
        Tab.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>知らせの印をやめて、ふつうのつまみに戻す。</summary>
    private void EndFlash(bool collapse)
    {
        flashing = false;
        flashTimer.Stop();
        Tab.BeginAnimation(OpacityProperty, null);
        Tab.Opacity = 1;
        Tab.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Hkd.HandleTab");
        Chevron.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Hkd.Accent");
        Chevron.Text = dropping ? "\uE710" : chevron;
        if (collapse)
        {
            Expand(false);
        }
    }

    /// <summary>マウスを乗せるだけで開く（乗せたままにすると <see cref="HoverDwelled"/>）。</summary>
    public bool OpenOnHover { get; set; }

    /// <summary>マウスを乗せたまま、少し待った。</summary>
    public event Action? HoverDwelled;

    /// <summary>クリックした。</summary>
    public event Action? Clicked;

    /// <summary>ドラッグして動かしている（新しい位置、0〜1）。</summary>
    public event Action<double>? Dragged;

    /// <summary>ドラッグを終えた。</summary>
    public event Action? DragFinished;

    /// <summary>ほかのアプリから、何かが落とされた。</summary>
    public event Action<IDataObject>? Dropped;

    /// <summary>置く端に合わせて、線とつまみの形を変える。</summary>
    public void Apply(ShelfLayout newLayout)
    {
        layout = newLayout;
        const double hit = 10;
        const double stripThickness = 4;
        const double stripLength = 56;
        const double inset = 2;
        switch (newLayout.Edge)
        {
            case ShelfEdge.Right:
                SetStrip(HorizontalAlignment.Right, VerticalAlignment.Stretch, hit, double.NaN);
                SetPill(HorizontalAlignment.Right, VerticalAlignment.Center, stripThickness, stripLength, new Thickness(0, 0, inset, 0));
                SetTab(new CornerRadius(8, 0, 0, 8), new Thickness(1, 1, 0, 1), "\uE76B");
                break;
            case ShelfEdge.Left:
                SetStrip(HorizontalAlignment.Left, VerticalAlignment.Stretch, hit, double.NaN);
                SetPill(HorizontalAlignment.Left, VerticalAlignment.Center, stripThickness, stripLength, new Thickness(inset, 0, 0, 0));
                SetTab(new CornerRadius(0, 8, 8, 0), new Thickness(0, 1, 1, 1), "\uE76C");
                break;
            case ShelfEdge.Top:
                SetStrip(HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, hit);
                SetPill(HorizontalAlignment.Center, VerticalAlignment.Top, stripLength, stripThickness, new Thickness(0, inset, 0, 0));
                SetTab(new CornerRadius(0, 0, 8, 8), new Thickness(1, 0, 1, 1), "\uE70D");
                break;
            default:
                SetStrip(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, hit);
                SetPill(HorizontalAlignment.Center, VerticalAlignment.Bottom, stripLength, stripThickness, new Thickness(0, 0, 0, inset));
                SetTab(new CornerRadius(8, 8, 0, 0), new Thickness(1, 1, 1, 0), "\uE70E");
                break;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.MakeToolWindow(this, noActivate: true);
    }

    private void SetStrip(HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
    {
        HitArea.HorizontalAlignment = horizontal;
        HitArea.VerticalAlignment = vertical;
        HitArea.Width = width;
        HitArea.Height = height;
    }

    private void SetPill(HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height, Thickness margin)
    {
        Strip.HorizontalAlignment = horizontal;
        Strip.VerticalAlignment = vertical;
        Strip.Width = width;
        Strip.Height = height;
        Strip.Margin = margin;
    }

    private void SetTab(CornerRadius corners, Thickness border, string glyph)
    {
        Tab.CornerRadius = corners;
        Tab.BorderThickness = border;
        chevron = glyph;
        Chevron.Text = dropping ? "\uE710" : glyph;
    }

    private void Expand(bool expanded)
    {
        Tab.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Strip.Visibility = expanded ? Visibility.Hidden : Visibility.Visible;
    }

    private int Along((int X, int Y) point) => layout?.IsVertical == false ? point.X : point.Y;

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        if (flashing)
        {
            EndFlash(collapse: false);
        }

        Expand(true);
        if (OpenOnHover)
        {
            hoverTimer.Start();
        }
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverTimer.Stop();
        if (!pressed && !dropping)
        {
            Expand(false);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        var accept = DropReader.CanRead(e.Data);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (accept && !dropping)
        {
            if (flashing)
            {
                EndFlash(collapse: false);
            }

            // 「ここに置ける」と見せる
            dropping = true;
            Chevron.Text = "\uE710";
            Expand(true);
        }

        e.Handled = true;
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X < 0 || position.Y < 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
        {
            EndDropping();
        }

        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        EndDropping();
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

    private void EndDropping()
    {
        dropping = false;
        Chevron.Text = chevron;
        Expand(false);
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (layout is null)
        {
            return;
        }

        e.Handled = true;
        hoverTimer.Stop();
        pressed = true;
        dragging = false;
        var cursor = WindowHelper.CursorPosition();
        pressedAt = Along(cursor);
        grabOffset = pressedAt - (layout.IsVertical ? layout.Handle.Y : layout.Handle.X);
        Root.CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!pressed || layout is null)
        {
            return;
        }

        var along = Along(WindowHelper.CursorPosition());
        if (!dragging && Math.Abs(along - pressedAt) >= DragThreshold * layout.Scale)
        {
            dragging = true;
            Cursor = layout.IsVertical ? Cursors.SizeNS : Cursors.SizeWE;
        }

        if (dragging)
        {
            Dragged?.Invoke(layout.HandlePositionAt(along, grabOffset));
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed)
        {
            return;
        }

        e.Handled = true;
        var wasDragging = dragging;
        pressed = false;
        dragging = false;
        Cursor = null;
        Root.ReleaseMouseCapture();
        if (wasDragging)
        {
            DragFinished?.Invoke();
        }
        else
        {
            Clicked?.Invoke();
        }

        if (!Root.IsMouseOver)
        {
            Expand(false);
        }
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (pressed)
        {
            // ドラッグの途中でマウスを取られた（ほかのウインドウが出たなど）
            pressed = false;
            if (dragging)
            {
                dragging = false;
                Cursor = null;
                DragFinished?.Invoke();
            }

            Expand(false);
        }
    }
}
