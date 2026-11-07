using System.Buffers.Binary;

namespace Hikidana.Core.Imaging;

/// <summary>
/// クリップボードの DIB（CF_DIB・CF_DIBV5）の読み書き。
/// 読むのは BITMAPCOREHEADER・BITMAPINFOHEADER・V4・V5、1〜32 ビット（パレット、16 ビットの 5-5-5、ビットフィールド）、上下どちら向きも。
/// 書くのは、CF_DIBV5 が 32 ビット（アルファつき、sRGB）、CF_DIB が 24 ビット（透明な所は白に重ねる）。どちらも下の行から（いちばん広く読める形）。
/// </summary>
public static class DibCodec
{
    public const int InfoHeaderSize = 40;
    public const int V5HeaderSize = 124;

    private const int BiRgb = 0;
    private const int BiBitfields = 3;
    private const int BiPng = 5;
    private const int BiAlphaBitfields = 6;
    private const uint LcsSrgb = 0x73524742; // 'sRGB'
    private const int LcsGmImages = 4;

    /// <summary>CF_DIB か CF_DIBV5 の中身を読む。読めないときは <see cref="InvalidDataException"/>。</summary>
    public static PixelImage Decode(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 12)
        {
            throw new InvalidDataException("DIB が短すぎます。");
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        int width, height, bitCount, compression, colorsUsed;
        int paletteEntrySize;
        uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0;
        var masksAfterHeader = 0;

        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(dib[6..]);
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
            compression = BiRgb;
            colorsUsed = 0;
            paletteEntrySize = 3;
        }
        else if (headerSize >= InfoHeaderSize && headerSize <= dib.Length)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
            colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
            paletteEntrySize = 4;

