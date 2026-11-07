using System.Diagnostics;
using System.Text.Json;
using Hikidana.Testing;

namespace Hikidana.Win32.Tests.Support;

/// <summary>ページが受け取った 1 つのファイル。</summary>
internal sealed record PastedFile(string Name, string Type, long Size);

/// <summary>ページが受け取った 1 回の貼り付け（ブラウザーの paste イベントの中身）。</summary>
internal sealed record PasteEvent(int Number, IReadOnlyList<string> Types, IReadOnlyList<string> Items, IReadOnlyList<PastedFile> Files, string Text, int HtmlLength, int Width, int Height, string Raw)
{
    public override string ToString() => Raw;
}

/// <summary>
/// Edge で開いた、貼り付けのテスト用のページ（Pages/paste-test.html）。ChatGPT や Claude のアプリと同じ Chromium で、
/// 入力欄が paste イベントで受け取った中身を、ウインドウの題名に JSON で書く（「HKD[」と「]HKD」の間）。
/// Edge の起動には時間がかかるので、テストのあいだ 1 つを使い回す。
/// </summary>
internal sealed class ChromiumPage : IDisposable
{
    private const string Start = "HKD[";
    private const string End = "]HKD";
    private static ChromiumPage? shared;
    private static TempDir? sharedDir;

    private readonly Process process;
    private int lastNumber;

    private ChromiumPage(Process process, nint window)
    {
        this.process = process;
        Window = window;
    }

    public nint Window { get; }

    /// <summary>テストのあいだ使い回すページ。無ければ開く。</summary>
    public static async Task<ChromiumPage> SharedAsync()
    {
        if (shared is { } page && Hikidana.Win32.TargetWindow.From(page.Window)?.Exists == true)
        {
            return page;
        }

        sharedDir ??= new TempDir();
        shared = await OpenAsync(sharedDir);
        return shared;
    }

    public static void CloseShared()
    {
        shared?.Dispose();
        shared = null;
        sharedDir?.Dispose();
        sharedDir = null;
    }

    public static string? FindEdge()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            var path = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static async Task<ChromiumPage> OpenAsync(TempDir dir)
    {
        var edge = FindEdge() ?? throw new SkipException("Microsoft Edge が見つかりません。");
        var page = Path.Combine(AppContext.BaseDirectory, "Pages", "paste-test.html");
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        foreach (var argument in new[]
        {
            "--app=" + new Uri(page).AbsoluteUri,
            "--user-data-dir=" + dir.Sub("edge-profile"),
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-sync",
            "--disable-extensions",
            "--disable-features=Translate",
            "--window-position=80,60",
            "--window-size=1200,860",
        })
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new AssertionException("Edge を起動できませんでした。");
        var window = await Desktop.WaitForWindowAsync(w => Desktop.TitleOf(w).Contains(Start + "ready" + End, StringComparison.Ordinal), TimeSpan.FromSeconds(60));
        if (window == 0)
        {
            ScreenCapture.Save("failed-edge-start");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new AssertionException("貼り付けのテストのページが 60 秒たっても開きませんでした。");
        }

        Console.WriteLine($"       ページ: {Desktop.Describe(window)}");
        return new ChromiumPage(process, window);
    }

    /// <summary>次の貼り付けを待つ（前に読んだものより新しいもの）。</summary>
    public async Task<PasteEvent> NextPasteAsync(int timeoutMilliseconds = 8000)
    {
        PasteEvent? found = null;
        await Desktop.WaitUntilAsync(
            () =>
            {
                found = Read();
                return found is not null && found.Number > lastNumber;
            },
            TimeSpan.FromMilliseconds(timeoutMilliseconds));
        if (found is null || found.Number <= lastNumber)
        {
            throw new AssertionException($"ページが貼り付けを受け取りませんでした（題名: {Desktop.TitleOf(Window)}、前面: {Desktop.ForegroundDescription()}）。");
        }

        lastNumber = found.Number;
        Console.WriteLine($"       受け取った中身: {found.Raw}");
        return found;
    }

    /// <summary>いまの題名から、最後の貼り付けを読む。まだ無ければ null。</summary>
    public PasteEvent? Read()
    {
        var title = Desktop.TitleOf(Window);
        var start = title.IndexOf(Start, StringComparison.Ordinal);
        var end = title.LastIndexOf(End, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return null;
        }

        var json = title[(start + Start.Length)..end];
        if (!json.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            static IReadOnlyList<string> Strings(JsonElement element) => element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
            return new PasteEvent(
                root.GetProperty("n").GetInt32(),
                Strings(root.GetProperty("types")),
                Strings(root.GetProperty("items")),
                root.GetProperty("files").EnumerateArray()
                    .Select(f => new PastedFile(f.GetProperty("name").GetString() ?? string.Empty, f.GetProperty("type").GetString() ?? string.Empty, f.GetProperty("size").GetInt64()))
                    .ToList(),
                root.GetProperty("text").GetString() ?? string.Empty,
                root.GetProperty("html").GetInt32(),
                root.GetProperty("w").GetInt32(),
                root.GetProperty("h").GetInt32(),
                json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
    }
}
