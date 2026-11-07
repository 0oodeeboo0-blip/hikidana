using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Storage;
using Hikidana.Core.Tests.Imaging;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Storage;

public sealed class ClipStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hikidana-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTime time = new(new DateTimeOffset(2026, 11, 7, 18, 12, 5, TimeSpan.Zero));

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void TextSurvivesReload()
    {
        var store = NewStore();
        var clip = store.Add(Text("明日の打ち合わせは 10:00 から"))!;
        var again = NewStore();
        Assert.Equal(1, again.Items.Count);
        Assert.Equal(clip.Id, again.Items[0].Id);
        Assert.Equal("明日の打ち合わせは 10:00 から", again.Items[0].Preview);
        Assert.Equal("明日の打ち合わせは 10:00 から", again.ReadText(again.Items[0]));
    }

    [Fact]
    public void ScreenshotGetsImageAndThumbnail()
    {
        var store = NewStore();
        var image = TestImages.Opaque(1920, 1080);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = image, SourceApp = "SnippingTool.exe" })!;
        Assert.Equal(1920, clip.ImageWidth);
        Assert.Equal(1080, clip.ImageHeight);
        Assert.True(clip.HasThumbnail);
        Assert.True(image.SamePixels(PngCodec.Decode(store.ReadPng(clip)!)));
        var thumb = PngCodec.Decode(File.ReadAllBytes(store.ThumbnailPath(clip)!));
        Assert.Equal(768, thumb.Width);
        Assert.Equal(432, thumb.Height);
    }

    [Fact]
    public void SmallImageUsesItselfAsThumbnail()
    {
        var store = NewStore();
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = TestImages.Opaque(200, 100) })!;
        Assert.False(clip.HasThumbnail);
        Assert.Equal(store.ImagePath(clip), store.ThumbnailPath(clip));
    }

    [Fact]
    public void OriginalPngIsStoredAsIs()
    {
        var store = NewStore();
        var image = TestImages.WithAlpha(50, 40);
        var png = PngCodec.Encode(image);
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = image, OriginalPng = png })!;
        Assert.SequenceEqual(png, store.ReadPng(clip)!);
    }

    [Fact]
    public void SameTextMovesToTopKeepingPinAndFirstTime()
    {
        var store = NewStore();
        var a = store.Add(Text("A"))!;
        store.SetPinned(a.Id, true);
        time.Advance(TimeSpan.FromMinutes(1));
        store.Add(Text("B"));
        time.Advance(TimeSpan.FromMinutes(1));
        var again = store.Add(new ClipDraft { Kind = ClipKind.RichText, Text = "A\r\n", Preserved = [new ClipboardFormatData(0xC123, FormatName.Html, [1, 2])] })!;
        Assert.Equal(2, store.Items.Count);
        Assert.Equal(again.Id, store.Items[0].Id);
        Assert.Equal(ClipKind.RichText, store.Items[0].Kind, "新しい方（書式つき）に入れ替わる。");
        Assert.True(store.Items[0].Pinned);
        Assert.Equal(a.CreatedAt, store.Items[0].CreatedAt);
        Assert.False(Directory.Exists(Path.Combine(root, "clips", a.Id)), "古い中身は消える。");
    }

    [Fact]
    public void SameImageMovesToTop()
    {
        var store = NewStore();
        var image = TestImages.Opaque(64, 64);
        var first = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = image })!;
        time.Advance(TimeSpan.FromMinutes(1));
        store.Add(Text("あいだ"));
        time.Advance(TimeSpan.FromMinutes(1));
        var copy = new PixelImage(64, 64, (byte[])image.Bgra.Clone());
        var second = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = copy, OriginalPng = PngCodec.Encode(copy, System.IO.Compression.CompressionLevel.Fastest) })!;
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, store.Items.Count);
        Assert.Equal(first.Id, store.Items[0].Id);
        Assert.Equal(ClipKind.Screenshot, store.Items[0].Kind);
    }

    [Fact]
    public void SameFilesIgnoreCase()
    {
        var store = NewStore();
        store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [@"C:\a\B.txt", @"C:\c.txt"] });
        store.Add(new ClipDraft { Kind = ClipKind.Files, Files = [@"c:\C.TXT", @"c:\a\b.txt"] });
        Assert.Equal(1, store.Items.Count);
    }

    [Fact]
    public void OldestUnpinnedGoFirst()
    {
        var store = NewStore(new ClipStoreOptions { MaxItems = 3 });
        var pinned = store.Add(Text("ピン留め"))!;
        store.SetPinned(pinned.Id, true);
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            ids.Add(store.Add(Text($"テキスト {i}"))!.Id);
        }

        var left = store.Items.Select(c => c.Id).ToList();
        Assert.Equal(4, left.Count);
        Assert.True(left.Contains(pinned.Id));
        Assert.SequenceEqual(new[] { ids[4], ids[3], ids[2] }, left.Where(id => id != pinned.Id));
        Assert.False(Directory.Exists(Path.Combine(root, "clips", ids[0])));
    }

    [Fact]
    public void OldItemsExpire()
    {
        var store = NewStore(new ClipStoreOptions { MaxAge = TimeSpan.FromDays(7) });
        store.Add(Text("古い"));
        time.Advance(TimeSpan.FromDays(8));
        store.Add(Text("新しい"));
        Assert.SequenceEqual(new[] { "新しい" }, store.Items.Select(c => c.Preview));
    }

    [Fact]
    public void DeleteCanBeUndone()
    {
        var store = NewStore();
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Image, Image = TestImages.Opaque(20, 20) })!;
        var removed = store.Delete(clip.Id)!;
        Assert.Equal(0, store.Items.Count);
        Assert.True(store.Restore(removed));
        Assert.Equal(1, store.Items.Count);
        Assert.NotNull(store.ReadPng(store.Items[0]));
    }

    [Fact]
    public void ClearKeepsPinned()
    {
        var store = NewStore();
        var keep = store.Add(Text("残す"))!;
        store.SetPinned(keep.Id, true);
        store.Add(Text("消す"));
        store.Clear(keepPinned: true);
        Assert.SequenceEqual(new[] { keep.Id }, store.Items.Select(c => c.Id));
        store.Clear(keepPinned: false);
        Assert.Equal(0, store.Items.Count);
    }

    [Fact]
    public void PreservedFormatsRoundTrip()
    {
        var store = NewStore();
        var clip = store.Add(new ClipDraft
        {
            Kind = ClipKind.Table,
            Text = "a\tb\r\nc\td",
            TableRows = [["a", "b"], ["c", "d"]],
            Preserved =
            [
                new ClipboardFormatData(0xC0F1, FormatName.Biff12, [1, 2, 3]),
                new ClipboardFormatData(StandardFormat.Sylk, "CF_SYLK", [4, 5]),
            ],
        })!;
        Assert.Equal(2, clip.TableRowCount);
        Assert.Equal(2, clip.TableColumnCount);
        var formats = NewStore().ReadPreserved(clip);
        Assert.Equal(2, formats.Count);
        Assert.Equal(FormatName.Biff12, formats[0].Name);
        Assert.Equal(0u, formats[0].StandardId, "登録された形の番号は覚えない。");
        Assert.SequenceEqual(new byte[] { 1, 2, 3 }, formats[0].Data);
        Assert.Equal(StandardFormat.Sylk, formats[1].StandardId);
    }

    [Fact]
    public void ExportedImageHasWindowsStyleName()
    {
        var store = NewStore();
        var clip = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = TestImages.Opaque(30, 30) })!;
        var path = store.ExportImageFile(clip);
        Assert.Equal("スクリーンショット 2026-11-07 181205.png", Path.GetFileName(path));
        Assert.Equal(path, store.ExportImageFile(clip), "同じ画像なら同じファイルを使う。");

        var other = store.Add(new ClipDraft { Kind = ClipKind.Screenshot, Image = TestImages.WithAlpha(30, 30) })!;
        Assert.Equal("スクリーンショット 2026-11-07 181205 (2).png", Path.GetFileName(store.ExportImageFile(other)));
    }

    [Fact]
    public void BrokenIndexIsSetAside()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "clips.json"), "{ これは壊れています");
        var store = NewStore();
        Assert.Equal(0, store.Items.Count);
        Assert.True(Directory.GetFiles(root, "clips.broken-*.json").Length == 1);
    }

    [Fact]
    public void LeftoverFoldersAreRemovedOnLoad()
    {
        var store = NewStore();
        store.Add(Text("ある"));
        var stray = Path.Combine(root, "clips", "20200101-000000-dead");
        Directory.CreateDirectory(stray);
        NewStore();
        Assert.False(Directory.Exists(stray));
    }

    [Fact]
    public void TooLongTextIsNotStored()
    {
        var store = NewStore(new ClipStoreOptions { MaxTextLength = 10 });
        Assert.Null(store.Add(Text(new string('あ', 11))));
        Assert.Equal(0, store.Items.Count);
    }

    private ClipStore NewStore(ClipStoreOptions? options = null)
    {
        var store = new ClipStore(root, options, time);
        store.Load();
        return store;
    }

    private static ClipDraft Text(string text) => new() { Kind = ClipKind.Text, Text = text, SourceApp = "Notepad.exe" };

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
