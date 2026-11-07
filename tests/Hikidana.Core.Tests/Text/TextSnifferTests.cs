using Hikidana.Core.Text;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Text;

public class TextSnifferTests
{
    [Fact]
    public void UrlAloneIsLink()
    {
        Assert.True(TextSniffer.IsSingleUrl("https://learn.microsoft.com/ja-jp/windows/apps/design/", out var uri));
        Assert.Equal("learn.microsoft.com", uri!.Host);
    }

    [Fact]
    public void UrlWithSurroundingSpacesIsLink()
    {
        Assert.True(TextSniffer.IsSingleUrl("  https://example.com/a?b=1\r\n", out _));
    }

    [Fact]
    public void UrlInsideSentenceIsNotLink()
    {
        Assert.False(TextSniffer.IsSingleUrl("詳しくは https://example.com を見てください", out _));
        Assert.False(TextSniffer.IsSingleUrl("see https://example.com", out _));
    }

    [Fact]
    public void OnlyWebSchemesAreLinks()
    {
        Assert.False(TextSniffer.IsSingleUrl("ftp://example.com/file", out _));
        Assert.False(TextSniffer.IsSingleUrl("file:///C:/Users/a.txt", out _));
        Assert.False(TextSniffer.IsSingleUrl("mailto:someone@example.com", out _));
        Assert.False(TextSniffer.IsSingleUrl("http://", out _));
        Assert.False(TextSniffer.IsSingleUrl("https://intranet", out _));
        Assert.True(TextSniffer.IsSingleUrl("http://localhost:5000/", out _));
    }

    [Fact]
    public void HexColors()
    {
        Assert.True(TextSniffer.TryParseColor("#0B6E78", out var c));
        Assert.Equal(new ColorValue(11, 110, 120), c);
        Assert.Equal("#0B6E78", c.Hex);
        Assert.Equal("rgb(11, 110, 120)", c.Rgb);

        Assert.True(TextSniffer.TryParseColor("#fff", out var white));
        Assert.Equal(new ColorValue(255, 255, 255), white);

        Assert.True(TextSniffer.TryParseColor(" #11223380 ", out var half));
        Assert.Equal((byte)0x80, half.A);
        Assert.Equal("#11223380", half.Hex);
    }

    [Fact]
    public void RgbColors()
    {
        Assert.True(TextSniffer.TryParseColor("rgb(11, 110, 120)", out var c));
        Assert.Equal(new ColorValue(11, 110, 120), c);

        Assert.True(TextSniffer.TryParseColor("RGB(0 0 0)", out var black));
        Assert.Equal(new ColorValue(0, 0, 0), black);

        Assert.True(TextSniffer.TryParseColor("rgba(255, 0, 0, 0.5)", out var red));
        Assert.Equal((byte)128, red.A);
        Assert.Equal("rgba(255, 0, 0, 0.5)", red.Rgb);
    }

    [Fact]
    public void NotColors()
    {
        Assert.False(TextSniffer.TryParseColor("#12345", out _));
        Assert.False(TextSniffer.TryParseColor("0B6E78", out _));
        Assert.False(TextSniffer.TryParseColor("rgb(300, 0, 0)", out _));
        Assert.False(TextSniffer.TryParseColor("色は #0B6E78 です", out _));
        Assert.False(TextSniffer.TryParseColor("", out _));
    }

    [Fact]
    public void PowerShellPipelineIsCode()
    {
        Assert.True(TextSniffer.LooksLikeCode("Get-ChildItem -Recurse -Filter *.png |\n  Measure-Object Length -Sum"));
    }

    [Fact]
    public void CSharpIsCode()
    {
        const string code = "public static int Add(int a, int b)\n{\n    return a + b;\n}";
        Assert.True(TextSniffer.LooksLikeCode(code));
    }

    [Fact]
    public void JsonIsCode()
    {
        Assert.True(TextSniffer.LooksLikeCode("{\n  \"name\": \"hikidana\",\n  \"version\": 1\n}"));
    }

    [Fact]
    public void JapaneseSentencesAreNotCode()
    {
        Assert.False(TextSniffer.LooksLikeCode("明日の打ち合わせは 10:00 から第 2 会議室です。資料は共有フォルダーに入れておきました。"));
        Assert.False(TextSniffer.LooksLikeCode("いつもお世話になっております。\n株式会社サンプルの山田です。"));
    }

    [Fact]
    public void EnglishSentenceIsNotCode()
    {
        Assert.False(TextSniffer.LooksLikeCode("Thanks for the update. Let's meet tomorrow at 10 and go through the numbers."));
    }

    [Fact]
    public void OneSignalIsEnoughFromCodeEditor()
    {
        Assert.False(TextSniffer.LooksLikeCode("winget install Microsoft.DotNet.SDK.10"));
        Assert.True(TextSniffer.LooksLikeCode("winget install Microsoft.DotNet.SDK.10", fromCodeEditor: true));
    }
}
