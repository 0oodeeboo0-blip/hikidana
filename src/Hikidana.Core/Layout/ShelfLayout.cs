namespace Hikidana.Core.Layout;

/// <summary>棚を置く画面の端。</summary>
public enum ShelfEdge
{
    Right,
    Left,
    Top,
    Bottom,
}

/// <summary>画面の上の四角（本当の画素で）。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int CenterX => X + Width / 2;

    public int CenterY => Y + Height / 2;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public override string ToString() => $"({X}, {Y}) {Width}×{Height}";
}

/// <summary>
/// 取っ手と棚と知らせの置き場所を決める。大きさは DIP（96 dpi での画素）で決めてあり、画面の拡大率を掛けて本当の画素にする。
/// 取っ手と棚は、作業領域（タスクバーを除いた所）の端に付ける。
/// </summary>
public sealed class ShelfLayout
{
    /// <summary>棚の幅（右と左の端のとき）。上と下の端のときも、この幅の縦長の棚にする。</summary>
    public const double PanelWidth = 360;

    /// <summary>上と下の端のときの、棚のいちばん大きな高さ。</summary>
    public const double PanelMaxHeightTopBottom = 640;

    /// <summary>棚と作業領域の端のすきま。</summary>
    public const double PanelMargin = 10;

    /// <summary>取っ手の、端に沿った長さ。</summary>
    public const double HandleLength = 96;

    /// <summary>取っ手の厚み（マウスを乗せて広がったとき）。ふだんは端に細い線だけが見える。</summary>
    public const double HandleThickness = 28;

    /// <summary>知らせと、取っ手・棚のすきま。</summary>
    public const double ToastGap = 10;

    private ShelfLayout(ShelfEdge edge, PixelRect workArea, double scale, PixelRect handle, PixelRect panel)
    {
        Edge = edge;
        WorkArea = workArea;
        Scale = scale;
        Handle = handle;
        Panel = panel;
    }

    public ShelfEdge Edge { get; }

    public PixelRect WorkArea { get; }

    /// <summary>画面の拡大率（1.0 = 100%）。</summary>
    public double Scale { get; }

    /// <summary>取っ手のウインドウ（広がったときの大きさ）。</summary>
    public PixelRect Handle { get; }

    /// <summary>棚のウインドウ。</summary>
    public PixelRect Panel { get; }

    /// <summary>
    /// 棚の「届く範囲」: 棚の四角を、端の側へ作業領域の端まで広げて、取っ手の場所も含めたもの。
    /// マウスを乗せるだけで開くとき、マウスがこの外に出たら閉じる（棚と画面の端のすきまにいるだけでは閉じない）。
    /// </summary>
    public PixelRect PanelReach
    {
        get
        {
            var extended = Edge switch
            {
                ShelfEdge.Right => PixelRect.FromEdges(Panel.X, Panel.Y, WorkArea.Right, Panel.Bottom),
                ShelfEdge.Left => PixelRect.FromEdges(WorkArea.X, Panel.Y, Panel.Right, Panel.Bottom),
                ShelfEdge.Top => PixelRect.FromEdges(Panel.X, WorkArea.Y, Panel.Right, Panel.Bottom),
                _ => PixelRect.FromEdges(Panel.X, Panel.Y, Panel.Right, WorkArea.Bottom),
            };
            return PixelRect.FromEdges(
                Math.Min(extended.X, Handle.X),
                Math.Min(extended.Y, Handle.Y),
                Math.Max(extended.Right, Handle.Right),
                Math.Max(extended.Bottom, Handle.Bottom));
        }
    }

    /// <summary>右か左の端なら true（縦に長い取っ手）。</summary>
    public bool IsVertical => Edge is ShelfEdge.Right or ShelfEdge.Left;

