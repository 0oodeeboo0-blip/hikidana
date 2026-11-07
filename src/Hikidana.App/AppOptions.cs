using System.Globalization;
using Hikidana.Core.Layout;
using Hikidana.Core.Settings;

namespace Hikidana.App;

/// <summary>
/// 起動のときの指定（試すときと、CI で画面写真を撮るとき用）。
/// <c>--theme light|dark|system</c>: 画面の色。<c>--demo</c>: 見本の中身で動かす（本物の棚と設定は使わない）。
/// <c>--open</c>: 起動したら棚を開く。<c>--edge right|left|top|bottom</c>: 棚の端。<c>--position 0〜1</c>: 取っ手の位置。
/// <c>--data フォルダー</c>: しまう場所。<c>--hover</c>: 取っ手にマウスを乗せるだけで開く。<c>--settings</c>: 設定の画面を開いて始める。
/// </summary>
public sealed record AppOptions
{
    public string? Theme { get; init; }

    public bool Demo { get; init; }

    public bool Open { get; init; }

    public ShelfEdge? Edge { get; init; }

    public double? HandlePosition { get; init; }

    public string? DataRoot { get; init; }

    /// <summary>取っ手にマウスを乗せるだけで開く（--hover）。</summary>
    public bool OpenOnHover { get; init; }

    /// <summary>設定の画面を開いて始める（--settings）。</summary>
    public bool Settings { get; init; }

    /// <summary>新しい版に入れ替えたあとの起動（--updated）。</summary>
    public bool Updated { get; init; }

    /// <summary>このプロセス（入れ替える前の版）が終わるのを待ってから始める（--wait 番号）。</summary>
    public int? WaitForProcess { get; init; }

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        var options = new AppOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--theme" when next is not null:
                    options = options with { Theme = next.ToLowerInvariant() };
                    i++;
                    break;
                case "--demo":
                    options = options with { Demo = true };
                    break;
                case "--open":
                    options = options with { Open = true };
                    break;
                case "--hover":
                    options = options with { OpenOnHover = true };
                    break;
                case "--settings":
                    options = options with { Settings = true };
                    break;
                case "--updated":
                    options = options with { Updated = true };
                    break;
                case "--wait" when next is not null && int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var pid):
                    options = options with { WaitForProcess = pid };
                    i++;
                    break;
                case "--edge" when next is not null && Enum.TryParse<ShelfEdge>(next, ignoreCase: true, out var edge):
                    options = options with { Edge = edge };
                    i++;
                    break;
                case "--position" when next is not null && double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var position):
                    options = options with { HandlePosition = position };
                    i++;
                    break;
                case "--data" when next is not null:
                    options = options with { DataRoot = next };
                    i++;
                    break;
            }
        }

        return options;
    }

    /// <summary>指定があったものだけ、設定を上書きする。</summary>
    public void ApplyTo(AppSettings settings)
    {
        if (Theme is not null)
        {
            settings.Theme = Theme;
        }

        if (Edge is { } edge)
        {
            settings.Edge = edge;
        }

        if (HandlePosition is { } position)
        {
            settings.HandlePosition = position;
        }

        if (OpenOnHover)
        {
            settings.OpenOnHover = true;
        }

        settings.Normalize();
    }
}
