using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Hikidana.Core.Clipboard;

/// <summary>
/// CF_HDROP（エクスプローラーでの「ファイルのコピー」）の中身。DROPFILES の 20 バイトの後に、
/// ファイルのパスが NUL 区切りで並び、最後に NUL がもう 1 つ付く。
/// </summary>
public static class DropFiles
{
    private const int HeaderSize = 20;

    /// <summary>パスの一覧を読む。形が違えば <see cref="InvalidDataException"/>。</summary>
    public static IReadOnlyList<string> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("CF_HDROP が短すぎます。");
        }

        var offset = BinaryPrimitives.ReadInt32LittleEndian(data);
        var wide = BinaryPrimitives.ReadInt32LittleEndian(data[16..]) != 0;
        if (offset < HeaderSize || offset > data.Length)
        {
            throw new InvalidDataException("CF_HDROP のパスの位置が違います。");
        }

        var body = data[offset..];
        string all;
        if (wide)
        {
            all = new string(MemoryMarshal.Cast<byte, char>(body[..(body.Length & ~1)]));
        }
        else
        {
            // 古いアプリは ANSI（日本語の Windows なら Shift_JIS）で書く
            all = AnsiEncoding().GetString(body);
        }

        var paths = new List<string>();
        foreach (var part in all.Split('\0'))
        {
            if (part.Length == 0)
            {
                break;
            }

            paths.Add(part);
        }

        return paths;
    }

    /// <summary>パスの一覧から CF_HDROP の中身を作る（UTF-16）。</summary>
    public static byte[] Build(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("パスがありません。", nameof(paths));
        }

        var text = string.Concat(list.Select(p => p + "\0")) + "\0";
        var result = new byte[HeaderSize + text.Length * 2];
        BinaryPrimitives.WriteInt32LittleEndian(result, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 1);
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(result.AsSpan(HeaderSize));
        return result;
    }

    private static Encoding AnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>「Preferred DropEffect」の中身（コピーなら 1、移動なら 2）。</summary>
    public static byte[] DropEffect(bool move = false)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(result, move ? 2 : 1);
        return result;
    }
}
