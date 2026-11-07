using System.IO.Compression;
using System.Security.Cryptography;

namespace Hikidana.Core.Updates;

/// <summary>
/// 動いている exe を、新しい版に入れ替える（ファイルの操作だけ）。
/// Windows では、動いている exe の中身は書き換えられないが、名前は変えられる。そこで、いまの exe の名前を「.old」で終わる名前に変えてから、
/// 新しい exe を元の名前で置く。古いほうは、次に起動したときに消す。
/// </summary>
public static class SelfUpdate
{
    /// <summary>アプリの exe の名前。</summary>
    public const string ExeName = "Hikidana.exe";

    /// <summary>入れ替えた古い exe の名前の終わり。</summary>
    public const string OldSuffix = ".old";

    /// <summary>zip の中から Hikidana.exe を取り出して <paramref name="folder"/> に置き、その場所を返す。無ければ・exe でなければ例外。</summary>
    public static string ExtractExe(string zipPath, string folder)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, ExeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("zip の中に Hikidana.exe がありません。");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, ExeName);
        entry.ExtractToFile(target, overwrite: true);
        if (!LooksLikeExe(target))
        {
            throw new InvalidDataException("zip から取り出したファイルが、exe ではありません。");
        }

        return target;
    }

    /// <summary>先頭が「MZ」（Windows の実行ファイルの印）なら true。</summary>
    public static bool LooksLikeExe(string path)
    {
        Span<byte> head = stackalloc byte[2];
        using var file = File.OpenRead(path);
        return file.Length > 2 && file.Read(head) == 2 && head[0] == (byte)'M' && head[1] == (byte)'Z';
    }

    /// <summary>
    /// <paramref name="currentExe"/>（いま動いている exe）を、<paramref name="newExe"/> に入れ替える。
    /// 途中で失敗したら、元の exe を元の名前に戻してから、例外をそのまま投げる。古い exe の場所（名前を変えたもの）を返す。
    /// </summary>
    public static string Replace(string currentExe, string newExe)
    {
        if (!File.Exists(newExe))
        {
            throw new FileNotFoundException("新しい exe が見つかりません。", newExe);
        }

        var old = FreeOldName(currentExe);
        File.Move(currentExe, old);
        try
        {
            File.Copy(newExe, currentExe, overwrite: false);
        }
        catch
        {
            if (File.Exists(currentExe))
            {
                TryDelete(currentExe);
            }

            File.Move(old, currentExe);
            throw;
        }

        return old;
    }

    /// <summary>前に入れ替えたときの古い exe を消す（まだ使われていて消せないものは、そのまま。次の機会に消す）。消した数を返す。</summary>
    public static int CleanupOld(string currentExe)
    {
        var folder = Path.GetDirectoryName(currentExe);
        if (folder is null || !Directory.Exists(folder))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.GetFiles(folder, Path.GetFileName(currentExe) + "*" + OldSuffix))
        {
            if (TryDelete(file))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>ファイルの SHA-256（16 進の小文字）。</summary>
    public static string Sha256(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }

    /// <summary>
    /// チェックサムのファイル（「ハッシュ  ファイル名」か、ハッシュだけ）から、SHA-256 の 16 進を取り出す。読めなければ null。
    /// </summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>古い exe に付ける、まだ使われていない名前（前の残りを消せなければ、番号を付ける）。</summary>
    private static string FreeOldName(string currentExe)
    {
        var name = currentExe + OldSuffix;
        if (!File.Exists(name) || TryDelete(name))
        {
            return name;
        }

        for (var i = 2; ; i++)
        {
            var numbered = $"{currentExe}.{i}{OldSuffix}";
            if (!File.Exists(numbered) || TryDelete(numbered))
            {
                return numbered;
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
