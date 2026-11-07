using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hikidana.Core.Clipboard;
using Hikidana.Core.Clips;
using Hikidana.Core.Imaging;
using Hikidana.Core.Storage;

namespace Hikidana.App.Demo;

/// <summary>
/// 見本の中身（--demo のとき）。画面写真を撮るときや、試すときに使う。本物の棚とは別の、一時フォルダーに置く。
/// 本物と同じ見分け方（ClipClassifier）を通してしまう。
/// </summary>
internal static class DemoData
{
    public static string Root => Path.Combine(Path.GetTempPath(), "Hikidana-demo");

    /// <summary>前の見本を消す。</summary>
    public static void Reset()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Fill(ClipStore store, DemoClock clock)
    {
        var now = DateTimeOffset.Now;
        void At(TimeSpan ago, ClipboardSnapshot snapshot, bool pin = false)
        {
            clock.Override = now - ago;
            if (ClipClassifier.Classify(snapshot) is { } draft && store.Add(draft) is { } clip && pin)
            {
                store.SetPinned(clip.Id, true);
            }
        }

        var files = Path.Combine(Root, "files");
        Directory.CreateDirectory(files);
        var report = Path.Combine(files, "見積書_2026年11月.pdf");
        var photo = Path.Combine(files, "打ち合わせのメモ.docx");
        File.WriteAllText(report, "見本");
        File.WriteAllText(photo, "見本");

        At(TimeSpan.FromDays(3), Text("住所: 東京都千代田区丸の内 1-1-1\n電話: 03-0000-0000", "msedge.exe"), pin: true);
        At(TimeSpan.FromHours(27), Text("#2B4C7E", "Code.exe"));
        At(TimeSpan.FromHours(6), Table("品名\t数量\t金額\r\nりんご\t3\t360\r\nみかん\t10\t480\r\nぶどう\t1\t650\r\n", "EXCEL.EXE"));
        At(TimeSpan.FromHours(3), Text("const total = items.reduce((sum, item) => sum + item.price, 0);\nconsole.log(`合計: ${total} 円`);", "Code.exe"));
        At(TimeSpan.FromMinutes(95), Files([report, photo]));
        At(TimeSpan.FromMinutes(52), Text("https://learn.microsoft.com/ja-jp/windows/apps/design/", "msedge.exe"));
        At(TimeSpan.FromMinutes(34), Image(Landscape(1200, 800), "msedge.exe"));
        At(TimeSpan.FromMinutes(12), Rich("<p><b>来週の予定</b>：月曜は定例、水曜は外出です。資料は金曜までにお送りします。</p>", "来週の予定：月曜は定例、水曜は外出です。資料は金曜までにお送りします。", "WINWORD.EXE"));
        At(TimeSpan.FromMinutes(4), Text("明日の打ち合わせは 10 時からです。会議室は 3 階の A です。", "ChatGPT.exe"));
        At(TimeSpan.FromSeconds(25), Image(ChatScreenshot(1600, 900), "SnippingTool.exe"));
        clock.Override = null;
    }

    private static ClipboardSnapshot Text(string text, string owner) =>
        new([Unicode(text)], owner);

    private static ClipboardSnapshot Rich(string fragment, string text, string owner) =>
        new([Unicode(text), new ClipboardFormatData(0, FormatName.Html, HtmlClipboard.Build(fragment))], owner);

    private static ClipboardSnapshot Table(string tsv, string owner) =>
        new([Unicode(tsv), new ClipboardFormatData(0, FormatName.Csv, Encoding.UTF8.GetBytes(tsv.Replace('\t', ',')))], owner);

    private static ClipboardSnapshot Files(IReadOnlyList<string> paths) =>
        new([new ClipboardFormatData(StandardFormat.HDrop, "CF_HDROP", DropFiles.Build(paths))], "explorer.exe");

    private static ClipboardSnapshot Image(PixelImage image, string owner) =>
        new([new ClipboardFormatData(0, FormatName.Png, PngCodec.Encode(image))], owner);

    private static ClipboardFormatData Unicode(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        MemoryMarshal.AsBytes(text.AsSpan()).CopyTo(bytes);
        return new ClipboardFormatData(StandardFormat.UnicodeText, "CF_UNICODETEXT", bytes);
    }

