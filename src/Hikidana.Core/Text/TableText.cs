using System.Text;

namespace Hikidana.Core.Text;

/// <summary>
/// Excel やスプレッドシートからコピーした、タブ区切りのテキスト。
/// セルの中に改行やタブがあると、Excel はそのセルを "" で囲む（中の " は "" になる）。
/// </summary>
public static class TableText
{
    /// <summary>
    /// 表として読む。セルが 2 つ以上あれば true。
    /// 見せる分だけ読む（行は <paramref name="maxRows"/>、列は <paramref name="maxColumns"/> まで）。総数は <paramref name="totalRows"/> などで返す。
    /// </summary>
    public static bool TryParse(string? text, out IReadOnlyList<IReadOnlyList<string>> rows, out int totalRows, out int totalColumns, int maxRows = 50, int maxColumns = 20)
    {
        rows = [];
        totalRows = totalColumns = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 5_000_000)
        {
            return false;
        }

        var all = Split(text);
        while (all.Count > 0 && all[^1].Count == 1 && all[^1][0].Length == 0)
        {
            all.RemoveAt(all.Count - 1);
        }

        if (all.Count == 0)
        {
            return false;
        }

        totalRows = all.Count;
        totalColumns = all.Max(r => r.Count);
        if ((long)totalRows * totalColumns < 2 || !text.Contains('\t') && totalRows < 2)
        {
            return false;
        }

        var columns = Math.Min(totalColumns, maxColumns);
        rows = all.Take(maxRows)
            .Select(r => (IReadOnlyList<string>)Enumerable.Range(0, columns).Select(i => i < r.Count ? r[i] : string.Empty).ToList())
            .ToList();
        return true;
    }

    private static List<List<string>> Split(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var i = 0;
        var atCellStart = true;
        while (i < text.Length)
        {
            var c = text[i];
            if (atCellStart && c == '"')
            {
                // 囲まれたセル: 次の単独の " まで
                var j = i + 1;
                var quoted = new StringBuilder();
                var closed = false;
                while (j < text.Length)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"')
                        {
                            quoted.Append('"');
                            j += 2;
                            continue;
                        }

                        closed = true;
                        j++;
                        break;
                    }

                    quoted.Append(text[j]);
                    j++;
                }

                if (closed && (j >= text.Length || text[j] is '\t' or '\r' or '\n'))
                {
                    cell.Append(quoted);
                    i = j;
                    atCellStart = false;
                    continue;
                }

                // 閉じていない " はふつうの文字として扱う
            }

            switch (c)
            {
                case '\t':
                    row.Add(cell.ToString());
                    cell.Clear();
                    atCellStart = true;
                    i++;
                    continue;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    i++;
                    continue;
                case '\r':
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = [];
                    atCellStart = true;
                    i++;
                    continue;
                default:
                    cell.Append(c);
                    atCellStart = false;
                    i++;
                    continue;
            }
        }

        row.Add(cell.ToString());
        rows.Add(row);
        return rows;
    }
}
