using Hikidana.Core.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Imaging;

public class PngCodecTests
{
    // pypng（Python）で作った 7×5 の PNG。こちらの書き方と別の作り手のものを読めるかを確かめる
    private const string Rgba8Interlaced = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAYAAAH+ncZOAAAAg0lEQVR4nAXBIREAIRAF0B/iIiBOryYAMxhCrGGGADTAIFCrz9BgG2A2ApYk3HsAcDGQGmDpDvsaAt61USOC1bXNIsDvDZzW4No2fxEOzynwU8EZjv0pzFO5ZzjjU6xPNc0APdfRewL5VSjNQdyUat7UIyD+OkknCK8idQ7pTeXLWzT++IRArKc7M94AAAAASUVORK5CYII=";
    private const string Rgb16 = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFEAIAAABWaL3MAAAAtElEQVR4nCWOoQ3DMBBFLRUUBKRSQWAqBTTIAxRUQYaVF7gBzAINDxbcAIW3gWkkE0PDyBMYFnqE6tIDT/rS0/+n1HHTLDRW6LyQWBiysLS/pXQVVdfT11hdz7vzunYbsa49h6zr9V2arsOqlIIkKqTzbiyknp2HNKzEkMYlZEj3S2mQpPCYmmbibjOWeFidJ5YsvSETPz6lER/PRRQ1Ys/GRhwX5yPqShzxiSFHfN1KiwjpB74vTzmzx2/eAAAAAElFTkSuQmCC";
    private const string Gray1Interlaced = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFAQAAAAHW5vvjAAAAEUlEQVR4nGNgQAIPwHAVwyoAFR4D9YM4KScAAAAASUVORK5CYII=";
    private const string Palette4Alpha = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFBAMAAAB7tOvrAAAAGFBMVEX/AAAA/wAAAP///wAA////AP+AgIAKFB4SwLUWAAAAA3RSTlMAgMhUShYXAAAAH0lEQVR4nGNgVHZNYFB2TWdgcE1nVGBIZ1R2YACJAQAxTgP676lr9wAAAABJRU5ErkJggg==";
    private const string GrayAlpha16 = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFEAQAAABzA+IQAAAAe0lEQVR4nAXBMQ1AIQwFwOeF5SthxkMnHKCBIAIHpAq6sjVh6oaE4qD/DkBE+iJyiagtYsyItSPOiwBgnj7zXMxrMx/TfG3z88wB8E0f31z41sZ3TL5r8z2PL4Cu6euaS9fauo7Zde2u53UFQJI+klxIaiMZk2RtkvNIfikTVXTgMrX9AAAAAElFTkSuQmCC";
    private const string Gray8Trns = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAAAAACs8akEAAAAAnRSTlMAAHaTzTgAAAATSURBVHicY2DQCKhYcOIDAwEaADJ3EGn13XQpAAAAAElFTkSuQmCC";
    private const string Rgb8Trns = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAIAAAAG+GGPAAAABnRSTlMAAAAAAABupgeRAAAAR0lEQVR4nBXMQREAIQwEsMjhXSWIqAiU9I2clYOEm4uAwGJzuIQHZZVdTrkl5RXaaruddlvaa4w19jjjjow3/mnFjhM3Ei8+GQUeFY6LTIoAAAAASUVORK5CYII=";

    [Fact]
    public void OpaqueImageRoundTripsAsRgb()
    {
        var image = TestImages.Opaque(37, 23);
        var png = PngCodec.Encode(image);
        Assert.True(PngCodec.IsPng(png));
        Assert.Equal((byte)2, png[25], "色の種類は RGB（2）のはず。");
        Assert.True(PngCodec.TryReadSize(png, out var w, out var h));
        Assert.Equal(37, w);
        Assert.Equal(23, h);
        Assert.True(image.SamePixels(PngCodec.Decode(png)));
    }

