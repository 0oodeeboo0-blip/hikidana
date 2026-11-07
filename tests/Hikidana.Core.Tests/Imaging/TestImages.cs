using Hikidana.Core.Imaging;

namespace Hikidana.Core.Tests.Imaging;

/// <summary>テスト用の画像を作る。</summary>
internal static class TestImages
{
    /// <summary>位置で色が変わる不透明な画像（スクリーンショットのように、同じ色が続く所と細かい所がある）。</summary>
    public static PixelImage Opaque(int width, int height)
    {
        var image = PixelImage.Create(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var block = (x / 8 + y / 8) % 3;
                image.SetPixel(x, y, (byte)(x * 7 + block * 40), (byte)(y * 5), (byte)((x ^ y) & 0xFF));
            }
        }

        return image;
    }

    /// <summary>透明・半透明の画素がある画像。</summary>
    public static PixelImage WithAlpha(int width, int height)
    {
        var image = Opaque(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, _) = image.GetPixel(x, y);
                image.SetPixel(x, y, r, g, b, (byte)((x * 255 / Math.Max(1, width - 1) + y) & 0xFF));
            }
        }

        return image;
    }
}