    /// <summary>チャットのアプリを撮ったような、見本のスクリーンショット。</summary>
    private static PixelImage ChatScreenshot(int width, int height) => Render(width, height, dc =>
    {
        dc.DrawRectangle(Solid(0xF7F7F8), null, new Rect(0, 0, width, height));
        dc.DrawRectangle(Solid(0xECECF1), null, new Rect(0, 0, 300, height));
        for (var i = 0; i < 9; i++)
        {
            dc.DrawRoundedRectangle(Solid(i == 1 ? 0xDADAE3u : 0xE3E3EAu), null, new Rect(20, 96 + (i * 52), 260 - (i % 3 * 40), 34), 8, 8);
        }

        DrawText(dc, "新しいチャット", 28, 34, 22, 0x202123, bold: true);
        dc.DrawRectangle(Brushes.White, null, new Rect(300, 0, width - 300, 72));
        dc.DrawRectangle(Solid(0xE5E5E5), null, new Rect(300, 72, width - 300, 1));
        DrawText(dc, "週末の旅行の計画", 340, 22, 24, 0x202123, bold: true);

        var left = 420.0;
        dc.DrawRoundedRectangle(Solid(0xE8F0FE), null, new Rect(width - 720, 120, 600, 64), 18, 18);
        DrawText(dc, "1 泊 2 日で、温泉に行ける所を教えて。", width - 696, 138, 22, 0x1F1F1F);
        dc.DrawEllipse(Solid(0x10A37F), null, new Point(left - 40, 240), 18, 18);
        DrawText(dc, "いくつか候補を挙げますね。", left, 224, 22, 0x1F1F1F);
        string[] lines = ["1. 箱根: 都心から近く、美術館も楽しめます。", "2. 草津: 湯畑の景色と、名物の湯もみ。", "3. 熱海: 海の近くで、食べ歩きもできます。"];
        for (var i = 0; i < lines.Length; i++)
        {
            DrawText(dc, lines[i], left, 280 + (i * 46), 22, 0x353740);
        }

        dc.DrawRoundedRectangle(Brushes.White, new Pen(Solid(0xD9D9E3), 1.5), new Rect(left - 20, height - 130, width - left - 100, 70), 22, 22);
        DrawText(dc, "メッセージを入力", left + 10, height - 108, 22, 0x8E8EA0);
    });

    /// <summary>写真のような、見本の画像（空と山と湖）。</summary>
    private static PixelImage Landscape(int width, int height) => Render(width, height, dc =>
    {
        var sky = new LinearGradientBrush(Color.FromRgb(0x5B, 0x8F, 0xD6), Color.FromRgb(0xF6, 0xC9, 0x9B), 90);
        dc.DrawRectangle(sky, null, new Rect(0, 0, width, height));
        dc.DrawEllipse(Solid(0xFFE7B0), null, new Point(width * 0.72, height * 0.38), 60, 60);
        dc.DrawGeometry(Solid(0x3E5C76), null, Mountains(width, height, 0.62, [0.0, 0.48, 0.18, 0.30, 0.42, 0.40, 0.65, 0.26, 0.85, 0.45, 1.0, 0.36]));
        dc.DrawGeometry(Solid(0x2B4459), null, Mountains(width, height, 0.70, [0.0, 0.58, 0.25, 0.50, 0.52, 0.62, 0.78, 0.52, 1.0, 0.60]));
        var lake = new LinearGradientBrush(Color.FromRgb(0x7F, 0xA7, 0xD9), Color.FromRgb(0x30, 0x4F, 0x73), 90);
        dc.DrawRectangle(lake, null, new Rect(0, height * 0.70, width, height * 0.30));
    });

    private static Geometry Mountains(int width, int height, double baseLine, double[] points)
    {
        var figure = new PathFigure { StartPoint = new Point(0, height * baseLine), IsClosed = true };
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            figure.Segments.Add(new LineSegment(new Point(width * points[i], height * points[i + 1]), true));
        }

        figure.Segments.Add(new LineSegment(new Point(width, height * baseLine), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, uint rgb, bool bold = false)
    {
        var typeface = new Typeface(new FontFamily("Yu Gothic UI, Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight, typeface, size, Solid(rgb), 1.0);
        dc.DrawText(formatted, new Point(x, y));
    }

    private static SolidColorBrush Solid(uint rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    private static PixelImage Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // 不透明に描いたので、乗算済みの色のままで同じ
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return new PixelImage(width, height, pixels);
    }
}

/// <summary>見本をしまうときに、しまった日時をずらすための時計。</summary>
internal sealed class DemoClock : TimeProvider
{
    public DateTimeOffset? Override { get; set; }

    public override DateTimeOffset GetUtcNow() => Override?.ToUniversalTime() ?? base.GetUtcNow();
}
