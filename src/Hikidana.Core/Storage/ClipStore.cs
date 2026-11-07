using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Text;

namespace Hikidana.Core.Storage;

/// <summary>棚の決まり（件数や大きさの上限）。</summary>
public sealed class ClipStoreOptions
{
    /// <summary>しまっておく数の上限（ピン留めは数えない）。</summary>
    public int MaxItems { get; set; } = 300;

    /// <summary>しまっておく期間の上限（ピン留めは消さない）。null なら無期限。</summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>縮小画像の長い辺の画素の数（棚のカードは表示倍率 200% でもこれで足りる）。</summary>
    public int ThumbnailSize { get; set; } = 768;

    /// <summary>索引に書くテキストの長さ（カードと検索に使う）。</summary>
    public int PreviewLength { get; set; } = 500;

    /// <summary>しまうテキストの上限（UTF-16 の文字数）。これより長いものはしまわない。</summary>
    public int MaxTextLength { get; set; } = 10_000_000;
}

/// <summary>
/// 棚の中身を %LOCALAPPDATA%\Hikidana に保存する。索引は clips.json、中身は clips\{id}\ に置く。
/// どのスレッドから呼んでもよい。
/// </summary>
public sealed class ClipStore
{
    private const string IndexFile = "clips.json";
    private const string ClipsFolder = "clips";
    private const string TrashFolder = "trash";
    private const string ExportFolder = "export";
    private const string ImageFile = "image.png";
    private const string ThumbnailFile = "thumb.png";
    private const string TextFile = "text.txt";
    private const string FormatsFile = "formats.json";

    private readonly object gate = new();
    private readonly TimeProvider time;
    private List<Clip> items = [];

    public ClipStore(string root, ClipStoreOptions? options = null, TimeProvider? time = null)
    {
        Root = root;
        Options = options ?? new ClipStoreOptions();
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>既定の保存先（%LOCALAPPDATA%\Hikidana）。</summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hikidana");

    public string Root { get; }

    public ClipStoreOptions Options { get; }

    /// <summary>中身が変わった（足した・消した・ピン留めした…）。どのスレッドからも呼ばれる。</summary>
    public event EventHandler? Changed;

    /// <summary>しまってあるもの。新しい順（ピン留めも混ざる。画面で分けて見せる）。</summary>
    public IReadOnlyList<Clip> Items
    {
        get
        {
            lock (gate)
            {
                return items.ToList();
            }
        }
    }

    private string ClipsDir => Path.Combine(Root, ClipsFolder);

    /// <summary>保存してある索引を読む。壊れていれば脇によけて、空から始める。</summary>
    public void Load()
    {
        lock (gate)
        {
            Directory.CreateDirectory(ClipsDir);
            var path = Path.Combine(Root, IndexFile);
            var loaded = new List<Clip>();
            if (File.Exists(path))
            {
                try
                {
                    var index = JsonSerializer.Deserialize(File.ReadAllBytes(path), StoreJsonContext.Default.ClipIndex);
                    loaded = index?.Items ?? [];
                }
                catch (JsonException)
                {
                    var broken = Path.Combine(Root, $"clips.broken-{time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                    File.Move(path, broken, overwrite: true);
                }
            }

            // 中身のフォルダーが無いものは捨てる（ファイルの種類はフォルダーを持たない）
            items = loaded
                .Where(c => c.Kind == ClipKind.Files || Directory.Exists(Path.Combine(ClipsDir, c.Id)))
                .OrderByDescending(c => c.UpdatedAt)
                .ToList();

            // 索引に無いフォルダー（前に消しそこねたもの）を消す
            var known = items.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.GetDirectories(ClipsDir))
            {
                if (!known.Contains(Path.GetFileName(dir)))
                {
                    TryDeleteDirectory(dir);
                }
            }

            TryDeleteDirectory(Path.Combine(Root, TrashFolder));
            CleanExports(TimeSpan.FromDays(1));
            EnforceLimits();
            SaveIndex();
        }

        RaiseChanged();
    }

    /// <summary>
    /// しまう。同じもの（同じテキスト・同じ画素の画像・同じファイル）がもうあれば、新しく足さずにいちばん上へ上げる。
    /// しまったもの（か、上げたもの）を返す。テキストが長すぎるなど、しまえないときは null。
    /// </summary>
    public Clip? Add(ClipDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Text is { } t && t.Length > Options.MaxTextLength)
        {
            return null;
        }

        var fingerprint = FingerprintOf(draft);
        var now = time.GetLocalNow();

        // 同じ画像・同じファイルなら、中身はそのままで上へ上げるだけ
        if (draft.Kind is ClipKind.Image or ClipKind.Screenshot or ClipKind.Files)
        {
            Clip? same;
            lock (gate)
            {
                same = items.FirstOrDefault(c => c.Fingerprint == fingerprint);
                if (same is not null)
                {
                    same.UpdatedAt = now;
                    same.SourceApp = draft.SourceApp ?? same.SourceApp;
                    if (draft.Kind == ClipKind.Screenshot)
                    {
                        same.Kind = ClipKind.Screenshot;
                    }

                    Sort();
                    SaveIndex();
                }
            }

            if (same is not null)
            {
                RaiseChanged();
                return same;
            }
        }

        // 新しく作る（重い画像の処理は鍵の外で）
        var clip = new Clip
        {
            Id = NewId(now),
            Kind = draft.Kind,
            CreatedAt = now,
            UpdatedAt = now,
            SourceApp = draft.SourceApp,
            SourceUrl = draft.SourceUrl,
            Fingerprint = fingerprint,
        };
        var dir = Path.Combine(ClipsDir, clip.Id);
        try
        {
            WriteContent(clip, draft, dir);
        }
        catch
        {
            TryDeleteDirectory(dir);
            throw;
        }

        Clip? replaced;
        lock (gate)
        {
            // テキストは、同じテキストでも書式が違うことがあるので、新しい方に入れ替える（ピン留めと最初の日時は引き継ぐ）
            replaced = items.FirstOrDefault(c => c.Fingerprint == fingerprint);
            if (replaced is not null)
            {
                items.Remove(replaced);
                clip.Pinned = replaced.Pinned;
                clip.CreatedAt = replaced.CreatedAt;
            }

            items.Insert(0, clip);
            EnforceLimits();
            SaveIndex();
        }

        if (replaced is not null && replaced.Kind != ClipKind.Files)
        {
            TryDeleteDirectory(Path.Combine(ClipsDir, replaced.Id));
        }

        RaiseChanged();
        return clip;
    }

