using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Imaging;

namespace Hikidana.Core.Tests.Clipboard;

/// <summary>テスト用のクリップボードの中身を作る。</summary>
internal static class Snap
{
    private static uint nextId = 0xC100;
    private static readonly Dictionary<string, uint> Registered = new(StringComparer.OrdinalIgnoreCase);

    public static ClipboardSnapshot Of(string? owner, params ClipboardFormatData[] formats) => new(formats, owner, 1);

    public static ClipboardFormatData UnicodeText(string text) =>
        new(StandardFormat.UnicodeText, "CF_UNICODETEXT", Encoding.Unicode.GetBytes(text + "\0"));

    public static ClipboardFormatData Named(string name, byte[] data) => new(IdOf(name), name, data);

    public static ClipboardFormatData Named(string name, string utf8) => Named(name, Encoding.UTF8.GetBytes(utf8));

    public static ClipboardFormatData Html(string fragment, string? sourceUrl = null) => Named(FormatName.Html, HtmlClipboard.Build(fragment, sourceUrl));

    public static ClipboardFormatData Png(PixelImage image) => Named(FormatName.Png, PngCodec.Encode(image));

    public static ClipboardFormatData DibV5(PixelImage image) => new(StandardFormat.DibV5, "CF_DIBV5", DibCodec.EncodeV5(image));

    public static ClipboardFormatData Dib(PixelImage image) => new(StandardFormat.Dib, "CF_DIB", DibCodec.EncodeDib(image));

    public static ClipboardFormatData Standard(uint id, byte[] data) => new(id, StandardFormat.NameOf(id), data);

    private static uint IdOf(string name)
    {
        lock (Registered)
        {
            if (!Registered.TryGetValue(name, out var id))
            {
                id = nextId++;
                Registered[name] = id;
            }

            return id;
        }
    }
}
