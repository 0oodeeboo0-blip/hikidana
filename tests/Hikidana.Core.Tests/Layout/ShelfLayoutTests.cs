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
    public void TopAndBottomEdgesPutThePanelUnderTheHandle()
    {
        var top = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.5);
        Assert.Equal(new PixelRect((1920 - 96) / 2, 0, 96, 28), top.Handle);
        Assert.Equal(1032 - 20, top.Panel.Height);
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

    [Fact]
    public void OpeningPanelGrowsFromTheEdge()
    {
        var right = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5);
        var half = right.PanelOpening(0.5);
        Assert.Equal(right.Panel.Right, half.Right);
        Assert.Equal(180, half.Width);
        Assert.Equal(right.Panel.Height, half.Height);
        Assert.Equal(right.Panel, right.PanelOpening(1));
        Assert.Equal(right.Panel, right.PanelOpening(double.NaN));

        var left = ShelfLayout.Compute(ShelfEdge.Left, WorkArea, 1.0, 0.5);
        Assert.Equal(left.Panel.X, left.PanelOpening(0.25).X);
        Assert.Equal(90, left.PanelOpening(0.25).Width);

        var top = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.5);
        Assert.Equal(top.Panel.Y, top.PanelOpening(0.5).Y);
        Assert.Equal((1032 - 20) / 2, top.PanelOpening(0.5).Height);

        var bottom = ShelfLayout.Compute(ShelfEdge.Bottom, WorkArea, 1.0, 0.5);
        Assert.Equal(bottom.Panel.Bottom, bottom.PanelOpening(0.5).Bottom);
        Assert.Equal(1, bottom.PanelOpening(0).Height);
    }

    [Fact]
    public void ReachCoversTheGapToTheEdgeAndTheHandle()
    {
        var right = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.0);
        Assert.True(right.PanelReach.Contains(1919, 500), "棚と画面の端のすきま");
        Assert.True(right.PanelReach.Contains(1919, 2), "いちばん上の取っ手");
        Assert.True(right.PanelReach.Contains(1600, 500), "棚の上");
        Assert.False(right.PanelReach.Contains(1500, 500), "棚の左の外");

        var top = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.5);
        Assert.True(top.PanelReach.Contains(top.Handle.CenterX, 0));
        Assert.False(top.PanelReach.Contains(top.Handle.CenterX, top.Panel.Bottom + 5));
    }

    [Fact]
    public void HandleSizesChangeTheHandle()
    {
        var small = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5, new ShelfLayoutOptions { HandleSize = SizeChoice.Small });
        Assert.Equal(new PixelRect(1920 - 24, (1032 - 72) / 2, 24, 72), small.Handle);
        Assert.Equal(6, small.HandleMetrics.Hit);

        var large = ShelfLayout.Compute(ShelfEdge.Bottom, WorkArea, 1.5, 0.5, new ShelfLayoutOptions { HandleSize = SizeChoice.Large });
        Assert.Equal(216, large.Handle.Width);
        Assert.Equal(48, large.Handle.Height);
        Assert.Equal(16, large.HandleMetrics.Hit);
        Assert.Equal(10, ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5).HandleMetrics.Hit, "ふつうは中");
    }

    [Fact]
    public void ShorterPanelsSitBesideTheHandle()
    {
        // 中: 使える高さ（1012）の 4 分の 3。取っ手の真ん中に合わせる
        var medium = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5, new ShelfLayoutOptions { PanelSize = SizeChoice.Medium });
        Assert.Equal(759, medium.Panel.Height);
        Assert.Equal(medium.Handle.CenterY, medium.Panel.CenterY);
        Assert.Equal(1920 - 10 - 360, medium.Panel.X);

        // 取っ手がいちばん上なら、棚も上に寄せる（はみ出さない）
        var top = ShelfLayout.Compute(ShelfEdge.Left, WorkArea, 1.0, 0.0, new ShelfLayoutOptions { PanelSize = SizeChoice.Small });
        Assert.Equal(506, top.Panel.Height);
        Assert.Equal(10, top.Panel.Y);
        var bottom = ShelfLayout.Compute(ShelfEdge.Left, WorkArea, 1.0, 1.0, new ShelfLayoutOptions { PanelSize = SizeChoice.Small });
        Assert.Equal(1032 - 10, bottom.Panel.Bottom);

        // 上の端の棚も短くなる
        var topEdge = ShelfLayout.Compute(ShelfEdge.Top, WorkArea, 1.0, 0.5, new ShelfLayoutOptions { PanelSize = SizeChoice.Medium });
        Assert.Equal(759, topEdge.Panel.Height);
        Assert.Equal(10, topEdge.Panel.Y);

        // 低い画面でも、短くしすぎない（360 より短くしない。画面がもっと低ければ画面いっぱい）
        var low = new PixelRect(0, 0, 1280, 600);
        Assert.Equal(360, ShelfLayout.Compute(ShelfEdge.Right, low, 1.0, 0.5, new ShelfLayoutOptions { PanelSize = SizeChoice.Small }).Panel.Height);
        Assert.Equal(300 - 20, ShelfLayout.Compute(ShelfEdge.Right, new PixelRect(0, 0, 1280, 300), 1.0, 0.5, new ShelfLayoutOptions { PanelSize = SizeChoice.Small }).Panel.Height);
    }

    [Fact]
    public void LargerTextWidensThePanel()
    {
        var large = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5, new ShelfLayoutOptions { ContentScale = ShelfLayoutOptions.ScaleFor(SizeChoice.Large) });
        Assert.Equal(414, large.Panel.Width);
        Assert.Equal(1920 - 10 - 414, large.Panel.X);
        Assert.Equal(1.15, large.ContentScale);

        var small = ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 2.0, 0.5, new ShelfLayoutOptions { ContentScale = ShelfLayoutOptions.ScaleFor(SizeChoice.Small) });
        Assert.Equal(648, small.Panel.Width);
        Assert.Equal(1.0, ShelfLayout.Compute(ShelfEdge.Right, WorkArea, 1.0, 0.5, new ShelfLayoutOptions { ContentScale = double.NaN }).ContentScale);
    }
}
