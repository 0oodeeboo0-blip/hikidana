using Hikidana.Core.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Imaging;

public class ImageScalerTests
{
    [Fact]
    public void SmallImageIsKept()
    {
        var image = TestImages.Opaque(100, 50);
        Assert.True(ReferenceEquals(image, ImageScaler.Fit(image, 200, 200)));
    }

    [Fact]
    public void FitKeepsAspectRatio()
    {
        var thumb = ImageScaler.Fit(TestImages.Opaque(1920, 1080), 768, 768);
        Assert.Equal(768, thumb.Width);
        Assert.Equal(432, thumb.Height);
    }

    [Fact]
    public void HalvingAveragesBlocks()
    {
        var image = PixelImage.Create(4, 2);
        image.SetPixel(0, 0, 0, 0, 0);
        image.SetPixel(1, 0, 100, 100, 100);
        image.SetPixel(0, 1, 200, 200, 200);
        image.SetPixel(1, 1, 100, 100, 100);
        for (var x = 2; x < 4; x++)
        {
            image.SetPixel(x, 0, 10, 20, 30);
            image.SetPixel(x, 1, 10, 20, 30);
        }

        var half = ImageScaler.Shrink(image, 2, 1);
        Assert.Equal(((byte)100, (byte)100, (byte)100, (byte)255), half.GetPixel(0, 0));
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), half.GetPixel(1, 0));
    }

    [Fact]
    public void TransparentColorDoesNotBleed()
    {
        // 左は透明な赤（色は見えない）、右は不透明な青。縮めても紫にならない
        var image = PixelImage.Create(2, 1);
        image.SetPixel(0, 0, 255, 0, 0, 0);
        image.SetPixel(1, 0, 0, 0, 255, 255);
        var one = ImageScaler.Shrink(image, 1, 1);
        var (r, g, b, a) = one.GetPixel(0, 0);
        Assert.Equal((byte)0, r);
        Assert.Equal((byte)0, g);
        Assert.Equal((byte)255, b);
        Assert.Equal((byte)128, a);
    }
}
