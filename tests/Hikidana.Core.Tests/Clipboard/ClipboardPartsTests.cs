using System.Buffers.Binary;
using System.Text;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Text;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Clipboard;

public class ClipboardPartsTests
{
    [Fact]
    public void DropFilesRoundTripWithJapaneseNames()
    {
        string[] paths = [@"C:\Users\山田\Desktop\見積書_2026-11.xlsx", @"D:\写真\旅行"];
        var data = DropFiles.Build(paths);
        Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(data));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)), "fWide");
        Assert.SequenceEqual(paths, DropFiles.Parse(data));
    }

    [Fact]
    public void DropFilesReadsAnsiLists()
    {
        var body = Encoding.ASCII.GetBytes("C:\\a.txt\0C:\\b.txt\0\0");
        var data = new byte[20 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data, 20);
        body.CopyTo(data, 20);
        Assert.SequenceEqual(new[] { @"C:\a.txt", @"C:\b.txt" }, DropFiles.Parse(data));
    }

    [Fact]
    public void BrokenDropFilesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => DropFiles.Parse(new byte[8]));
        var bad = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(bad, 100);
        Assert.Throws<InvalidDataException>(() => DropFiles.Parse(bad));
    }

    [Fact]
    public void HtmlFormatRoundTrip()
    {
        var data = HtmlClipboard.Build("<b>締め切り</b>は 11/15", "https://example.com/page");
        Assert.True(HtmlClipboard.TryGetFragment(data, out var fragment, out var url));
        Assert.Equal("<b>締め切り</b>は 11/15", fragment);
        Assert.Equal("https://example.com/page", url);
    }

    [Fact]
    public void HtmlFormatWithoutOffsetsFallsBackToMarkers()
    {
        var data = Encoding.UTF8.GetBytes("Version:0.9\r\n<html><body><!--StartFragment--><i>x</i><!--EndFragment--></body></html>");
        Assert.True(HtmlClipboard.TryGetFragment(data, out var fragment, out _));
        Assert.Equal("<i>x</i>", fragment);
    }

    [Fact]
    public void SingleImageFragments()
    {
        Assert.True(HtmlClipboard.IsSingleImage("<img src=\"https://example.com/a.png\" alt=\"写真\">"));
        Assert.True(HtmlClipboard.IsSingleImage("<a href=\"https://example.com\"><img src=\"a.png\"/></a>"));
        Assert.True(HtmlClipboard.IsSingleImage("<meta charset='utf-8'><p><img src='a.png'></p>\r\n"));
        Assert.False(HtmlClipboard.IsSingleImage("<p>写真です <img src='a.png'></p>"));
        Assert.False(HtmlClipboard.IsSingleImage("<img src='a.png'><img src='b.png'>"));
        Assert.False(HtmlClipboard.IsSingleImage("<p>文字だけ</p>"));
    }

    [Fact]
    public void PrivacyMarkers()
    {
        static byte[]? None(string _) => null;
        Assert.Equal(SkipReason.None, ClipboardPrivacy.Check(["CF_UNICODETEXT"], None));
        Assert.Equal(SkipReason.Empty, ClipboardPrivacy.Check([], None));
        Assert.Equal(SkipReason.OwnWrite, ClipboardPrivacy.Check(["CF_UNICODETEXT", FormatName.HikidanaMarker], None));
        Assert.Equal(SkipReason.MarkedPrivate, ClipboardPrivacy.Check(["CF_UNICODETEXT", FormatName.ExcludeFromMonitor], None));
        Assert.Equal(SkipReason.ViewerIgnore, ClipboardPrivacy.Check(["CF_UNICODETEXT", FormatName.ViewerIgnore], None));
        Assert.Equal(SkipReason.ExcludedApp, ClipboardPrivacy.Check(["CF_UNICODETEXT"], None, "KeePass.exe", ["keepass.exe"]));
    }

    [Fact]
    public void CanIncludeInHistoryZeroMeansSkip()
    {
        string[] names = ["CF_UNICODETEXT", FormatName.CanIncludeInHistory];
        Assert.Equal(SkipReason.NotForHistory, ClipboardPrivacy.Check(names, _ => new byte[4]));
        Assert.Equal(SkipReason.None, ClipboardPrivacy.Check(names, _ => [1, 0, 0, 0]));
    }

    [Fact]
    public void ExcelTableText()
    {
        Assert.True(TableText.TryParse("品名\t数量\r\nノート\t12\r\n", out var rows, out var total, out var columns));
        Assert.Equal(2, total);
        Assert.Equal(2, columns);
        Assert.Equal("ノート", rows[1][0]);
        Assert.Equal("12", rows[1][1]);
    }

    [Fact]
    public void QuotedCellsKeepNewlinesAndTabs()
    {
        Assert.True(TableText.TryParse("\"1 行目\n2 行目\"\t\"a\tb\"\r\n\"say \"\"hi\"\"\"\tx", out var rows, out _, out _));
        Assert.Equal("1 行目\n2 行目", rows[0][0]);
        Assert.Equal("a\tb", rows[0][1]);
        Assert.Equal("say \"hi\"", rows[1][0]);
    }

    [Fact]
    public void SingleCellIsNotTable()
    {
        Assert.False(TableText.TryParse("123\r\n", out _, out _, out _));
        Assert.True(TableText.TryParse("1\r\n2\r\n3\r\n", out var column, out _, out _), "1 列でも行が 2 つ以上あれば表。");
        Assert.Equal(3, column.Count);
    }
}
