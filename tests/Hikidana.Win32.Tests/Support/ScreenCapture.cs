using System.IO.Compression;
using Hikidana.Core.Imaging;
using static Hikidana.Win32.Tests.Support.TestNative;

namespace Hikidana.Win32.Tests.Support;

/// <summary>画面全体を撮る（CI で、貼り付けた様子を残すため）。</summary>
internal static unsafe class ScreenCapture
{
    /// <summary>
    /// 環境変数 HIKIDANA_SCREENSHOTS のフォルダーに「名前.png」で置く。環境変数が無ければ何もしない。
    /// </summary>
    public static string? Save(string name)
    {
        var folder = Environment.GetEnvironmentVariable("HIKIDANA_SCREENSHOTS");
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        File.WriteAllBytes(path, PngCodec.Encode(Capture(), CompressionLevel.Fastest));
        Console.WriteLine($"       画面写真: {path}");
        return path;
    }

    public static PixelImage Capture()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // 上から下へ
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        var bitmap = CreateDIBSection(memory, &header, 0, &bits, 0, 0);
        try
        {
            var previous = SelectObject(memory, bitmap);
            BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT);
            SelectObject(memory, previous);
            var data = new byte[width * height * 4];
            new ReadOnlySpan<byte>(bits, data.Length).CopyTo(data);
            for (var i = 3; i < data.Length; i += 4)
            {
                data[i] = 255; // GDI は透明度を 0 のままにする
            }

            return new PixelImage(width, height, data);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }
}
