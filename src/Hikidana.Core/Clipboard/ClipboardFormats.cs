namespace Hikidana.Core.Clipboard;

/// <summary>Windows に最初からあるクリップボードの形（CF_ で始まるもの）の番号。</summary>
public static class StandardFormat
{
    public const uint Text = 1;
    public const uint Bitmap = 2;
    public const uint MetafilePict = 3;
    public const uint Sylk = 4;
    public const uint Dif = 5;
    public const uint Tiff = 6;
    public const uint OemText = 7;
    public const uint Dib = 8;
    public const uint Palette = 9;
    public const uint PenData = 10;
    public const uint Riff = 11;
    public const uint Wave = 12;
    public const uint UnicodeText = 13;
    public const uint EnhMetafile = 14;
    public const uint HDrop = 15;
    public const uint Locale = 16;
    public const uint DibV5 = 17;
    public const uint OwnerDisplay = 0x80;
    public const uint DspText = 0x81;
    public const uint DspBitmap = 0x82;
    public const uint DspMetafilePict = 0x83;
    public const uint DspEnhMetafile = 0x8E;
    public const uint PrivateFirst = 0x200;
    public const uint PrivateLast = 0x2FF;
    public const uint GdiObjectFirst = 0x300;
    public const uint GdiObjectLast = 0x3FF;

    /// <summary>名前（ログ用）。</summary>
    public static string NameOf(uint id) => id switch
    {
        Text => "CF_TEXT",
        Bitmap => "CF_BITMAP",
        MetafilePict => "CF_METAFILEPICT",
        Sylk => "CF_SYLK",
        Dif => "CF_DIF",
        Tiff => "CF_TIFF",
        OemText => "CF_OEMTEXT",
        Dib => "CF_DIB",
        Palette => "CF_PALETTE",
        PenData => "CF_PENDATA",
        Riff => "CF_RIFF",
        Wave => "CF_WAVE",
        UnicodeText => "CF_UNICODETEXT",
        EnhMetafile => "CF_ENHMETAFILE",
        HDrop => "CF_HDROP",
        Locale => "CF_LOCALE",
        DibV5 => "CF_DIBV5",
        OwnerDisplay => "CF_OWNERDISPLAY",
        DspText => "CF_DSPTEXT",
        DspBitmap => "CF_DSPBITMAP",
        DspMetafilePict => "CF_DSPMETAFILEPICT",
        DspEnhMetafile => "CF_DSPENHMETAFILE",
        _ => $"#{id}",
    };

    /// <summary>
    /// 中身がメモリのかたまり（HGLOBAL）ではなく、GDI などの「ハンドル」の形なら true。
    /// こうした形はバイト列として取り込めないので、読まない。
    /// </summary>
    public static bool IsHandleFormat(uint id) =>
        id is Bitmap or MetafilePict or Palette or EnhMetafile or OwnerDisplay or DspBitmap or DspMetafilePict or DspEnhMetafile
        || id is >= GdiObjectFirst and <= GdiObjectLast;

    /// <summary>Windows がほかの形から自動で作る形なら true（書き戻すときは元の形だけ置けばよい）。</summary>
    public static bool IsSynthesized(uint id) => id is Text or OemText or Locale or Bitmap;
}

/// <summary>名前で登録される形（RegisterClipboardFormat）。</summary>
public static class FormatName
{
    public const string Html = "HTML Format";
    public const string Rtf = "Rich Text Format";
    public const string Png = "PNG";
    public const string MimePng = "image/png";
    public const string PreferredDropEffect = "Preferred DropEffect";
    public const string ShellIdList = "Shell IDList Array";
    public const string FileNameW = "FileNameW";
    public const string FileName = "FileName";
    public const string Csv = "Csv";
    public const string XmlSpreadsheet = "XML Spreadsheet";
    public const string Biff12 = "Biff12";
    public const string Biff8 = "Biff8";
    public const string Biff5 = "Biff5";

    /// <summary>パスワード管理アプリなどが「クリップボードの履歴に残さないで」と付ける印（Microsoft の決まり）。</summary>
    public const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>DWORD の 0 なら「履歴に残さないで」（Microsoft の決まり）。</summary>
    public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";

    /// <summary>DWORD の 0 なら「クラウドに送らないで」。</summary>
    public const string CanUploadToCloud = "CanUploadToCloudClipboard";

    /// <summary>KeePass などが付ける印。クリップボードを見るアプリは読まないことになっている。</summary>
    public const string ViewerIgnore = "Clipboard Viewer Ignore";

    /// <summary>Hikidana が書いた中身に付ける印（自分で書いたものを取り込み直さないため）。</summary>
    public const string HikidanaMarker = "Hikidana.Clip";

    /// <summary>
    /// 取り込んでも意味がない形（OLE の生きたオブジェクトへの参照、ドラッグの見た目など）。
    /// 元のアプリを閉じると使えなくなるので、残さない。
    /// </summary>
    public static readonly IReadOnlySet<string> Ephemeral = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Object Descriptor",
        "Link Source",
        "Link Source Descriptor",
        "Embed Source",
        "Embedded Object",
        "Ole Private Data",
        "DataObject",
        "Native",
        "OwnerLink",
        "ObjectLink",
        "Link",
        "FileContents",
        "FileGroupDescriptor",
        "FileGroupDescriptorW",
        "DragImageBits",
        "DragContext",
        "DragSourceHelperFlags",
        "IsShowingLayered",
        "IsShowingText",
        "IsComputingImage",
        "ComputedDragImage",
        "UsingDefaultDragImage",
        "DisableDragText",
        "DropDescription",
        "InShellDragLoop",
        "Shell Object Offsets",
        "TargetCLSID",
        "Logical Performed DropEffect",
        "Performed DropEffect",
        "Paste Succeeded",
        ExcludeFromMonitor,
        CanIncludeInHistory,
        CanUploadToCloud,
        ViewerIgnore,
        HikidanaMarker,
    };
}
