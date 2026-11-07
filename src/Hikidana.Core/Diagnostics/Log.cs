using System.Diagnostics;
using System.Text;

namespace Hikidana.Core.Diagnostics;

/// <summary>
/// 記録（ログ）。%LOCALAPPDATA%\Hikidana\logs に 1 日 1 つのファイルで書く。2 週間より古いものは消す。
/// 困ったときに、利用者に送ってもらうためのもの。クリップボードの中身は書かない。
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string? folder;

    /// <summary>書く場所を決める（決めるまでは Trace にだけ出す）。</summary>
    public static void Initialize(string logFolder, TimeSpan? keep = null)
    {
        lock (Gate)
        {
            folder = logFolder;
            try
            {
                Directory.CreateDirectory(logFolder);
                var limit = DateTime.Now - (keep ?? TimeSpan.FromDays(14));
                foreach (var file in Directory.GetFiles(logFolder, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < limit)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"[Hikidana] 記録の場所を用意できませんでした: {ex.Message}");
            }
        }
    }

    public static string? Folder => folder;

    public static void Info(string message) => Write("情報", message, null);

    public static void Warn(string message, Exception? error = null) => Write("注意", message, error);

    public static void Error(string message, Exception? error = null) => Write("エラー", message, error);

    private static void Write(string level, string message, Exception? error)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (error is not null)
        {
            line.AppendLine().Append(error);
        }

        line.AppendLine();
        var text = line.ToString();
        Trace.Write("[Hikidana] " + text);
        lock (Gate)
        {
            if (folder is null)
            {
                return;
            }

            try
            {
                File.AppendAllText(Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd}.log"), text, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 書けなくても、アプリは止めない
            }
        }
    }
}
