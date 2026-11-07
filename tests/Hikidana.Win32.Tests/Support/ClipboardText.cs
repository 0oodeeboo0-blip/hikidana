using System.Text;
using Hikidana.Core.Clipboard;

namespace Hikidana.Win32.Tests.Support;

/// <summary>いまのクリップボードのテキスト（CF_UNICODETEXT）。</summary>
internal static class ClipboardText
{
    public static string? Read()
    {
        var data = ClipboardAccess.ReadFormat(0, StandardFormat.UnicodeText);
        if (data is null)
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(data);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }
}
