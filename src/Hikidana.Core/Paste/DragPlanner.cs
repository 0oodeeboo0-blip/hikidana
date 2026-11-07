using System.Runtime.InteropServices;
using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Storage;

namespace Hikidana.Core.Paste;

/// <summary>
/// ドラッグで渡すもの。落とす先はドラッグを始めたときにはわからないので、どこでも受け取れる形にする。
/// 画面の方が、これを Windows のドラッグの形（OLE の DataObject）にする。
/// </summary>
public sealed class DragPlan
{
    /// <summary>ファイルとして渡すもの（CF_HDROP）。</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>文字として渡すもの（CF_UNICODETEXT）。</summary>
    public string? Text { get; init; }

    /// <summary>そのほかの形（PNG、HTML Format、URL、自分の印など）。</summary>
    public IReadOnlyList<OutgoingFormat> Formats { get; init; } = [];

    /// <summary>渡せないとき・注意があるときの説明。</summary>
    public string? Note { get; init; }

    /// <summary>渡すために書き出したファイル（画像のとき）。</summary>
    public string? ExportedFile { get; init; }

    /// <summary>リンクとしても渡せる（デスクトップに落とすとショートカットになる）。</summary>
    public bool AllowLink { get; init; }

    public bool IsEmpty => Files.Count == 0 && Text is null;
}

/// <summary>しまったものを、ドラッグで渡す形に変える。</summary>
public static class DragPlanner
{
    /// <summary>
    /// 画像は名前の付いた PNG ファイルとして渡す（チャットのアプリでは添付に、エクスプローラーではファイルのコピーに、
    /// Word や OneNote では画像になる）。ブラウザーで開いた画面に画像だけを落としても受け取ってもらえないため。
    /// テキストは文字（と、しまってある書式）、ファイルはそのファイル、リンクは文字と URL。
    /// </summary>
    public static DragPlan Plan(ClipStore store, Clip clip, Func<string, bool>? pathExists = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clip);
        pathExists ??= p => File.Exists(p) || Directory.Exists(p);
        var marker = OutgoingFormat.Named(FormatName.HikidanaMarker, PastePlanner.Marker(clip.Id));

        if (clip.IsImage)
        {
            var png = store.ReadPng(clip);
            if (png is null)
            {
                return new DragPlan { Note = "画像のファイルが見つかりませんでした。" };
            }

            var path = store.ExportImageFile(clip);
            return new DragPlan
            {
                Files = [path],
                Formats = [OutgoingFormat.Named(FormatName.Png, png), marker],
                ExportedFile = path,
            };
        }

        if (clip.Kind == ClipKind.Files)
        {
            var existing = clip.Files.Where(pathExists).ToList();
            if (existing.Count == 0)
            {
                return new DragPlan { Note = "ファイルが見つかりません。移動か削除をされたようです。" };
            }

            return new DragPlan
            {
                Files = existing,
                Formats = [marker],
                Note = existing.Count < clip.Files.Count ? $"見つからないファイルが {clip.Files.Count - existing.Count} 個あったので、残りを渡しました。" : null,
            };
        }

        var text = store.ReadText(clip) ?? clip.Preview;
        var formats = new List<OutgoingFormat>();
        foreach (var (name, standardId, data) in store.ReadPreserved(clip))
        {
            // CF_ の番号の形は、ドラッグの仕組みでは名前で渡せないので、登録した名前の形だけにする
            if (standardId == 0)
            {
                formats.Add(new OutgoingFormat(name, 0, data));
            }
        }

        var isLink = clip.Kind == ClipKind.Link && Uri.TryCreate(text.Trim(), UriKind.Absolute, out _);
        if (isLink)
        {
            var url = text.Trim();
            formats.Add(OutgoingFormat.Named(FormatName.UrlW, Utf16z(url)));
            formats.Add(OutgoingFormat.Named(FormatName.Url, [.. Encoding.ASCII.GetBytes(url), 0]));
        }

        formats.Add(marker);
        return new DragPlan { Text = text, Formats = formats, AllowLink = isLink };
    }

    private static byte[] Utf16z(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return bytes;
    }
}