    /// <summary>ピン留めする・外す。</summary>
    public void SetPinned(string id, bool pinned)
    {
        lock (gate)
        {
            var clip = items.FirstOrDefault(c => c.Id == id);
            if (clip is null || clip.Pinned == pinned)
            {
                return;
            }

            clip.Pinned = pinned;
            EnforceLimits();
            SaveIndex();
        }

        RaiseChanged();
    }

    /// <summary>並び順の日時を今にする（「貼り付けたら上へ」のとき）。</summary>
    public void Touch(string id)
    {
        lock (gate)
        {
            var clip = items.FirstOrDefault(c => c.Id == id);
            if (clip is null)
            {
                return;
            }

            clip.UpdatedAt = time.GetLocalNow();
            Sort();
            SaveIndex();
        }

        RaiseChanged();
    }

    /// <summary>消す。中身はしばらく脇によけておき、<see cref="Restore"/> で戻せる（次に起動したときに消える）。</summary>
    public Clip? Delete(string id)
    {
        Clip? clip;
        lock (gate)
        {
            clip = items.FirstOrDefault(c => c.Id == id);
            if (clip is null)
            {
                return null;
            }

            items.Remove(clip);
            var dir = Path.Combine(ClipsDir, clip.Id);
            if (Directory.Exists(dir))
            {
                var trash = Path.Combine(Root, TrashFolder);
                Directory.CreateDirectory(trash);
                var target = Path.Combine(trash, clip.Id);
                TryDeleteDirectory(target);
                try
                {
                    Directory.Move(dir, target);
                }
                catch (IOException)
                {
                    TryDeleteDirectory(dir);
                }
            }

            SaveIndex();
        }

        RaiseChanged();
        return clip;
    }

