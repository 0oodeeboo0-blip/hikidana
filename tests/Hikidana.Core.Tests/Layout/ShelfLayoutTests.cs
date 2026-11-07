using Hikidana.Core.Layout;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Layout;

public sealed class ShelfLayoutTests
{
    // 1920×1080 の画面で、下にタスクバー（48）がある作業領域
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);

    [Fact]
    public void RightEdgeAtHundredPercent()
    {
        var layout = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5);

        Assert.Equal(new PixelRect(1920 - 28, (1032 - 96) / 2, 28, 96), layout.Handle);
        Assert.Equal(new PixelRect(1920 - 10 - 360, 10, 360, 1032 - 20), layout.Panel);
        Assert.True(layout.IsVertical);
    }

    [Fact]
    public void LeftEdgeAtOneHundredFiftyPercent()
    {
        var work = new PixelRect(0, 0, 2880, 1640);
        var layout = ShelfLayout.Compute(ShelfEdge.Left, work, 1.5, 0.0);

        Assert.Equal(new PixelRect(0, 0, 42, 144), layout.Handle);
        Assert.Equal(new PixelRect(15, 15, 540, 1640 - 30), layout.Panel);
    }

    [Fact]
    public void TopAndBottomEdgesUseShortPanelUnderTheHandle()
    {
        var top = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.5);
        Assert.Equal(new PixelRect((1920 - 96) / 2, 0, 96, 28), top.Handle);
        Assert.Equal(640, top.Panel.Height);
        Assert.Equal(10, top.Panel.Y);
        Assert.Equal(top.Handle.CenterX, top.Panel.CenterX);
        Assert.False(top.IsVertical);

        var bottom = ShelfLayout.Compute(ShelfEdge.Bottom, WorkArea, 1.0, 1.0);
        Assert.Equal(1032 - 28, bottom.Handle.Y);
        Assert.Equal(1920 - 96, bottom.Handle.X);
        Assert.Equal(1032 - 10, bottom.Panel.Bottom);

        // 端に寄った取っ手でも、棚は作業領域からはみ出さない
        Assert.Equal(1920 - 10, bottom.Panel.Right);
    }

    [Fact]
    public void SecondMonitorOnTheLeftWithNegativeCoordinates()
    {
        var work = new PixelRect(-1280, 0, 1280, 984);
        var layout = ShelfLayout.Compute(ShelfEdge.Right, work, 1.0, 0.25);

        Assert.Equal(-28, layout.Handle.X);
        Assert.Equal((int)Math.Round((984 - 96) * 0.25), layout.Handle.Y);
        Assert.Equal(-10 - 360, layout.Panel.X);
    }

    [Fact]
    public void TinyScreenStillFits()
    {
        var work = new PixelRect(0, 0, 300, 80);
        var layout = ShelfLayout.Compute(ShelfEdge.Right, work, 1.0, 0.5);

        Assert.True(layout.Handle.Height <= 80);
        Assert.True(layout.Panel.Width <= 300 - 20);
        Assert.True(layout.Panel.X >= 0);
    }

    [Fact]
    public void BadValuesAreClamped()
    {
        var layout = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, double.NaN, 7);
        Assert.Equal(1.0, layout.Scale);
        Assert.Equal(1032 - 96, layout.Handle.Y);
        Assert.Throws<ArgumentException>(() => ShelfLayout.Compute(ShelfEdge.Right, default, 1, 0.5));
    }

    [Fact]
    public void ToastSitsBesideHandleOrPanel()
    {
        var layout = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5);

        var closed = layout.Toast(300, 60, panelOpen: false);
        Assert.Equal(1920 - 10 - 300, closed.X);
        Assert.Equal(layout.Handle.CenterY - 30, closed.Y);

        var open = layout.Toast(300, 60, panelOpen: true);
        Assert.Equal(layout.Panel.X - 10 - 300, open.X);

        var top = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.0);
        var nearCorner = top.Toast(300, 60, panelOpen: false);
        Assert.Equal(10, nearCorner.X);
        Assert.Equal(10, nearCorner.Y);
    }

    [Fact]
    public void DraggingTheHandleGivesPosition()
    {
        var layout = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5);

        Assert.Equal(0.0, layout.HandlePositionAt(0, 0));
        Assert.Equal(1.0, layout.HandlePositionAt(5000, 0));
        Assert.Equal(0.5, layout.HandlePositionAt((1032 - 96) / 2 + 40, 40));
    }
}
