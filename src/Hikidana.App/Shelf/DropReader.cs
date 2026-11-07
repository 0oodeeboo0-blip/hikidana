using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hikidana.App.Interop;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Imaging;
using IComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using IDataObject = System.Windows.IDataObject;

namespace Hikidana.App.Shelf;

/// <summary>
/// 取っ手や棚に落とされたもの（ほかのアプリからのドラッグ）を、クリップボードの中身と同じ形（<see cref="ClipboardSnapshot"/>）にする。
/// こうすると、コピーしたものと同じ見分け方でしまえる。
/// </summary>
internal static class DropReader
{
    private const string FileGroupDescriptorW = "FileGroupDescriptorW";
    private const string FileContents = "FileContents";
    private const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>しまえるものが入っていれば true（自分の棚のカードをドラッグしているときは false）。</summary>
    public static bool CanRead(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(FormatName.HikidanaMarker, false))
            {
                return false;
            }

            return data.GetDataPresent(DataFormats.FileDrop, false)
                || data.GetDataPresent(DataFormats.UnicodeText, false)
                || data.GetDataPresent(DataFormats.Text, false)
                || data.GetDataPresent(DataFormats.Html, false)
                || data.GetDataPresent(FormatName.Png, false)
                || data.GetDataPresent(DataFormats.Dib, false)
                || data.GetDataPresent(FileGroupDescriptorW, false)
                || data.GetDataPresent(FormatName.UrlW, false);
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>落とされたものを読む（落とされた瞬間にしか読めないので、すぐに呼ぶ）。読めるものが無ければ null。</summary>
    public static ClipboardSnapshot? Read(IDataObject data)
    {
        if (!CanRead(data))
        {
            return null;
        }

        var formats = new List<ClipboardFormatData>();
        var files = Try(() => data.GetData(DataFormats.FileDrop, true) as string[]);
        if (files is { Length: > 0 })
        {
            formats.Add(new ClipboardFormatData(StandardFormat.HDrop, "CF_HDROP", DropFiles.Build(files)));
        }

        var text = Try(() => data.GetData(DataFormats.UnicodeText, true) as string) ?? Try(() => data.GetData(DataFormats.Text, true) as string);
        if (string.IsNullOrEmpty(text) && data.GetDataPresent(FormatName.UrlW, false))
        {
            text = ReadString(data, FormatName.UrlW, Encoding.Unicode);
        }

        if (!string.IsNullOrEmpty(text))
        {
            formats.Add(new ClipboardFormatData(StandardFormat.UnicodeText, "CF_UNICODETEXT", Utf16z(text)));
        }

        if (Try(() => data.GetData(DataFormats.Html, true) as string) is { Length: > 0 } html)
        {
            // HTML Format は UTF-8 のバイト列（先頭の位置の数字もバイトで数えてある）。WPF が文字にしたものを、もとのバイトに戻す
            formats.Add(new ClipboardFormatData(0, FormatName.Html, Encoding.UTF8.GetBytes(html)));
        }

        if (files is not { Length: > 0 })
        {
            var png = ReadBytes(data, FormatName.Png);
            if (png is not null && PngCodec.TryReadSize(png, out _, out _))
            {
                formats.Add(new ClipboardFormatData(0, FormatName.Png, png));
            }
            else if (ReadBytes(data, DataFormats.Dib) is { Length: > 40 } dib)
            {
                formats.Add(new ClipboardFormatData(StandardFormat.Dib, "CF_DIB", dib));
            }
            else if (data.GetDataPresent(FileGroupDescriptorW, false) && ReadFirstFileContents(data) is { } contents && ToPng(contents) is { } converted)
            {
                // ブラウザーの画像などは、ファイルの中身だけが渡される（ファイルはどこにも無い）
                formats.Add(new ClipboardFormatData(0, FormatName.Png, converted));
            }
        }

        return formats.Count == 0 ? null : new ClipboardSnapshot(formats, ClipLabels.DropSource);
    }

    private static T? Try<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or OutOfMemoryException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }

    private static byte[]? ReadBytes(IDataObject data, string format)
    {
        if (!data.GetDataPresent(format, false))
        {
            return null;
        }

        return Try(() => data.GetData(format, false)) switch
        {
            MemoryStream stream when stream.Length <= MaxBytes => stream.ToArray(),
            byte[] bytes => bytes,
            _ => null,
        };
    }

    private static string? ReadString(IDataObject data, string format, Encoding encoding) =>
        ReadBytes(data, format) is { } bytes ? encoding.GetString(bytes).TrimEnd('\0') : null;

    private static byte[] Utf16z(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    /// <summary>1 つ目のファイルの中身（FileContents）を、COM の IDataObject から直接読む。</summary>
    private static unsafe byte[]? ReadFirstFileContents(IDataObject data)
    {
        if (data is not IComDataObject com)
        {
            return null;
        }

        var format = new FORMATETC
        {
            cfFormat = unchecked((short)Native.RegisterClipboardFormat(FileContents)),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = 0,
            ptd = 0,
            tymed = TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM,
        };
        STGMEDIUM medium;
        try
        {
            com.GetData(ref format, out medium);
        }
        catch (COMException ex)
        {
            Log.Warn("落とされたファイルの中身を読めませんでした。", ex);
            return null;
        }

        try
        {
            if (medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != 0)
            {
                var size = (long)Native.GlobalSize(medium.unionmember);
                if (size <= 0 || size > MaxBytes)
                {
                    return null;
                }

                var pointer = Native.GlobalLock(medium.unionmember);
                if (pointer == 0)
                {
                    return null;
                }

                try
                {
                    return new ReadOnlySpan<byte>((void*)pointer, (int)size).ToArray();
                }
                finally
                {
                    Native.GlobalUnlock(medium.unionmember);
                }
            }

            if (medium.tymed == TYMED.TYMED_ISTREAM && medium.unionmember != 0 && Marshal.GetObjectForIUnknown(medium.unionmember) is IStream stream)
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                var read = 0;
                do
                {
                    stream.Read(chunk, chunk.Length, (nint)(&read));
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxBytes)
                    {
                        return null;
                    }
                }
                while (read > 0);
                return buffer.ToArray();
            }

            return null;
        }
        finally
        {
            Native.ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>画像のファイルの中身（PNG・JPEG・GIF・BMP など）を PNG にする。画像でなければ null。</summary>
    private static byte[]? ToPng(byte[] bytes)
    {
        if (PngCodec.TryReadSize(bytes, out _, out _))
        {
            return bytes;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > PixelImage.MaxPixels)
            {
                return null;
            }

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return PngCodec.Encode(new PixelImage(frame.PixelWidth, frame.PixelHeight, pixels));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