    /// <summary>消したものを戻す。中身がもう無ければ false。</summary>
    public bool Restore(Clip clip)
    {
        lock (gate)
        {
            if (items.Any(c => c.Id == clip.Id))
            {
                return true;
            }

            var trashed = Path.Combine(Root, TrashFolder, clip.Id);
            var dir = Path.Combine(ClipsDir, clip.Id);
            if (clip.Kind != ClipKind.Files)
            {
                if (!Directory.Exists(trashed))
                {
                    return false;
                }

                Directory.Move(trashed, dir);
            }

            items.Add(clip);
            Sort();
            EnforceLimits();
            SaveIndex();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>全部消す（ピン留めを残すかを選べる）。</summary>
    public void Clear(bool keepPinned = true)
    {
        List<Clip> removed;
        lock (gate)
        {
            removed = items.Where(c => !keepPinned || !c.Pinned).ToList();
            items = items.Where(c => keepPinned && c.Pinned).ToList();
            SaveIndex();
        }

        foreach (var clip in removed)
        {
            TryDeleteDirectory(Path.Combine(ClipsDir, clip.Id));
        }

        RaiseChanged();
    }

    public Clip? Find(string id)
    {
        lock (gate)
        {
            return items.FirstOrDefault(c => c.Id == id);
        }
    }

    /// <summary>テキストの全体。無ければ null。</summary>
    public string? ReadText(Clip clip)
    {
        var path = Path.Combine(ClipsDir, clip.Id, TextFile);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    /// <summary>画像のファイル（PNG）。画像でなければ null。</summary>
    public string? ImagePath(Clip clip) => clip.IsImage ? Path.Combine(ClipsDir, clip.Id, ImageFile) : null;

    /// <summary>縮小画像のファイル（無ければ元の画像）。画像でなければ null。</summary>
    public string? ThumbnailPath(Clip clip) =>
        !clip.IsImage ? null : Path.Combine(ClipsDir, clip.Id, clip.HasThumbnail ? ThumbnailFile : ImageFile);

    /// <summary>画像の PNG の中身。</summary>
    public byte[]? ReadPng(Clip clip) => ImagePath(clip) is { } path && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>貼り付けるときにそのまま戻す形（HTML・RTF・Excel の形など）。</summary>
    public IReadOnlyList<(string Name, uint StandardId, byte[] Data)> ReadPreserved(Clip clip)
    {
        var dir = Path.Combine(ClipsDir, clip.Id);
        var listPath = Path.Combine(dir, FormatsFile);
        if (!File.Exists(listPath))
        {
            return [];
        }

        var list = JsonSerializer.Deserialize(File.ReadAllBytes(listPath), StoreJsonContext.Default.ListStoredFormat) ?? [];
        var result = new List<(string, uint, byte[])>();
        foreach (var f in list)
        {
            var path = Path.Combine(dir, f.File);
            if (File.Exists(path))
            {
                result.Add((f.Name, f.StandardId, File.ReadAllBytes(path)));
            }
        }

        return result;
    }

    /// <summary>
    /// 画像を、名前の付いた PNG ファイルとして書き出す（エクスプローラーへ貼るときと「ファイルとして貼り付け」のとき）。
    /// 名前は Windows のスクリーンショットと同じ形（スクリーンショット 2026-11-07 181205.png）。
    /// </summary>
    public string ExportImageFile(Clip clip)
    {
        var source = ImagePath(clip) ?? throw new InvalidOperationException("画像ではありません。");
        var dir = Path.Combine(Root, ExportFolder);
        Directory.CreateDirectory(dir);
        var baseName = string.Create(CultureInfo.InvariantCulture, $"{(clip.Kind == ClipKind.Screenshot ? "スクリーンショット" : "画像")} {clip.CreatedAt:yyyy-MM-dd HHmmss}");
        var size = new FileInfo(source).Length;
        for (var n = 1; n < 1000; n++)
        {
            var name = n == 1 ? $"{baseName}.png" : $"{baseName} ({n}).png";
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length == size && File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(source)))
                {
                    return path;
                }

                continue;
            }

            File.Copy(source, path);
            return path;
        }

        throw new IOException("書き出すファイルの名前を決められませんでした。");
    }