    [Fact]
    public void TransparentImageRoundTripsAsRgba()
    {
        var image = TestImages.WithAlpha(40, 17);
        var png = PngCodec.Encode(image);
        Assert.Equal((byte)6, png[25], "色の種類は RGBA（6）のはず。");
        Assert.True(image.SamePixels(PngCodec.Decode(png)));
    }

    [Fact]
    public void OnePixelImageRoundTrips()
    {
        var image = PixelImage.Create(1, 1);
        image.SetPixel(0, 0, 10, 20, 30, 255);
        Assert.True(image.SamePixels(PngCodec.Decode(PngCodec.Encode(image))));
    }

    [Fact]
    public void ScreenshotSizedImageRoundTrips()
    {
        var image = TestImages.Opaque(1280, 720);
        var png = PngCodec.Encode(image);
        Assert.True(png.Length < image.Bgra.Length / 4, $"小さくなっていません（{png.Length} バイト）。");
        Assert.True(image.SamePixels(PngCodec.Decode(png)));
    }

    [Fact]
    public void ReadsInterlacedRgba8()
    {
        Check(Rgba8Interlaced, (x, y) => ((byte)(x * 36), (byte)(y * 50), (byte)((x + y) * 20), (byte)(255 - x * 30)));
    }

    [Fact]
    public void ReadsRgb16()
    {
        Check(Rgb16, (x, y) => ((byte)((x * 9000) >> 8), (byte)((y * 12000) >> 8), (byte)((x * y * 1000) >> 8), (byte)255));
    }

    [Fact]
    public void ReadsInterlacedGray1()
    {
        Check(Gray1Interlaced, (x, y) =>
        {
            var v = (byte)((x + y) % 2 * 255);
            return (v, v, v, (byte)255);
        });
    }

    [Fact]
    public void ReadsPalette4WithAlpha()
    {
        (byte, byte, byte, byte)[] palette =
        [
            (255, 0, 0, 0), (0, 255, 0, 128), (0, 0, 255, 200), (255, 255, 0, 255),
            (0, 255, 255, 255), (255, 0, 255, 255), (128, 128, 128, 255), (10, 20, 30, 255),
        ];
        Check(Palette4Alpha, (x, y) => palette[(x + 2 * y) % 8]);
    }

    [Fact]
    public void ReadsGrayAlpha16()
    {
        Check(GrayAlpha16, (x, y) =>
        {
            var v = (byte)((x * 9000) >> 8);
            return (v, v, v, (byte)((65535 - y * 10000) >> 8));
        });
    }

    [Fact]
    public void ReadsGray8WithTransparentColor()
    {
        Check(Gray8Trns, (x, y) =>
        {
            var v = (byte)(x * 40);
            return (v, v, v, (byte)(v == 0 ? 0 : 255));
        });
    }

    [Fact]
    public void ReadsRgb8WithTransparentColor()
    {
        Check(Rgb8Trns, (x, y) =>
        {
            var r = (byte)(x * 40);
            var g = (byte)(y * 50);
            return (r, g, (byte)0, (byte)(r == 0 && g == 0 ? 0 : 255));
        });
    }

    [Fact]
    public void BrokenCrcIsRejected()
    {
        var png = PngCodec.Encode(TestImages.Opaque(8, 8));
        png[30] ^= 0xFF; // IHDR の中身
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode(png));
    }

    [Fact]
    public void TruncatedPngIsRejected()
    {
        var png = PngCodec.Encode(TestImages.Opaque(64, 64));
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode(png.AsSpan(0, png.Length / 2)));
    }

    [Fact]
    public void NotPngIsRejected()
    {
        Assert.False(PngCodec.IsPng("hello, world"u8));
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode("hello, world"u8));
    }

    private static void Check(string base64, Func<int, int, (byte R, byte G, byte B, byte A)> expected)
    {
        var image = PngCodec.Decode(Convert.FromBase64String(base64));
        Assert.Equal(7, image.Width);
        Assert.Equal(5, image.Height);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                Assert.Equal(expected(x, y), image.GetPixel(x, y), $"({x}, {y}) の色");
            }
        }
    }
}
