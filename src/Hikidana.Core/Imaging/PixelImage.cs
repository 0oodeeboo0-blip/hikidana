using System.Security.Cryptography;

namespace Hikidana.Core.Imaging;

/// <summary>
/// 画素の並び。1 画素 4 バイトの BGRA（各 8 ビット）、上の行から順に並ぶ。アルファは掛け合わせていない（straight）。
/// </summary>
public sealed class PixelImage
{
    /// <summary>扱う画像の大きさの上限（画素の数）。4K の画面 6 枚分ほど。壊れたデータで大きなメモリを取らないため。</summary>
    public const long MaxPixels = 50_000_000;

    public PixelImage(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bgra);
        if ((long)width * height > MaxPixels)
        {
            throw new ArgumentException($"画像が大きすぎます（{width} × {height}）。", nameof(width));
        }

        if ((long)width * height * 4 != bgra.LongLength)
        {
            throw new ArgumentException("画素の数が大きさと合いません。", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>BGRA の画素。</summary>
    public byte[] Bgra { get; }

    /// <summary>1 行のバイト数。</summary>
    public int Stride => Width * 4;

    /// <summary>新しい画像を作る（すべて透明な黒）。</summary>
    public static PixelImage Create(int width, int height)
    {
        if ((long)width * height > MaxPixels)
        {
            throw new ArgumentException($"画像が大きすぎます（{width} × {height}）。", nameof(width));
        }

        return new PixelImage(width, height, new byte[checked(width * height * 4)]);
    }

    /// <summary>不透明でない画素が 1 つでもあれば true。</summary>
    public bool HasTransparency()
    {
        var data = Bgra;
        for (var i = 3; i < data.Length; i += 4)
        {
            if (data[i] != 255)
            {
                return true;
            }
        }

        return false;
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        var i = (y * Width + x) * 4;
        Bgra[i] = b;
        Bgra[i + 1] = g;
        Bgra[i + 2] = r;
        Bgra[i + 3] = a;
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Bgra[i + 2], Bgra[i + 1], Bgra[i], Bgra[i + 3]);
    }

    /// <summary>
    /// 見た目が同じなら true。完全に透明な画素は、色が違っても同じとみなす。
    /// </summary>
    public bool SamePixels(PixelImage other)
    {
        if (other.Width != Width || other.Height != Height)
        {
            return false;
        }

        var a = Bgra;
        var b = other.Bgra;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (a[i + 3] != b[i + 3])
            {
                return false;
            }

            if (a[i + 3] == 0)
            {
                continue;
            }

            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 画素から求めた指紋（16 進 32 文字）。同じ画像をもう一度コピーしたかを見分けるのに使う。
    /// 完全に透明な画素の色は無視する（アプリによって色の残し方が違うため）。
    /// </summary>
    public string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[8];
        BitConverter.TryWriteBytes(header, Width);
        BitConverter.TryWriteBytes(header[4..], Height);
        hash.AppendData(header);

        var data = Bgra;
        var buffer = new byte[64 * 1024];
        var n = 0;
        for (var i = 0; i < data.Length; i += 4)
        {
            if (data[i + 3] == 0)
            {
                buffer[n++] = 0;
                buffer[n++] = 0;
                buffer[n++] = 0;
                buffer[n++] = 0;
            }
            else
            {
                buffer[n++] = data[i];
                buffer[n++] = data[i + 1];
                buffer[n++] = data[i + 2];
                buffer[n++] = data[i + 3];
            }

            if (n == buffer.Length)
            {
                hash.AppendData(buffer);
                n = 0;
            }
        }

        hash.AppendData(buffer.AsSpan(0, n));
        return Convert.ToHexString(hash.GetHashAndReset())[..32];
    }
}
