namespace Hikidana.Core.Imaging;

/// <summary>画像を縮める（カードの縮小表示用）。面積の割合で平均するので、文字の多いスクリーンショットでもちらつかない。</summary>
public static class ImageScaler
{
    /// <summary>幅と高さが上限に収まるように縮める。もう収まっていれば、そのまま返す（大きくはしない）。</summary>
    public static PixelImage Fit(PixelImage source, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        if (scale >= 1.0)
        {
            return source;
        }

        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, maxWidth);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, maxHeight);
        return Shrink(source, width, height);
    }

    /// <summary>指定の大きさに縮める（元より大きい大きさは指定できない）。</summary>
    public static PixelImage Shrink(PixelImage source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width > source.Width || height > source.Height || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "縮めることしかできません。");
        }

        if (width == source.Width && height == source.Height)
        {
            return source;
        }

        var sw = source.Width;
        var sh = source.Height;
        var columns = Contributions(sw, width);
        var rows = Contributions(sh, height);
        var src = source.Bgra;

        // 横に縮める。アルファを掛けてから平均する（透明な所の色がにじまないように）
        var middle = new float[(long)width * sh * 4];
        for (var y = 0; y < sh; y++)
        {
            var rowStart = y * sw * 4;
            var outStart = (long)y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var (first, weights) = columns[x];
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var i = rowStart + (first + k) * 4;
                    var w = weights[k];
                    var alpha = src[i + 3] / 255f;
                    b += src[i] * alpha * w;
                    g += src[i + 1] * alpha * w;
                    r += src[i + 2] * alpha * w;
                    a += alpha * w;
                }

                var o = outStart + x * 4;
                middle[o] = b;
                middle[o + 1] = g;
                middle[o + 2] = r;
                middle[o + 3] = a;
            }
        }

        // 縦に縮めて、アルファを戻す
        var result = PixelImage.Create(width, height);
        var dst = result.Bgra;
        for (var y = 0; y < height; y++)
        {
            var (first, weights) = rows[y];
            for (var x = 0; x < width; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var i = ((long)(first + k) * width + x) * 4;
                    var w = weights[k];
                    b += middle[i] * w;
                    g += middle[i + 1] * w;
                    r += middle[i + 2] * w;
                    a += middle[i + 3] * w;
                }

                var o = (y * width + x) * 4;
                if (a <= 0.5f / 255f)
                {
                    dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0;
                    continue;
                }

                dst[o] = ToByte(b / a);
                dst[o + 1] = ToByte(g / a);
                dst[o + 2] = ToByte(r / a);
                dst[o + 3] = ToByte(a * 255f);
            }
        }

        return result;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);

    /// <summary>縮めた後の 1 画素に、元のどの画素がどれだけ入るか（重みの合計は 1）。</summary>
    private static (int First, float[] Weights)[] Contributions(int sourceLength, int targetLength)
    {
        var ratio = (double)sourceLength / targetLength;
        var result = new (int, float[])[targetLength];
        for (var i = 0; i < targetLength; i++)
        {
            var s0 = i * ratio;
            var s1 = (i + 1) * ratio;
            var first = (int)Math.Floor(s0);
            var last = Math.Min(sourceLength - 1, (int)Math.Ceiling(s1) - 1);
            var weights = new float[last - first + 1];
            for (var j = first; j <= last; j++)
            {
                var overlap = Math.Min(s1, j + 1) - Math.Max(s0, j);
                weights[j - first] = (float)(overlap / ratio);
            }

            result[i] = (first, weights);
        }

        return result;
    }
}
