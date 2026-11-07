namespace Hikidana.Core.Clipboard;

/// <summary>クリップボードの 1 つの形と、その中身のバイト列。</summary>
/// <param name="Id">形の番号（CF_ の番号か、登録された番号）。</param>
/// <param name="Name">形の名前。CF_ の形は「CF_UNICODETEXT」のような名前。</param>
/// <param name="Data">中身。</param>
public sealed record ClipboardFormatData(uint Id, string Name, byte[] Data)
{
    public override string ToString() => $"{Name}（{Data.Length} バイト）";
}

/// <summary>ある時点のクリップボードの中身を写したもの。</summary>
public sealed class ClipboardSnapshot
{
    public ClipboardSnapshot(IReadOnlyList<ClipboardFormatData> formats, string? ownerProcess = null, uint sequence = 0, DateTimeOffset? capturedAt = null)
    {
        Formats = formats;
        OwnerProcess = ownerProcess;
        Sequence = sequence;
        CapturedAt = capturedAt ?? DateTimeOffset.Now;
    }

    /// <summary>クリップボードにあった順の形（読めたものだけ）。</summary>
    public IReadOnlyList<ClipboardFormatData> Formats { get; }

    /// <summary>クリップボードに書いたアプリの実行ファイルの名前（例: SnippingTool.exe）。分からなければ null。</summary>
    public string? OwnerProcess { get; }

    /// <summary>Windows のクリップボードの通し番号（GetClipboardSequenceNumber）。</summary>
    public uint Sequence { get; }

    public DateTimeOffset CapturedAt { get; }

    /// <summary>書いたアプリの名前がこれなら true（大文字と小文字は区別しない）。</summary>
    public bool IsOwnedBy(params string[] processNames) =>
        OwnerProcess is not null && processNames.Any(n => string.Equals(n, OwnerProcess, StringComparison.OrdinalIgnoreCase));

    public ClipboardFormatData? Find(uint id) => Formats.FirstOrDefault(f => f.Id == id);

    public ClipboardFormatData? Find(string name) =>
        Formats.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool Has(uint id) => Find(id) is not null;

    public bool Has(string name) => Find(name) is not null;

    public override string ToString() => $"[{OwnerProcess ?? "?"}] {string.Join(", ", Formats.Select(f => f.Name))}";
}