    /// <param name="edge">置く端。</param>
    /// <param name="workArea">その画面の作業領域（本当の画素）。</param>
    /// <param name="scale">その画面の拡大率。</param>
    /// <param name="handlePosition">取っ手の位置（端に沿って 0 = 始め〜1 = 終わり。0.5 で真ん中）。</param>
    public static ShelfLayout Compute(ShelfEdge edge, PixelRect workArea, double scale, double handlePosition)
    {
        if (workArea.IsEmpty)
        {
            throw new ArgumentException("作業領域が空です。", nameof(workArea));
        }

        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        handlePosition = double.IsFinite(handlePosition) ? Math.Clamp(handlePosition, 0, 1) : 0.5;
        var vertical = edge is ShelfEdge.Right or ShelfEdge.Left;
        var available = vertical ? workArea.Height : workArea.Width;
        var length = Math.Min(Px(HandleLength, scale), available);
        var thickness = Math.Min(Px(HandleThickness, scale), vertical ? workArea.Width : workArea.Height);
        var along = (int)Math.Round((available - length) * handlePosition);
        var handle = edge switch
        {
            ShelfEdge.Right => new PixelRect(workArea.Right - thickness, workArea.Y + along, thickness, length),
            ShelfEdge.Left => new PixelRect(workArea.X, workArea.Y + along, thickness, length),
            ShelfEdge.Top => new PixelRect(workArea.X + along, workArea.Y, length, thickness),
            _ => new PixelRect(workArea.X + along, workArea.Bottom - thickness, length, thickness),
        };

        var margin = Px(PanelMargin, scale);
        var width = Math.Min(Px(PanelWidth, scale), workArea.Width - 2 * margin);
        PixelRect panel;
        if (vertical)
        {
            var height = workArea.Height - 2 * margin;
            var x = edge == ShelfEdge.Right ? workArea.Right - margin - width : workArea.X + margin;
            panel = new PixelRect(x, workArea.Y + margin, width, height);
        }
        else
        {
            var height = Math.Min(Px(PanelMaxHeightTopBottom, scale), workArea.Height - 2 * margin);
            var x = Math.Clamp(handle.CenterX - width / 2, workArea.X + margin, workArea.Right - margin - width);
            var y = edge == ShelfEdge.Top ? workArea.Y + margin : workArea.Bottom - margin - height;
            panel = new PixelRect(x, y, width, height);
        }

        return new ShelfLayout(edge, workArea, scale, handle, panel);
    }

    /// <summary>DIP を本当の画素にする。</summary>
    public static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    /// <summary>
    /// 知らせの置き場所。棚が開いていれば棚の横、閉じていれば取っ手の横（どちらも作業領域の中に収める）。
    /// </summary>
    public PixelRect Toast(int width, int height, bool panelOpen)
    {
        var gap = Px(ToastGap, Scale);
        var margin = Px(PanelMargin, Scale);
        width = Math.Min(width, WorkArea.Width - 2 * margin);
        height = Math.Min(height, WorkArea.Height - 2 * margin);
        var anchor = panelOpen ? Panel : Handle;
        int x, y;
        switch (Edge)
        {
            case ShelfEdge.Right:
                x = (panelOpen ? Panel.X - gap : WorkArea.Right - margin) - width;
                y = anchor.CenterY - height / 2;
                break;
            case ShelfEdge.Left:
                x = panelOpen ? Panel.Right + gap : WorkArea.X + margin;
                y = anchor.CenterY - height / 2;
                break;
            case ShelfEdge.Top:
                x = anchor.CenterX - width / 2;
                y = panelOpen ? Panel.Bottom + gap : WorkArea.Y + margin;
                break;
            default:
                x = anchor.CenterX - width / 2;
                y = (panelOpen ? Panel.Y - gap : WorkArea.Bottom - margin) - height;
                break;
        }

        x = Math.Clamp(x, WorkArea.X + margin, WorkArea.Right - margin - width);
        y = Math.Clamp(y, WorkArea.Y + margin, WorkArea.Bottom - margin - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// 開く途中の棚の四角。<paramref name="fraction"/> が 0〜1 で、端の側から伸びていく
    /// （中身は端と反対の側にくっつけておくので、端から滑り出てくるように見える）。
    /// </summary>
    public PixelRect PanelOpening(double fraction)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 1;
        var width = Math.Max(1, (int)Math.Round(Panel.Width * fraction));
        var height = Math.Max(1, (int)Math.Round(Panel.Height * fraction));
        return Edge switch
        {
            ShelfEdge.Right => new PixelRect(Panel.Right - width, Panel.Y, width, Panel.Height),
            ShelfEdge.Left => new PixelRect(Panel.X, Panel.Y, width, Panel.Height),
            ShelfEdge.Top => new PixelRect(Panel.X, Panel.Y, Panel.Width, height),
            _ => new PixelRect(Panel.X, Panel.Bottom - height, Panel.Width, height),
        };
    }

    /// <summary>取っ手をドラッグしたときの、新しい位置（0〜1）。<paramref name="pointer"/> は端に沿った座標（本当の画素）。</summary>
    public double HandlePositionAt(int pointer, int grabOffset)
    {
        var start = IsVertical ? WorkArea.Y : WorkArea.X;
        var available = (IsVertical ? WorkArea.Height : WorkArea.Width) - (IsVertical ? Handle.Height : Handle.Width);
        return available <= 0 ? 0.5 : Math.Clamp((pointer - grabOffset - start) / (double)available, 0, 1);
    }
}
