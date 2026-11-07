using System.Runtime.InteropServices;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Imaging;
using Hikidana.Core.Paste;
using Hikidana.Win32.Native;

namespace Hikidana.Win32;

/// <summary>取り込む大きさの上限。</summary>
public sealed record CaptureLimits(long MaxFormatBytes = 64L * 1024 * 1024, long MaxImageBytes = 256L * 1024 * 1024, long MaxTotalBytes = 320L * 1024 * 1024);

/// <summary>読んだ結果。</summary>
public readonly record struct ReadResult(ClipboardSnapshot? Snapshot, SkipReason Reason, bool Busy);

/// <summary>書いた結果。</summary>
public enum WriteStatus
{
    /// <summary>書けて、読み戻しても同じだった。</summary>
    Ok,

    /// <summary>ほかのアプリがクリップボードを開いたままで、書けなかった。</summary>
    Busy,

    /// <summary>書いたはずが、読み戻すと違った（直後にほかのアプリが書いたなど）。</summary>
    NotVerified,

    /// <summary>Windows が受け付けなかった。</summary>
    Failed,
}

/// <summary>
/// クリップボードを読み書きする。どれもクリップボードを開いている時間をできるだけ短くする
/// （開いている間は、ほかのアプリが使えないため）。
/// </summary>
public static unsafe class ClipboardAccess
{
    /// <summary>クリップボードを開く。ほかのアプリが開いていれば、少し待ってやり直す。</summary>
    public static bool TryOpen(nint owner, int attempts = 10, int delayMilliseconds = 20)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(delayMilliseconds);
        }

        return false;
    }

    /// <summary>形の名前（CF_ の形は「CF_UNICODETEXT」、登録された形は登録された名前）。</summary>
    public static string NameOf(uint id)
    {
        if (id < 0xC000)
        {
            return StandardFormat.NameOf(id);
        }

        var buffer = stackalloc char[256];
        var length = NativeMethods.GetClipboardFormatName(id, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : $"#{id}";
    }

    /// <summary>
    /// いまのクリップボードを読む。「履歴に残さないで」の印などがあれば、中身は読まずに理由を返す。
    /// ほかのアプリが開いていて読めなければ Busy。
    /// </summary>
    public static ReadResult Read(nint owner, IReadOnlyCollection<string>? excludedApps = null, CaptureLimits? limits = null)
    {
        limits ??= new CaptureLimits();
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (!TryOpen(owner))
        {
            return new ReadResult(null, SkipReason.None, Busy: true);
        }

        try
        {
            var ids = new List<uint>();
            for (var f = NativeMethods.EnumClipboardFormats(0); f != 0; f = NativeMethods.EnumClipboardFormats(f))
            {
                ids.Add(f);
            }

            var names = ids.Select(NameOf).ToList();
            var ownerProcess = OwnerProcessName();
            var reason = ClipboardPrivacy.Check(
                names,
                name =>
                {
                    var i = names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                    return i >= 0 ? ReadGlobal(ids[i], 64) : null;
                },
                ownerProcess,
                excludedApps);
            if (reason != SkipReason.None)
            {
                return new ReadResult(null, reason, Busy: false);
            }

            // 同じ画像のいくつもの形は、いちばん良いものだけ読む（4K の画面なら 1 つ 30 MB ほどある）。
            // 読める PNG があれば PNG だけ。無ければ CF_DIB と CF_DIBV5 のうち先に並ぶほう
            // （アプリが置いた元の形。後ろに並ぶのは Windows が元の形から作ったもの）。
            var pngIndex = names.FindIndex(IsPngName);
            var png = pngIndex >= 0 ? ReadGlobal(ids[pngIndex], limits.MaxImageBytes) : null;
            var pngReadable = png is not null && PngCodec.TryReadSize(png, out _, out _);
            var dibIndex = ids.IndexOf(StandardFormat.Dib);
            var dibV5Index = ids.IndexOf(StandardFormat.DibV5);
            var skipImage = new HashSet<uint>();
            if (pngReadable)
            {
                skipImage.Add(StandardFormat.Dib);
                skipImage.Add(StandardFormat.DibV5);
            }
            else if (dibIndex >= 0 && dibV5Index >= 0)
            {
                skipImage.Add(dibIndex < dibV5Index ? StandardFormat.DibV5 : StandardFormat.Dib);
            }

            var formats = new List<ClipboardFormatData>();
            long total = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var name = names[i];
                if (StandardFormat.IsHandleFormat(id)
                    || StandardFormat.IsSynthesized(id)
                    || id is >= StandardFormat.PrivateFirst and <= StandardFormat.PrivateLast
                    || FormatName.Ephemeral.Contains(name)
                    || skipImage.Contains(id)
                    || IsPngName(name) && i != pngIndex)
                {
                    continue;
                }

                var isImage = id is StandardFormat.Dib or StandardFormat.DibV5 || IsPngName(name);
                var data = i == pngIndex ? png : ReadGlobal(id, isImage ? limits.MaxImageBytes : limits.MaxFormatBytes);
                if (data is null || total + data.Length > limits.MaxTotalBytes)
                {
                    continue;
                }

                total += data.Length;
                formats.Add(new ClipboardFormatData(id, name, data));
            }

            return new ReadResult(new ClipboardSnapshot(formats, ownerProcess, sequence), SkipReason.None, Busy: false);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>
    /// 書く。前の中身は消える。書いたあと読み戻して、自分が書いた中身になっているかを確かめる。
    /// <paramref name="owner"/> は自分のウインドウ（クリップボードの持ち主になる）。
    /// </summary>
    public static WriteStatus WriteAndVerify(nint owner, IReadOnlyList<OutgoingFormat> formats, int attempts = 3) =>
        WriteAndVerify(owner, formats, out _, attempts);

    /// <inheritdoc cref="WriteAndVerify(nint, IReadOnlyList{OutgoingFormat}, int)"/>
    /// <param name="sequence">確かめたときの通し番号（書けたときだけ）。この番号の変化は自分のもの。</param>
    public static WriteStatus WriteAndVerify(nint owner, IReadOnlyList<OutgoingFormat> formats, out uint sequence, int attempts = 3)
    {
        sequence = 0;
        var status = WriteStatus.Failed;
        for (var i = 0; i < attempts; i++)
        {
            status = Write(owner, formats);
            if (status == WriteStatus.Ok)
            {
                if (Verify(owner, formats, out sequence))
                {
                    return WriteStatus.Ok;
                }

                status = WriteStatus.NotVerified;
            }

            Thread.Sleep(60);
        }

        return status;
    }

    /// <summary>書く（確かめない）。</summary>
    public static WriteStatus Write(nint owner, IReadOnlyList<OutgoingFormat> formats)
    {
        if (!TryOpen(owner, attempts: 25))
        {
            return WriteStatus.Busy;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return WriteStatus.Failed;
            }

            foreach (var format in formats)
            {
                var id = IdOf(format);
                if (id == 0)
                {
                    return WriteStatus.Failed;
                }

                var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)Math.Max(1, format.Data.Length));
                if (handle == 0)
                {
                    return WriteStatus.Failed;
                }

                var pointer = NativeMethods.GlobalLock(handle);
                if (pointer == 0)
                {
                    NativeMethods.GlobalFree(handle);
                    return WriteStatus.Failed;
                }

                Marshal.Copy(format.Data, 0, pointer, format.Data.Length);
                NativeMethods.GlobalUnlock(handle);
                if (NativeMethods.SetClipboardData(id, handle) == 0)
                {
                    // 受け付けられなかったときだけ、自分でメモリを返す（受け付けられたら Windows のもの）
                    NativeMethods.GlobalFree(handle);
                    return WriteStatus.Failed;
                }
            }

            return WriteStatus.Ok;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>自分が書いた中身になっているか（持ち主が自分のウインドウで、どの形もあり、印が同じ）。</summary>
    public static bool Verify(nint owner, IReadOnlyList<OutgoingFormat> formats) => Verify(owner, formats, out _);

    /// <inheritdoc cref="Verify(nint, IReadOnlyList{OutgoingFormat})"/>
    /// <param name="sequence">開いている間に読んだ通し番号（開いている間は、ほかのアプリは書けない）。</param>
    public static bool Verify(nint owner, IReadOnlyList<OutgoingFormat> formats, out uint sequence)
    {
        sequence = 0;
        if (!TryOpen(owner))
        {
            return false;
        }

        try
        {
            sequence = NativeMethods.GetClipboardSequenceNumber();
            if (owner != 0 && NativeMethods.GetClipboardOwner() != owner)
            {
                return false;
            }

            foreach (var format in formats)
            {
                if (!NativeMethods.IsClipboardFormatAvailable(IdOf(format)))
                {
                    return false;
                }
            }

            var marker = formats.FirstOrDefault(f => f.Name == FormatName.HikidanaMarker);
            if (marker is not null)
            {
                var data = ReadGlobal(IdOf(marker), 4096);
                if (data is null || data.Length < marker.Data.Length || !data.AsSpan(0, marker.Data.Length).SequenceEqual(marker.Data))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>いまクリップボードにある形の一覧（調べる用。中身は読まない）。並びは、置かれた順。</summary>
    public static IReadOnlyList<(uint Id, string Name)> ListFormats(nint owner)
    {
        if (!TryOpen(owner))
        {
            return [];
        }

        try
        {
            var result = new List<(uint, string)>();
            for (var f = NativeMethods.EnumClipboardFormats(0); f != 0; f = NativeMethods.EnumClipboardFormats(f))
            {
                result.Add((f, NameOf(f)));
            }

            return result;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>1 つの形の中身を読む（調べる用）。無いか読めなければ null。</summary>
    public static byte[]? ReadFormat(nint owner, uint id, long maxBytes = 512L * 1024 * 1024)
    {
        if (!TryOpen(owner))
        {
            return null;
        }

        try
        {
            return ReadGlobal(id, maxBytes);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>名前で指定した形の中身を読む（調べる用）。</summary>
    public static byte[]? ReadFormat(nint owner, string name, long maxBytes = 512L * 1024 * 1024) =>
        ReadFormat(owner, NativeMethods.RegisterClipboardFormat(name), maxBytes);

    /// <summary>いまの通し番号（中身が変わるたびに増える）。</summary>
    public static uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    private static bool IsPngName(string name) =>
        string.Equals(name, FormatName.Png, StringComparison.OrdinalIgnoreCase) || string.Equals(name, FormatName.MimePng, StringComparison.OrdinalIgnoreCase);

    private static uint IdOf(OutgoingFormat format) => format.StandardId != 0 ? format.StandardId : NativeMethods.RegisterClipboardFormat(format.Name);

    private static string? OwnerProcessName()
    {
        var owner = NativeMethods.GetClipboardOwner();
        if (owner == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(owner, out var pid);
        return ProcessInfo.ImageName(pid);
    }

    private static byte[]? ReadGlobal(uint id, long maxBytes)
    {
        var handle = NativeMethods.GetClipboardData(id);
        if (handle == 0)
        {
            return null;
        }

        var size = (long)NativeMethods.GlobalSize(handle);
        if (size <= 0 || size > maxBytes || size > int.MaxValue)
        {
            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, (int)size);
            return data;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }
}
