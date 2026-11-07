using System.Buffers.Binary;
using Hikidana.Core.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Imaging;

public class DibCodecTests
{
    [Fact]
    public void V5RoundTripsWithAlpha()
    {
        var image = TestImages.WithAlpha(31, 19);
        var dib = DibCodec.EncodeV5(image);
        Assert.Equal(DibCodec.V5HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(dib));
        Assert.True(image.SamePixels(DibCodec.Decode(dib)));
    }

    [Fact]
    public void V5HeaderDescribesSrgbBgraBottomUp()
    {
        var dib = DibCodec.EncodeV5(TestImages.Opaque(3, 2));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)), "高さが正（下の行から）。");
        Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16)), "BI_BITFIELDS");
        Assert.Equal(0x00FF0000u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(40)));
        Assert.Equal(0xFF000000u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(52)));
        Assert.Equal(0x73524742u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(56)), "sRGB");
        Assert.Equal(DibCodec.V5HeaderSize + 3 * 4 * 2, dib.Length);
    }

    [Fact]
    public void DibRoundTripsOpaqueImage()
    {
        var image = TestImages.Opaque(33, 21); // 33 × 3 バイトは 4 の倍数でない（行の詰め物を確かめる）
        var dib = DibCodec.EncodeDib(image);
        Assert.Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.True(image.SamePixels(DibCodec.Decode(dib)));
    }

    [Fact]
    public void DibPutsTransparentPixelsOnWhite()
    {
        var image = PixelImage.Create(3, 1);
        image.SetPixel(0, 0, 255, 0, 0, 0);
        image.SetPixel(1, 0, 0, 0, 0, 128);
        image.SetPixel(2, 0, 10, 20, 30, 255);
        var back = DibCodec.Decode(DibCodec.EncodeDib(image));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), back.GetPixel(0, 0));
        Assert.Equal(((byte)127, (byte)127, (byte)127, (byte)255), back.GetPixel(1, 0));
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), back.GetPixel(2, 0));
    }

    [Fact]
    public void Rgb32WithZeroAlphaIsOpaque()
    {
        // 多くのアプリは 32 ビットの BI_RGB でアルファを 0 のまま書く
        var pixels = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 };
        var image = DibCodec.Decode(Build(40, 2, 1, 32, 0, pixels));
        Assert.Equal(((byte)3, (byte)2, (byte)1, (byte)255), image.GetPixel(0, 0));
        Assert.Equal(((byte)6, (byte)5, (byte)4, (byte)255), image.GetPixel(1, 0));
    }

    [Fact]
    public void Rgb32WithAlphaKeepsAlpha()
    {
        var pixels = new byte[] { 1, 2, 3, 0, 4, 5, 6, 200 };
        var image = DibCodec.Decode(Build(40, 2, 1, 32, 0, pixels));
        Assert.Equal((byte)0, image.GetPixel(0, 0).A);
        Assert.Equal((byte)200, image.GetPixel(1, 0).A);
    }

    [Fact]
    public void TopDownRowsAreRead()
    {
        // 高さが負なら上の行から
        var pixels = new byte[]
        {
            0, 0, 255, 0, // 1 行目: 赤
            255, 0, 0, 0, // 2 行目: 青
        };
        var image = DibCodec.Decode(Build(40, 1, -2, 32, 0, pixels));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), image.GetPixel(0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), image.GetPixel(0, 1));
    }

    [Fact]
    public void BottomUpRowsAreRead()
    {
        var pixels = new byte[]
        {
            0, 0, 255, 0, // いちばん下の行: 赤
            255, 0, 0, 0, // その上: 青
        };
        var image = DibCodec.Decode(Build(40, 1, 2, 32, 0, pixels));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), image.GetPixel(0, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), image.GetPixel(0, 1));
    }

    [Fact]
    public void PaletteImagesAreRead()
    {
        byte[] palette = [0, 0, 0, 0, 255, 255, 255, 0, 0, 0, 255, 0]; // 黒・白・赤（BGRx）
        byte[] pixels = [0, 1, 2, 0]; // 4 画素（1 行 4 バイト）
        var image = DibCodec.Decode(Build(40, 4, 1, 8, 0, pixels, palette: palette, colorsUsed: 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), image.GetPixel(0, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), image.GetPixel(1, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), image.GetPixel(2, 0));
    }

    [Fact]
    public void Bitfields565AreRead()
    {
        var masks = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0xF800);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0x07E0);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x001F);
        var pixels = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(pixels, 0xF800); // 赤いっぱい
        BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(2), 0x07E0); // 緑いっぱい
        var image = DibCodec.Decode(Build(40, 2, 1, 16, 3, pixels, masksAfter: masks));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), image.GetPixel(0, 0));
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), image.GetPixel(1, 0));
    }

    [Fact]
    public void V5WithMasksRepeatedAfterHeaderIsRead()
    {
        // V5 のマスクを、ヘッダーの後ろにも重ねて書くアプリへの備え
        var image = TestImages.WithAlpha(5, 3);
        var v5 = DibCodec.EncodeV5(image);
        var withExtra = new byte[v5.Length + 12];
        v5.AsSpan(0, DibCodec.V5HeaderSize).CopyTo(withExtra);
        v5.AsSpan(40, 12).CopyTo(withExtra.AsSpan(DibCodec.V5HeaderSize));
        v5.AsSpan(DibCodec.V5HeaderSize).CopyTo(withExtra.AsSpan(DibCodec.V5HeaderSize + 12));
        Assert.True(image.SamePixels(DibCodec.Decode(withExtra)));
    }

    [Fact]
    public void BrokenDibIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => DibCodec.Decode(new byte[] { 1, 2, 3 }));
        Assert.Throws<InvalidDataException>(() => DibCodec.Decode(Build(40, 100, 100, 32, 0, new byte[16])));
        Assert.Throws<InvalidDataException>(() => DibCodec.Decode(Build(40, 4, 4, 32, 1, new byte[64])));
    }

    private static byte[] Build(int headerSize, int width, int height, int bitCount, int compression, byte[] pixels, byte[]? palette = null, byte[]? masksAfter = null, int colorsUsed = 0)
    {
        var size = headerSize + (masksAfter?.Length ?? 0) + (palette?.Length ?? 0) + pixels.Length;
        var dib = new byte[size];
        var s = dib.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(s[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(s[14..], (ushort)bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], compression);
        BinaryPrimitives.WriteInt32LittleEndian(s[32..], colorsUsed);
        var offset = headerSize;
        if (masksAfter is not null)
        {
            masksAfter.CopyTo(dib, offset);
            offset += masksAfter.Length;
        }

        if (palette is not null)
        {
            palette.CopyTo(dib, offset);
            offset += palette.Length;
        }

        pixels.CopyTo(dib, offset);
        return dib;
    }
}
