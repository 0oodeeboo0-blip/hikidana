using System.Runtime.InteropServices;
using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Storage;

namespace Hikidana.Core.Paste;

/// <summary>貼り付け先の種類。</summary>
public enum PasteTarget
{
    /// <summary>ふつうのアプリ（ChatGPT・Claude・ブラウザー・Office など）。</summary>
    App,

    /// <summary>エクスプローラーやデスクトップ（ファイルしか受け取れない）。</summary>
    FileManager,

    /// <summary>ターミナル（テキストしか受け取れない）。</summary>
    Terminal,
}

/// <summary>貼り付け方。</summary>
public enum PasteMode
{
    /// <summary>しまったときの形のまま。</summary>
    Normal,

    /// <summary>書式なしのテキストで（ファイルならパスのテキストで）。</summary>
    PlainText,

    /// <summary>ファイルとして（画像なら名前付きの PNG ファイル）。</summary>
    AsFile,
}

/// <summary>クリップボードに置く 1 つの形。</summary>
/// <param name="Name">形の名前（登録して使う。CF_ の形なら「CF_UNICODETEXT」のような名前）。</param>
/// <param name="StandardId">CF_ の形の番号。登録する形なら 0。</param>
/// <param name="Data">中身。</param>
public sealed record OutgoingFormat(string Name, uint StandardId, byte[] Data)
{
    public static OutgoingFormat Standard(uint id, byte[] data) => new(StandardFormat.NameOf(id), id, data);

    public static OutgoingFormat Named(string name, byte[] data) => new(name, 0, data);

    public override string ToString() => $"{Name}（{Data.Length} バイト）";
}

/// <summary>貼り付けの段取り。クリップボードに置く形と、Ctrl+V を送るかどうか。</summary>
public sealed class PastePlan
{
    public required IReadOnlyList<OutgoingFormat> Formats { get; init; }

    /// <summary>Ctrl+V を送る。false なら、置くだけ（コピーだけ）にして <see cref="Note"/> を知らせる。</summary>
    public bool SendKeys { get; init; } = true;

    /// <summary>貼れないとき・注意があるときの説明。</summary>
    public string? Note { get; init; }

    /// <summary>貼るために書き出したファイル（画像をファイルとして渡すとき）。</summary>
    public string? ExportedFile { get; init; }

    /// <summary>置くものが無い（ファイルが見つからないなど）。</summary>
    public bool IsEmpty => Formats.Count == 0;
}

/// <summary>しまったものを、貼り付け先と貼り付け方に合わせたクリップボードの形に変える。</summary>
public static class PastePlanner
{
    /// <summary>自分で書いた印の中身。プロセスの番号としまったものの ID。</summary>
    public static byte[] Marker(string clipId) => Encoding.UTF8.GetBytes($"{Environment.ProcessId}:{clipId}");

    public static PastePlan Plan(ClipStore store, Clip clip, PasteTarget target, PasteMode mode, Func<string, bool>? pathExists = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clip);
        pathExists ??= p => File.Exists(p) || Directory.Exists(p);
        var marker = OutgoingFormat.Named(FormatName.HikidanaMarker, Marker(clip.Id));

        if (clip.IsImage)
        {
            var png = store.ReadPng(clip);
            if (png is null)
            {
                return new PastePlan { Formats = [], SendKeys = false, Note = "画像のファイルが見つかりませんでした。" };
            }

            if (target == PasteTarget.FileManager || mode == PasteMode.AsFile)
            {
                var path = store.ExportImageFile(clip);
                return new PastePlan
                {
                    Formats = [.. FileFormats([path]), marker],
                    ExportedFile = path,
                };
            }

            // ふつうのアプリには、画像の形だけを置く（Win+Shift+S で撮った直後と同じ。ファイルの参照は混ぜない）
            var image = PngCodec.Decode(png);
            var formats = new List<OutgoingFormat>
            {
                OutgoingFormat.Named(FormatName.Png, png),
                OutgoingFormat.Standard(StandardFormat.DibV5, DibCodec.EncodeV5(image)),
                OutgoingFormat.Standard(StandardFormat.Dib, DibCodec.EncodeDib(image)),
                marker,
            };
            if (target == PasteTarget.Terminal)
            {
                return new PastePlan { Formats = formats, SendKeys = false, Note = "ターミナルには画像を貼れません。コピーは済んでいます。" };
            }

            return new PastePlan { Formats = formats };
        }

        if (clip.Kind == ClipKind.Files)
        {
            var existing = clip.Files.Where(pathExists).ToList();
            if (existing.Count == 0)
            {
                return new PastePlan { Formats = [], SendKeys = false, Note = "ファイルが見つかりません。移動か削除をされたようです。" };
            }

            if (mode == PasteMode.PlainText || target == PasteTarget.Terminal)
            {
                return new PastePlan { Formats = [UnicodeText(string.Join("\r\n", existing)), marker] };
            }

            var note = existing.Count < clip.Files.Count ? $"見つからないファイルが {clip.Files.Count - existing.Count} 個あったので、残りを貼り付けました。" : null;
            return new PastePlan { Formats = [.. FileFormats(existing), marker], Note = note };
        }

        // テキストの種類
        var text = store.ReadText(clip) ?? clip.Preview;
        var result = new List<OutgoingFormat> { UnicodeText(text) };
        if (mode == PasteMode.Normal && target == PasteTarget.App)
        {
            foreach (var (name, standardId, data) in store.ReadPreserved(clip))
            {
                result.Add(new OutgoingFormat(name, standardId, data));
            }
        }

        result.Add(marker);
        if (target == PasteTarget.FileManager)
        {
            return new PastePlan { Formats = result, SendKeys = false, Note = "エクスプローラーにはテキストを貼れません。コピーは済んでいます。" };
        }

        return new PastePlan { Formats = result };
    }

    /// <summary>CF_UNICODETEXT の中身（UTF-16、末尾に NUL）。</summary>
    public static OutgoingFormat UnicodeText(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return OutgoingFormat.Standard(StandardFormat.UnicodeText, bytes);
    }

    private static IEnumerable<OutgoingFormat> FileFormats(IReadOnlyList<string> paths)
    {
        yield return OutgoingFormat.Standard(StandardFormat.HDrop, DropFiles.Build(paths));
        yield return OutgoingFormat.Named(FormatName.PreferredDropEffect, DropFiles.DropEffect());
        if (paths.Count == 1)
        {
            var name = paths[0] + "\0";
            var bytes = new byte[name.Length * 2];
            MemoryMarshal.AsBytes(name.AsSpan()).CopyTo(bytes);
            yield return OutgoingFormat.Named(FormatName.FileNameW, bytes);
        }
    }
}
