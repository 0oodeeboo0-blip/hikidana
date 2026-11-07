using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Hikidana.Testing;

/// <summary>テストのメソッドに付ける。public なクラスの、引数の無いメソッド（Task を返してもよい）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute
{
    /// <summary>理由を書くと飛ばす。</summary>
    public string? Skip { get; init; }

    /// <summary>Windows でだけ動かす（クリップボードやキー入力を本当に使うテスト）。</summary>
    public bool WindowsOnly { get; init; }

    /// <summary>STA のスレッドで動かす（OLE のクリップボードやドラッグを使うテスト）。</summary>
    public bool Sta { get; init; }
}

/// <summary>確かめた結果が違ったときに投げる。</summary>
public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void True([DoesNotReturnIf(false)] bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionException(message ?? "true のはずが false でした。");
        }
    }

    public static void False([DoesNotReturnIf(true)] bool condition, string? message = null)
    {
        if (condition)
        {
            throw new AssertionException(message ?? "false のはずが true でした。");
        }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException($"{Prefix(message)}期待: {Show(expected)} 実際: {Show(actual)}");
        }
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new AssertionException($"{Prefix(message)}{Show(actual)} 以外のはずでした。");
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null)
        {
            throw new AssertionException($"{Prefix(message)}null のはずが {Show(value)} でした。");
        }
    }

    public static void NotNull([NotNull] object? value, string? message = null)
    {
        if (value is null)
        {
            throw new AssertionException($"{Prefix(message)}null でした。");
        }
    }

    public static void Contains(string expectedPart, string? actual, string? message = null)
    {
        if (actual is null || !actual.Contains(expectedPart, StringComparison.Ordinal))
        {
            throw new AssertionException($"{Prefix(message)}{Show(expectedPart)} が {Show(actual)} に含まれていません。");
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var e = expected.ToArray();
        var a = actual.ToArray();
        if (e.Length != a.Length)
        {
            throw new AssertionException($"{Prefix(message)}長さが違います。期待: {e.Length} 実際: {a.Length}");
        }

        for (var i = 0; i < e.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(e[i], a[i]))
            {
                throw new AssertionException($"{Prefix(message)}{i} 番目が違います。期待: {Show(e[i])} 実際: {Show(a[i])}");
            }
        }
    }

    public static TException Throws<TException>(Action action, string? message = null)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"{Prefix(message)}{typeof(TException).Name} のはずが {ex.GetType().Name} でした: {ex.Message}");
        }

        throw new AssertionException($"{Prefix(message)}{typeof(TException).Name} が投げられませんでした。");
    }

    private static string Prefix(string? message) => string.IsNullOrEmpty(message) ? string.Empty : message + " ";

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "?",
    };
}

/// <summary>
/// テストを見つけて動かす。<c>dotnet run --project テストのプロジェクト -- --filter 名前の一部</c> で絞り込める。
/// 1 つでも失敗したら終了コード 1。
/// </summary>
public static class TestRunner
{
    public static int Run(Assembly assembly, string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? filter = null;
        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--filter" || args[i] == "-f") && i + 1 < args.Length)
            {
                filter = args[++i];
            }
        }

        var tests = assembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => (Type: t, Method: m, Fact: m.GetCustomAttribute<FactAttribute>())))
            .Where(x => x.Fact is not null)
            .OrderBy(x => x.Type.FullName, StringComparer.Ordinal)
            .ThenBy(x => x.Method.Name, StringComparer.Ordinal)
            .ToList();

        int passed = 0, failed = 0, skipped = 0;
        var failures = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (var (type, method, fact) in tests)
        {
            var name = $"{type.Name}.{method.Name}";
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fact!.Skip is { } reason)
            {
                skipped++;
                Console.WriteLine($"  skip {name}（{reason}）");
                continue;
            }

            if (fact.WindowsOnly && !OperatingSystem.IsWindows())
            {
                skipped++;
                continue;
            }

            var watch = Stopwatch.StartNew();
            var error = fact.Sta ? RunOnSta(type, method) : RunOne(type, method);
            if (error is null)
            {
                passed++;
                Console.WriteLine($"  ok   {name} ({watch.ElapsedMilliseconds} ms)");
            }
            else
            {
                failed++;
                failures.Add(name);
                Console.WriteLine($"  FAIL {name}");
                foreach (var line in error.ToString().Split('\n'))
                {
                    Console.WriteLine("       " + line.TrimEnd());
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed} 件成功、{failed} 件失敗、{skipped} 件飛ばし（{total.Elapsed.TotalSeconds:0.0} 秒）");
        foreach (var name in failures)
        {
            Console.WriteLine($"  失敗: {name}");
        }

        return failed == 0 ? 0 : 1;
    }

    private static Exception? RunOne(Type type, MethodInfo method)
    {
        try
        {
            var instance = method.IsStatic ? null : Activator.CreateInstance(type);
            try
            {
                var result = method.Invoke(instance, null);
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                }
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Exception? RunOnSta(Type type, MethodInfo method)
    {
        if (!OperatingSystem.IsWindows())
        {
            return RunOne(type, method);
        }

        Exception? error = null;
        var thread = new Thread(() => error = RunOne(type, method));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error;
    }
}
