using System.Buffers.Binary;
using System.IO.Compression;

namespace Hikidana.Core.Imaging;

/// <summary>
/// PNG の読み書き。書くのは 8 ビットの RGB か RGBA（透明な画素があるときだけ）。
/// 読むのは PNG の規格の全部（色の種類 0・2・3・4・6、ビット深度 1〜16、インターレース）。
/// </summary>
public static class PngCodec
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>PNG の先頭 8 バイトなら true。</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>PNG の大きさだけを読む（画素は読まない）。</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length < 33 || !IsPng(png) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        return width > 0 && height > 0;
    }

    /// <summary>PNG にする。透明な画素が無ければ RGB（小さくなる）、あれば RGBA で書く。色は sRGB とする。</summary>
    public static byte[] Encode(PixelImage image, CompressionLevel level = CompressionLevel.Optimal)
    {
        ArgumentNullException.ThrowIfNull(image);
        var alpha = image.HasTransparency();
        var bpp = alpha ? 4 : 3;
        var width = image.Width;
        var height = image.Height;
        var rowLength = width * bpp;

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(alpha ? 6 : 2);
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdr);
        WriteChunk(output, "sRGB"u8, [0]);

        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, level, leaveOpen: true))
            {
                var previous = new byte[rowLength];
                var current = new byte[rowLength];
                var candidates = new byte[5][];
                for (var f = 0; f < 5; f++)
                {
                    candidates[f] = new byte[rowLength + 1];
                    candidates[f][0] = (byte)f;
                }

                var source = image.Bgra;
                for (var y = 0; y < height; y++)
                {
                    var s = y * width * 4;
                    if (alpha)
                    {
                        for (int x = 0, d = 0; x < width; x++, s += 4, d += 4)
                        {
                            current[d] = source[s + 2];
                            current[d + 1] = source[s + 1];
                            current[d + 2] = source[s];
                            current[d + 3] = source[s + 3];
                        }
                    }
                    else
                    {
                        for (int x = 0, d = 0; x < width; x++, s += 4, d += 3)
                        {
                            current[d] = source[s + 2];
                            current[d + 1] = source[s + 1];
                            current[d + 2] = source[s];
                        }
                    }

                    var best = ChooseFilter(current, previous, bpp, candidates, y == 0);
                    z.Write(candidates[best]);
                    (previous, current) = (current, previous);
                }
            }

            WriteChunk(output, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>PNG を読む。壊れているときや大きすぎるときは <see cref="InvalidDataException"/>。</summary>
    public static PixelImage Decode(ReadOnlySpan<byte> png)
    {
        if (!IsPng(png))
        {
            throw new InvalidDataException("PNG ではありません。");
        }

        var header = default(Header);
        var haveHeader = false;
        byte[]? palette = null;
        byte[]? transparency = null;
        var compressed = new MemoryStream();
        var ended = false;
        var pos = 8;
        while (pos + 12 <= png.Length && !ended)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[pos..]);
            if (length < 0 || pos + 12L + length > png.Length)
            {
                throw new InvalidDataException("PNG のチャンクが途中で切れています。");
            }

            var type = png.Slice(pos + 4, 4);
            var data = png.Slice(pos + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png[(pos + 8 + length)..]);
            if (Crc32.Compute(png.Slice(pos + 4, 4 + length)) != crc)
            {
                throw new InvalidDataException("PNG の CRC が合いません。");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                header = ReadHeader(data);
                haveHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                ended = true;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException($"PNG に知らない大事なチャンク（{System.Text.Encoding.ASCII.GetString(type)}）があります。");
            }

            pos += 12 + length;
        }

        if (!haveHeader || compressed.Length == 0)
        {
            throw new InvalidDataException("PNG に IHDR か IDAT がありません。");
        }

        if (header.ColorType == 3 && palette is null)
        {
            throw new InvalidDataException("PNG にパレットがありません。");
        }

        var passes = Passes(header);
        long expected = 0;
        foreach (var p in passes)
        {
            if (p.Width > 0 && p.Height > 0)
            {
                expected += (long)p.Height * (1 + RowBytes(header, p.Width));
            }
        }

        if (expected > int.MaxValue)
        {
            throw new InvalidDataException("PNG が大きすぎます。");
        }

        var raw = new byte[expected];
        compressed.Position = 0;
        using (var z = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < raw.Length)
            {
                var n = z.Read(raw, read, raw.Length - read);
                if (n == 0)
                {
                    throw new InvalidDataException("PNG の画素のデータが足りません。");
                }

                read += n;
            }
        }

        var image = PixelImage.Create(header.Width, header.Height);
        var offset = 0;
        var filterBpp = Math.Max(1, header.BitsPerPixel / 8);
        foreach (var p in passes)
        {
            if (p.Width == 0 || p.Height == 0)
            {
                continue;
            }

            var rowBytes = RowBytes(header, p.Width);
            var previous = new byte[rowBytes];
            var row = new byte[rowBytes];
            for (var y = 0; y < p.Height; y++)
            {
                var filter = raw[offset];
                Array.Copy(raw, offset + 1, row, 0, rowBytes);
                offset += 1 + rowBytes;
                Unfilter(filter, row, previous, filterBpp);
                WriteRow(header, row, p, y, image, palette, transparency);
                (previous, row) = (row, previous);
            }
        }

        return image;
    }

    private static int ChooseFilter(byte[] raw, byte[] prior, int bpp, byte[][] candidates, bool firstRow)
    {
        var length = raw.Length;
        var best = 0;
        var bestScore = long.MaxValue;
        for (var f = 0; f < 5; f++)
        {
            // 1 行目は上が無いので Up と Average と Paeth は Sub か None と同じになる。None と Sub だけ試す
            if (firstRow && f >= 2)
            {
                break;
            }

            var target = candidates[f];
            long score = 0;
            for (var i = 0; i < length; i++)
            {
                int left = i >= bpp ? raw[i - bpp] : 0;
                int up = prior[i];
                int upLeft = i >= bpp ? prior[i - bpp] : 0;
                var value = f switch
                {
                    0 => raw[i],
                    1 => (byte)(raw[i] - left),
                    2 => (byte)(raw[i] - up),
                    3 => (byte)(raw[i] - ((left + up) >> 1)),
                    _ => (byte)(raw[i] - Paeth(left, up, upLeft)),
                };
                target[i + 1] = value;
                score += value < 128 ? value : 256 - value;
                if (score >= bestScore)
                {
                    break;
                }
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        // 途中でやめた候補は中身が半端なので、選んだものを最後まで作り直す
        var chosen = candidates[best];
        for (var i = 0; i < length; i++)
        {
            int left = i >= bpp ? raw[i - bpp] : 0;
            int up = prior[i];
            int upLeft = i >= bpp ? prior[i - bpp] : 0;
            chosen[i + 1] = best switch
            {
                0 => raw[i],
                1 => (byte)(raw[i] - left),
                2 => (byte)(raw[i] - up),
                3 => (byte)(raw[i] - ((left + up) >> 1)),
                _ => (byte)(raw[i] - Paeth(left, up, upLeft)),
            };
        }

        return best;
    }

    private static void Unfilter(byte filter, byte[] row, byte[] prior, int bpp)
    {
        switch (filter)
        {
            case 0:
                break;
            case 1:
                for (var i = bpp; i < row.Length; i++)
                {
                    row[i] += row[i - bpp];
                }

                break;
            case 2:
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] += prior[i];
                }

                break;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0;
                    row[i] += (byte)((left + prior[i]) >> 1);
                }

                break;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0;
                    int upLeft = i >= bpp ? prior[i - bpp] : 0;
                    row[i] += (byte)Paeth(left, prior[i], upLeft);
                }

                break;
            default:
                throw new InvalidDataException($"PNG のフィルター {filter} は知りません。");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static void WriteRow(Header h, byte[] row, Pass pass, int passY, PixelImage image, byte[]? palette, byte[]? transparency)
    {
        var target = image.Bgra;
        var y = pass.StartY + passY * pass.StepY;
        var maxSample = (1 << h.BitDepth) - 1;
        for (var px = 0; px < pass.Width; px++)
        {
            var x = pass.StartX + px * pass.StepX;
            var t = (y * h.Width + x) * 4;
            byte r, g, b, a = 255;
            switch (h.ColorType)
            {
                case 0:
                {
                    var raw = Sample(row, px, h.BitDepth);
                    var v = Scale(raw, h.BitDepth, maxSample);
                    r = g = b = v;
                    if (transparency is { Length: >= 2 } && raw == BinaryPrimitives.ReadUInt16BigEndian(transparency))
                    {
                        a = 0;
                    }

                    break;
                }

                case 2:
                {
                    var rr = Sample(row, px * 3, h.BitDepth);
                    var gg = Sample(row, px * 3 + 1, h.BitDepth);
                    var bb = Sample(row, px * 3 + 2, h.BitDepth);
                    r = Scale(rr, h.BitDepth, maxSample);
                    g = Scale(gg, h.BitDepth, maxSample);
                    b = Scale(bb, h.BitDepth, maxSample);
                    if (transparency is { Length: >= 6 }
                        && rr == BinaryPrimitives.ReadUInt16BigEndian(transparency)
                        && gg == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2))
                        && bb == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)))
                    {
                        a = 0;
                    }

                    break;
                }

                case 3:
                {
                    var index = Sample(row, px, h.BitDepth);
                    if (index * 3 + 2 < palette!.Length)
                    {
                        r = palette[index * 3];
                        g = palette[index * 3 + 1];
                        b = palette[index * 3 + 2];
                    }
                    else
                    {
                        r = g = b = 0;
                    }

                    if (transparency is not null && index < transparency.Length)
                    {
                        a = transparency[index];
                    }

                    break;
                }

                case 4:
                {
                    var v = Scale(Sample(row, px * 2, h.BitDepth), h.BitDepth, maxSample);
                    r = g = b = v;
                    a = Scale(Sample(row, px * 2 + 1, h.BitDepth), h.BitDepth, maxSample);
                    break;
                }

                default:
                {
                    r = Scale(Sample(row, px * 4, h.BitDepth), h.BitDepth, maxSample);
                    g = Scale(Sample(row, px * 4 + 1, h.BitDepth), h.BitDepth, maxSample);
                    b = Scale(Sample(row, px * 4 + 2, h.BitDepth), h.BitDepth, maxSample);
                    a = Scale(Sample(row, px * 4 + 3, h.BitDepth), h.BitDepth, maxSample);
                    break;
                }
            }

            target[t] = b;
            target[t + 1] = g;
            target[t + 2] = r;
            target[t + 3] = a;
        }
    }

    /// <summary>行の中の index 番目のサンプル（ビット深度のまま）。16 ビットなら 0〜65535。</summary>
    private static int Sample(byte[] row, int index, int depth)
    {
        switch (depth)
        {
            case 8:
                return row[index];
            case 16:
                return (row[index * 2] << 8) | row[index * 2 + 1];
            default:
                var bit = index * depth;
                var shift = 8 - depth - (bit & 7);
                return (row[bit >> 3] >> shift) & ((1 << depth) - 1);
        }
    }

    private static byte Scale(int sample, int depth, int maxSample) => depth switch
    {
        8 => (byte)sample,
        16 => (byte)(sample >> 8),
        _ => (byte)(sample * 255 / maxSample),
    };

    private static int RowBytes(Header h, int width) => (int)(((long)width * h.BitsPerPixel + 7) / 8);

    private static Header ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length != 13)
        {
            throw new InvalidDataException("PNG の IHDR の長さが違います。");
        }

        var h = new Header
        {
            Width = BinaryPrimitives.ReadInt32BigEndian(data),
            Height = BinaryPrimitives.ReadInt32BigEndian(data[4..]),
            BitDepth = data[8],
            ColorType = data[9],
            Interlace = data[12],
        };

        if (h.Width <= 0 || h.Height <= 0 || (long)h.Width * h.Height > PixelImage.MaxPixels)
        {
            throw new InvalidDataException($"PNG の大きさ（{h.Width} × {h.Height}）は扱えません。");
        }

        var valid = h.ColorType switch
        {
            0 => h.BitDepth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => h.BitDepth is 8 or 16,
            3 => h.BitDepth is 1 or 2 or 4 or 8,
            _ => false,
        };
        if (!valid || data[10] != 0 || data[11] != 0 || h.Interlace > 1)
        {
            throw new InvalidDataException($"PNG の形式（色 {h.ColorType}、深度 {h.BitDepth}）は扱えません。");
        }

        var channels = h.ColorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };
        h.BitsPerPixel = channels * h.BitDepth;
        return h;
    }

    private static Pass[] Passes(Header h)
    {
        if (h.Interlace == 0)
        {
            return [new Pass(0, 0, 1, 1, h.Width, h.Height)];
        }

        int[] startX = [0, 4, 0, 2, 0, 1, 0];
        int[] startY = [0, 0, 4, 0, 2, 0, 1];
        int[] stepX = [8, 8, 4, 4, 2, 2, 1];
        int[] stepY = [8, 8, 8, 4, 4, 2, 2];
        var passes = new Pass[7];
        for (var i = 0; i < 7; i++)
        {
            var w = h.Width > startX[i] ? (h.Width - startX[i] + stepX[i] - 1) / stepX[i] : 0;
            var hh = h.Height > startY[i] ? (h.Height - startY[i] + stepY[i] - 1) / stepY[i] : 0;
            passes[i] = new Pass(startX[i], startY[i], stepX[i], stepY[i], w, hh);
        }

        return passes;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);
        var crc = Crc32.Append(Crc32.Compute(type), data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private struct Header
    {
        public int Width;
        public int Height;
        public int BitDepth;
        public int ColorType;
        public int Interlace;
        public int BitsPerPixel;
    }

    private readonly record struct Pass(int StartX, int StartY, int StepX, int StepY, int Width, int Height);
}