    /// <summary>同じものかを見分ける指紋。テキストは改行の違いと末尾の空白を無視する。</summary>
    public static string FingerprintOf(ClipDraft draft)
    {
        switch (draft.Kind)
        {
            case ClipKind.Image or ClipKind.Screenshot when draft.Image is not null:
                return "i:" + draft.Image.Fingerprint();
            case ClipKind.Files:
                var files = string.Join("\n", draft.Files.Select(f => f.ToUpperInvariant()).Order(StringComparer.Ordinal));
                return "f:" + Hash(files);
            default:
                var text = (draft.Text ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
                return "t:" + Hash(text);
        }
    }

    private void WriteContent(Clip clip, ClipDraft draft, string dir)
    {
        Directory.CreateDirectory(dir);
        var text = draft.Text;
        if (text is not null)
        {
            clip.Preview = text.Length > Options.PreviewLength ? text[..Options.PreviewLength] : text;
            clip.TextLength = text.Length;
            if (draft.Kind is not (ClipKind.Image or ClipKind.Screenshot))
            {
                File.WriteAllText(Path.Combine(dir, TextFile), text, new UTF8Encoding(false));
            }
        }

        switch (draft.Kind)
        {
            case ClipKind.Image or ClipKind.Screenshot:
            {
                var image = draft.Image ?? throw new ArgumentException("画像がありません。", nameof(draft));
                var png = draft.OriginalPng ?? PngCodec.Encode(image);
                File.WriteAllBytes(Path.Combine(dir, ImageFile), png);
                clip.ImageWidth = image.Width;
                clip.ImageHeight = image.Height;
                clip.ImageBytes = png.Length;
                var thumb = ImageScaler.Fit(image, Options.ThumbnailSize, Options.ThumbnailSize);
                if (!ReferenceEquals(thumb, image))
                {
                    File.WriteAllBytes(Path.Combine(dir, ThumbnailFile), PngCodec.Encode(thumb, CompressionLevel.Fastest));
                    clip.HasThumbnail = true;
                }

                break;
            }

            case ClipKind.Files:
                clip.Files = draft.Files.ToList();
                clip.Preview = string.Join("\n", draft.Files.Select(Path.GetFileName));
                break;
            case ClipKind.Color:
                clip.ColorHex = draft.Color?.Hex;
                break;
            case ClipKind.Table when draft.TableRows is not null:
                clip.TableRows = draft.TableRows.Select(r => r.ToList()).ToList();
                if (TableText.TryParse(text, out _, out var rowCount, out var columnCount, maxRows: 1, maxColumns: 1))
                {
                    clip.TableRowCount = rowCount;
                    clip.TableColumnCount = columnCount;
                }

                break;
        }

        if (draft.Preserved.Count > 0)
        {
            var list = new List<StoredFormat>();
            for (var i = 0; i < draft.Preserved.Count; i++)
            {
                var format = draft.Preserved[i];
                var file = $"format{i}.bin";
                File.WriteAllBytes(Path.Combine(dir, file), format.Data);
                list.Add(new StoredFormat
                {
                    Name = format.Name,
                    StandardId = format.Id < 0xC000 ? format.Id : 0,
                    File = file,
                });
            }

            File.WriteAllBytes(Path.Combine(dir, FormatsFile), JsonSerializer.SerializeToUtf8Bytes(list, StoreJsonContext.Default.ListStoredFormat));
            clip.PreservedCount = list.Count;
        }
    }

    private void EnforceLimits()
    {
        var now = time.GetLocalNow();
        var unpinned = items.Where(c => !c.Pinned).OrderByDescending(c => c.UpdatedAt).ToList();
        var remove = new List<Clip>();
        if (unpinned.Count > Options.MaxItems)
        {
            remove.AddRange(unpinned.Skip(Options.MaxItems));
        }

        if (Options.MaxAge is { } maxAge)
        {
            remove.AddRange(unpinned.Where(c => now - c.UpdatedAt > maxAge));
        }

        foreach (var clip in remove.Distinct())
        {
            items.Remove(clip);
            TryDeleteDirectory(Path.Combine(ClipsDir, clip.Id));
        }
    }

    private void Sort() => items = items.OrderByDescending(c => c.UpdatedAt).ToList();

    private void SaveIndex()
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, IndexFile);
        var temp = path + ".tmp";
        var index = new ClipIndex { Items = items };
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(index, StoreJsonContext.Default.ClipIndex));
        File.Move(temp, path, overwrite: true);
    }

    private void CleanExports(TimeSpan olderThan)
    {
        var dir = Path.Combine(Root, ExportFolder);
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir))
        {
            try
            {
                if (time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(file) > olderThan)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // 使われていれば次の機会に
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string NewId(DateTimeOffset now)
    {
        Span<byte> random = stackalloc byte[2];
        RandomNumberGenerator.Fill(random);
        return string.Create(CultureInfo.InvariantCulture, $"{now:yyyyMMdd-HHmmss}-{Convert.ToHexString(random).ToLowerInvariant()}");
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 使われていれば、次に起動したときに消す
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // 鍵の外で呼ぶ（受け取る側が棚を読み直しても止まらないように）
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
