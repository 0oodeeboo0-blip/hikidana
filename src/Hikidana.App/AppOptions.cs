namespace Hikidana.App;

/// <summary>
/// 起動のときの指定。
/// <c>--theme light|dark|system</c>: 画面の色（既定は Windows に合わせる）。
/// </summary>
public sealed record AppOptions(string Theme)
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        var theme = "system";
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--theme" when i + 1 < args.Count:
                    theme = args[++i].ToLowerInvariant();
                    break;
            }
        }

        return new AppOptions(theme);
    }
}