            if (compression is BiBitfields or BiAlphaBitfields)
            {
                if (headerSize >= 52)
                {
                    redMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
                    greenMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
                    blueMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
                    if (headerSize >= 56)
                    {
                        alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
                    }
                }
                else
                {
                    masksAfterHeader = compression == BiAlphaBitfields ? 16 : 12;
                    if (dib.Length < headerSize + masksAfterHeader)
                    {
                        throw new InvalidDataException("DIB のビットフィールドが足りません。");
                    }

                    redMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[headerSize..]);
                    greenMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(headerSize + 4)..]);
                    blueMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(headerSize + 8)..]);
                    if (compression == BiAlphaBitfields)
                    {
                        alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(headerSize + 12)..]);
                    }
                }
            }
            else if (headerSize >= 56 && compression == BiRgb && bitCount == 32)
            {
                // V4・V5 の BI_RGB でも、アルファのマスクが書いてあれば使う
                alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            }
        }
        else
        {
            throw new InvalidDataException($"DIB のヘッダーの大きさ（{headerSize}）が違います。");
        }

        if (compression == BiPng)
        {
            var offset = headerSize + PaletteBytes(bitCount, colorsUsed, paletteEntrySize);
            return PngCodec.Decode(dib[Math.Min(offset, dib.Length)..]);
        }

        if (compression is not (BiRgb or BiBitfields or BiAlphaBitfields))
        {
            throw new InvalidDataException($"DIB の圧縮（{compression}）は扱えません。");
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0 || (long)width * height > PixelImage.MaxPixels)
        {
            throw new InvalidDataException($"DIB の大きさ（{width} × {height}）は扱えません。");
        }

        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            throw new InvalidDataException($"DIB のビット数（{bitCount}）は扱えません。");
        }

        var paletteCount = bitCount <= 8 ? (colorsUsed > 0 ? Math.Min(colorsUsed, 1 << bitCount) : 1 << bitCount) : 0;
        var paletteOffset = headerSize + masksAfterHeader;
        var pixelOffset = paletteOffset + PaletteBytes(bitCount, colorsUsed, paletteEntrySize);
        var stride = (int)((((long)width * bitCount) + 31) / 32 * 4);
        var pixelBytes = (long)stride * height;

        // V5 のビットフィールドなのに、マスクをヘッダーの後ろにも重ねて書くアプリがある。中身がマスクと同じなら飛ばす
        if (masksAfterHeader == 0 && headerSize > InfoHeaderSize && compression is BiBitfields && bitCount >= 16
            && dib.Length >= pixelOffset + 12 + pixelBytes
            && BinaryPrimitives.ReadUInt32LittleEndian(dib[pixelOffset..]) == redMask
            && BinaryPrimitives.ReadUInt32LittleEndian(dib[(pixelOffset + 4)..]) == greenMask
            && BinaryPrimitives.ReadUInt32LittleEndian(dib[(pixelOffset + 8)..]) == blueMask)
        {
            pixelOffset += 12;
        }

        if (pixelOffset + pixelBytes > dib.Length)
        {
            throw new InvalidDataException("DIB の画素のデータが足りません。");
        }

        if (bitCount == 16 && compression == BiRgb)
        {
            redMask = 0x7C00;
            greenMask = 0x03E0;
            blueMask = 0x001F;
        }

        var image = PixelImage.Create(width, height);
        var target = image.Bgra;
        var palette = dib.Slice(paletteOffset, paletteCount * paletteEntrySize);
        var red = new MaskReader(redMask);
        var green = new MaskReader(greenMask);
        var blue = new MaskReader(blueMask);
        var alpha = new MaskReader(alphaMask);
        var anyAlpha = false;

        for (var row = 0; row < height; row++)
        {
            var source = dib.Slice(pixelOffset + row * stride, stride);
            var y = topDown ? row : height - 1 - row;
            var t = y * width * 4;
            for (var x = 0; x < width; x++, t += 4)
            {
                switch (bitCount)
                {
                    case 32 when compression == BiRgb:
                        target[t] = source[x * 4];
                        target[t + 1] = source[x * 4 + 1];
                        target[t + 2] = source[x * 4 + 2];
                        target[t + 3] = source[x * 4 + 3];
                        anyAlpha |= source[x * 4 + 3] != 0;
                        break;
                    case 32:
                    case 16:
                    {
                        var v = bitCount == 32
                            ? BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..])
                            : BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..]);
                        target[t] = blue.Read(v);
                        target[t + 1] = green.Read(v);
                        target[t + 2] = red.Read(v);
                        if (alpha.IsPresent)
                        {
                            var a = alpha.Read(v);
                            target[t + 3] = a;
                            anyAlpha |= a != 0;
                        }
                        else
                        {
                            target[t + 3] = 255;
                        }

                        break;
                    }

                    case 24:
                        target[t] = source[x * 3];
                        target[t + 1] = source[x * 3 + 1];
                        target[t + 2] = source[x * 3 + 2];
                        target[t + 3] = 255;
                        break;
                    default:
                    {
                        var bit = x * bitCount;
                        var index = (source[bit >> 3] >> (8 - bitCount - (bit & 7))) & ((1 << bitCount) - 1);
                        if (index < paletteCount)
                        {
                            target[t] = palette[index * paletteEntrySize];
                            target[t + 1] = palette[index * paletteEntrySize + 1];
                            target[t + 2] = palette[index * paletteEntrySize + 2];
                        }

                        target[t + 3] = 255;
                        break;
                    }
                }
            }
        }

        // 32 ビットでアルファがすべて 0 なのは「アルファを使っていない」印（多くのアプリがそう書く）。不透明にする
        var usesAlphaChannel = bitCount == 32 && (compression == BiRgb || alpha.IsPresent);
        if (usesAlphaChannel && !anyAlpha)
        {
            for (var i = 3; i < target.Length; i += 4)
            {
                target[i] = 255;
            }
        }

        return image;
    }

    /// <summary>CF_DIBV5 の中身を作る。32 ビット BGRA（アルファつき、掛け合わせない）、色は sRGB、下の行から。</summary>
    public static byte[] EncodeV5(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var width = image.Width;
        var height = image.Height;
        var stride = width * 4;
        var result = new byte[V5HeaderSize + (long)stride * height];
        var h = result.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(h, V5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], BiBitfields);
        BinaryPrimitives.WriteInt32LittleEndian(h[20..], stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], 3780); // 96 DPI（1 メートルあたりの画素）
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], 3780);
        BinaryPrimitives.WriteUInt32LittleEndian(h[40..], 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(h[44..], 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(h[48..], 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(h[52..], 0xFF000000);
        BinaryPrimitives.WriteUInt32LittleEndian(h[56..], LcsSrgb);
        BinaryPrimitives.WriteInt32LittleEndian(h[108..], LcsGmImages);

        var source = image.Bgra;
        for (var y = 0; y < height; y++)
        {
            Array.Copy(source, (long)y * stride, result, V5HeaderSize + (long)(height - 1 - y) * stride, stride);
        }

        return result;
    }

    /// <summary>CF_DIB の中身を作る。24 ビット（透明な所は白に重ねる）、下の行から。古いアプリでも読める形。</summary>
    public static byte[] EncodeDib(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var width = image.Width;
        var height = image.Height;
        var stride = (width * 3 + 3) & ~3;
        var result = new byte[InfoHeaderSize + (long)stride * height];
        var h = result.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(h, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], BiRgb);
        BinaryPrimitives.WriteInt32LittleEndian(h[20..], stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], 3780);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], 3780);

        var source = image.Bgra;
        for (var y = 0; y < height; y++)
        {
            var s = y * width * 4;
            var d = InfoHeaderSize + (height - 1 - y) * stride;
            for (var x = 0; x < width; x++, s += 4, d += 3)
            {
                int a = source[s + 3];
                if (a == 255)
                {
                    result[d] = source[s];
                    result[d + 1] = source[s + 1];
                    result[d + 2] = source[s + 2];
                }
                else
                {
                    result[d] = (byte)((source[s] * a + 255 * (255 - a) + 127) / 255);
                    result[d + 1] = (byte)((source[s + 1] * a + 255 * (255 - a) + 127) / 255);
                    result[d + 2] = (byte)((source[s + 2] * a + 255 * (255 - a) + 127) / 255);
                }
            }
        }

        return result;
    }

    private static int PaletteBytes(int bitCount, int colorsUsed, int entrySize)
    {
        if (bitCount > 8)
        {
            // 16 ビット以上でも biClrUsed にパレットを書くアプリがある（色の目安で、画素には使わない）
            return colorsUsed > 0 && colorsUsed <= 256 ? colorsUsed * entrySize : 0;
        }

        var count = colorsUsed > 0 ? Math.Min(colorsUsed, 1 << bitCount) : 1 << bitCount;
        return count * entrySize;
    }

    /// <summary>ビットフィールドのマスクから 8 ビットの値を取り出す。</summary>
    private readonly struct MaskReader
    {
        private readonly uint mask;
        private readonly int shift;
        private readonly int bits;

        public MaskReader(uint mask)
        {
            this.mask = mask;
            if (mask == 0)
            {
                shift = 0;
                bits = 0;
                return;
            }

            shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
            bits = System.Numerics.BitOperations.PopCount(mask);
        }

        public bool IsPresent => mask != 0;

        public byte Read(uint value)
        {
            if (bits == 0)
            {
                return 0;
            }

            var v = (value & mask) >> shift;
            if (bits == 8)
            {
                return (byte)v;
            }

            var max = (1u << bits) - 1;
            return (byte)((v * 255 + max / 2) / max);
        }
    }
}
