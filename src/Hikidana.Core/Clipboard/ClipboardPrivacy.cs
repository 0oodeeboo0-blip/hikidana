using System.Buffers.Binary;

namespace Hikidana.Core.Clipboard;

/// <summary>取り込まない理由。</summary>
public enum SkipReason
{
    /// <summary>取り込んでよい。</summary>
    None,

    /// <summary>Hikidana 自身が書いた中身。</summary>
    OwnWrite,

    /// <summary>「クリップボードの履歴に残さないで」の印（ExcludeClipboardContentFromMonitorProcessing）。</summary>
    MarkedPrivate,

    /// <summary>「履歴に残さないで」の印（CanIncludeInClipboardHistory が 0）。</summary>
    NotForHistory,

    /// <summary>KeePass などの印（Clipboard Viewer Ignore）。</summary>
    ViewerIgnore,

    /// <summary>記録しないアプリに指定されたアプリからのコピー。</summary>
    ExcludedApp,

    /// <summary>記録を止めている間のコピー。</summary>
    Paused,

    /// <summary>中身が空。</summary>
    Empty,
}

/// <summary>
/// 取り込んでよいかを決める。中身を読む前に、形の名前だけで決める（パスワードの中身を読まないため）。
/// </summary>
public static class ClipboardPrivacy
{
    /// <param name="formatNames">クリップボードにある形の名前。</param>
    /// <param name="readSmallFormat">名前を指定して中身を読む（CanIncludeInClipboardHistory の 4 バイトを読むのに使う）。</param>
    /// <param name="ownerProcess">書いたアプリの実行ファイルの名前。</param>
    /// <param name="excludedApps">記録しないアプリの実行ファイルの名前。</param>
    public static SkipReason Check(
        IReadOnlyCollection<string> formatNames,
        Func<string, byte[]?> readSmallFormat,
        string? ownerProcess = null,
        IReadOnlyCollection<string>? excludedApps = null)
    {
        bool Has(string name) => formatNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        if (formatNames.Count == 0)
        {
            return SkipReason.Empty;
        }

        if (Has(FormatName.HikidanaMarker))
        {
            return SkipReason.OwnWrite;
        }

        if (Has(FormatName.ExcludeFromMonitor))
        {
            return SkipReason.MarkedPrivate;
        }

        if (Has(FormatName.CanIncludeInHistory))
        {
            var data = readSmallFormat(FormatName.CanIncludeInHistory);
            if (data is { Length: >= 4 } && BinaryPrimitives.ReadInt32LittleEndian(data) == 0)
            {
                return SkipReason.NotForHistory;
            }
        }

        if (Has(FormatName.ViewerIgnore))
        {
            return SkipReason.ViewerIgnore;
        }

        if (ownerProcess is not null && excludedApps is not null
            && excludedApps.Any(a => string.Equals(a, ownerProcess, StringComparison.OrdinalIgnoreCase)))
        {
            return SkipReason.ExcludedApp;
        }

        return SkipReason.None;
    }
}
