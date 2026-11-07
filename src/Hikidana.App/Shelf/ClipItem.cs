using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media;
using Hikidana.Core.Clips;
using Hikidana.Core.Text;

namespace Hikidana.App.Shelf;

/// <summary>棚の 1 枚のカードに出すもの。しまったもの（<see cref="Clip"/>）から作る。</summary>
public sealed class ClipItem : INotifyPropertyChanged
{
    private const int MaxPreviewLength = 400;
    private ImageSource? thumbnail;
    private string time = string.Empty;
    private bool selected;

    public ClipItem(Clip clip, DateTimeOffset now)
    {
        Clip = clip;
        Version = (clip.UpdatedAt, clip.Pinned, clip.Kind);
        KindName = ClipLabels.KindName(clip.Kind);
        AppName = ClipLabels.AppName(clip.SourceApp);
        Glyph = clip.Kind switch
        {
            ClipKind.Image => "\uE8B9",
            ClipKind.Screenshot => "\uE722",
            ClipKind.Link => "\uE71B",
            ClipKind.Code => "\uE943",
            ClipKind.Files => "\uE8B7",
            ClipKind.Color => "\uE790",
            ClipKind.Table => "\uE8A9",
            ClipKind.RichText => "\uE70F",
            _ => "\uE8C1",
        };

        switch (clip.Kind)
        {
            case ClipKind.Image or ClipKind.Screenshot:
                IsImage = true;
                Detail = $"{clip.ImageWidth}×{clip.ImageHeight}" + (clip.ImageBytes > 0 ? $"・{ClipLabels.Size(clip.ImageBytes)}" : string.Empty);
                break;
            case ClipKind.Files:
                IsFiles = true;
                var names = clip.Files.Take(4).Select(f => Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : f).ToList();
                Text = string.Join("\n", names);
                Detail = clip.Files.Count > names.Count ? $"ほか {clip.Files.Count - names.Count} 個" : clip.Files.Count > 1 ? $"{clip.Files.Count} 個" : null;
                break;
            case ClipKind.Color:
                IsColor = true;
                Text = clip.ColorHex ?? clip.Preview;
                if (clip.ColorHex is { } hex && ColorConverter.ConvertFromString(hex) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    Swatch = brush;
                }

                break;
            case ClipKind.Link:
                IsLink = true;
                Text = Shorten(clip.Preview);
                Detail = Uri.TryCreate(clip.Preview.Trim(), UriKind.Absolute, out var uri) ? uri.Host : null;
                break;
            case ClipKind.Table:
                IsTable = true;
                Text = TablePreview(clip);
                Detail = $"{clip.TableRowCount} 行 × {clip.TableColumnCount} 列";
                break;
            case ClipKind.Code:
                IsCode = true;
                Text = Shorten(clip.Preview);
                break;
            default:
                Text = Shorten(clip.Preview);
                if (clip.TextLength > MaxPreviewLength)
                {
                    Detail = $"{clip.TextLength:N0} 文字";
                }

                break;
        }

        HasText = !string.IsNullOrEmpty(Text);
        SearchText = string.Join(
            "\n",
            new[] { clip.Preview, KindName, AppName, clip.SourceApp, clip.SourceUrl, clip.ColorHex, Detail }
                .Concat(clip.Files)
                .Where(t => !string.IsNullOrEmpty(t)));
        Category = clip.Kind switch
        {
            ClipKind.Image or ClipKind.Screenshot => ClipCategory.Images,
            ClipKind.Link => ClipCategory.Links,
            ClipKind.Files => ClipCategory.Files,
            _ => ClipCategory.Text,
        };
        UpdateTime(now);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>検索で探す文字（見出し・アプリ・URL・ファイルの名前など）。</summary>
    public string SearchText { get; }

    /// <summary>絞り込みの分け方。</summary>
    public ClipCategory Category { get; }

    /// <summary>キーボードで選んでいる（検索中の ↑↓）。</summary>
    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected != value)
            {
                selected = value;
                OnPropertyChanged();
            }
        }
    }

    public Clip Clip { get; }

    /// <summary>作ったときの、並び順の日時・ピン留め・種類。変わったらカードを作り直す。</summary>
    public (DateTimeOffset UpdatedAt, bool Pinned, ClipKind Kind) Version { get; }

    public string Glyph { get; }

    public string KindName { get; }

    public string? AppName { get; }

    /// <summary>「Chrome・5 分前」のような、カードの上の小さな文字。</summary>
    public string Time
    {
        get => time;
        private set
        {
            if (time != value)
            {
                time = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Meta));
            }
        }
    }

    public string Meta => AppName is null ? Time : $"{AppName}・{Time}";

    public string? Text { get; }

    public bool HasText { get; }

    /// <summary>大きさ・行と列の数・URL のホストなど、補足の文字。</summary>
    public string? Detail { get; }

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public bool IsImage { get; }

    public bool IsFiles { get; }

    public bool IsColor { get; }

    public bool IsLink { get; }

    public bool IsTable { get; }

    public bool IsCode { get; }

    /// <summary>テキストとしてそのまま見せる（コードと表と色以外。リンクとファイルの名前も）。</summary>
    public bool IsPlainText => HasText && !IsCode && !IsTable && !IsColor;

    public Brush? Swatch { get; }

    public bool Pinned => Version.Pinned;

    public string PinGlyph => Version.Pinned ? "\uE840" : "\uE718";

    public string PinToolTip => Version.Pinned ? "ピン留めを外す" : "ピン留めする（消えないようにする）";

    /// <summary>右クリックの「ピン留め」の文字。</summary>
    public string PinMenuText => Version.Pinned ? "ピン留めを外す" : "ピン留めする";

    /// <summary>右クリックの、もう 1 つの貼り付け方（無ければ null。ふつうの貼り付けと同じになる種類は出さない）。</summary>
    public string? AlternatePasteText => Clip.Kind switch
    {
        ClipKind.Image or ClipKind.Screenshot => "ファイルとして貼り付け",
        ClipKind.RichText or ClipKind.Table => "書式なしで貼り付け",
        ClipKind.Files => "場所を文字で貼り付け",
        _ => null,
    };

    public bool HasAlternatePaste => AlternatePasteText is not null;

    /// <summary>「名前を付けて保存」ができる（ファイルは元の場所にあるので出さない）。</summary>
    public bool CanSave => !IsFiles;

    public string PasteToolTip => IsImage
        ? "クリックで貼り付け。Shift を押しながらクリックすると、ファイルとして貼り付けます。"
        : "クリックで貼り付け。Shift を押しながらクリックすると、書式なしで貼り付けます。";

    /// <summary>カードに出す画像の大きさ（DIP）。読み込む前から場所をとっておく。</summary>
    public double ImageDisplayWidth => ImageSize().Width;

    public double ImageDisplayHeight => ImageSize().Height;

    /// <summary>コードと表は等幅の文字で見せる。</summary>
    public bool IsMonospace => HasText && (IsCode || IsTable);

    public ImageSource? Thumbnail
    {
        get => thumbnail;
        set
        {
            thumbnail = value;
            OnPropertyChanged();
        }
    }

    /// <summary>カードの中の、画像を置ける幅と高さ（DIP）。</summary>
    public const double ImageMaxWidth = 308;

    public const double ImageMaxHeight = 200;

    /// <summary>しまったものと同じなら true（作り直さなくてよい）。</summary>
    public bool Matches(Clip clip) => ReferenceEquals(clip, Clip) && Version == (clip.UpdatedAt, clip.Pinned, clip.Kind);

    public void UpdateTime(DateTimeOffset now) => Time = RelativeTime.Format(Clip.UpdatedAt, now);

    private (double Width, double Height) ImageSize()
    {
        if (!IsImage || Clip.ImageWidth <= 0 || Clip.ImageHeight <= 0)
        {
            return (0, 0);
        }

        var scale = Math.Min(1, Math.Min(ImageMaxWidth / Clip.ImageWidth, ImageMaxHeight / Clip.ImageHeight));
        return (Math.Max(1, Math.Round(Clip.ImageWidth * scale)), Math.Max(1, Math.Round(Clip.ImageHeight * scale)));
    }

    private static string Shorten(string text)
    {
        // 空の行が続くところは 1 行にまとめ、長ければ先頭だけ
        var builder = new StringBuilder(Math.Min(text.Length, MaxPreviewLength));
        var blank = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            blank = line.Length == 0 ? blank + 1 : 0;
            if (blank > 1)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line);
            if (builder.Length >= MaxPreviewLength)
            {
                break;
            }
        }

        var result = builder.ToString().Trim('\n');
        return result.Length > MaxPreviewLength ? result[..MaxPreviewLength] + "…" : result;
    }

    private static string TablePreview(Clip clip)
    {
        if (clip.TableRows is not { Count: > 0 } rows)
        {
            return Shorten(clip.Preview);
        }

        var columns = Math.Min(4, rows.Max(r => r.Count));
        var shown = rows.Take(4).ToList();
        var widths = Enumerable.Range(0, columns)
            .Select(c => Math.Min(12, shown.Max(r => c < r.Count ? Width(r[c]) : 0)))
            .ToArray();
        var builder = new StringBuilder();
        foreach (var row in shown)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            for (var c = 0; c < columns; c++)
            {
                var cell = c < row.Count ? row[c].ReplaceLineEndings(" ") : string.Empty;
                cell = Cut(cell, widths[c]);
                builder.Append(cell);
                if (c < columns - 1)
                {
                    builder.Append(' ', Math.Max(1, widths[c] - Width(cell) + 2));
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>等幅の文字の幅（全角は 2）。</summary>
    private static int Width(string text) => text.Sum(c => c > 0x2E7F ? 2 : 1);

    private static string Cut(string text, int width)
    {
        if (Width(text) <= width)
        {
            return text;
        }

        var builder = new StringBuilder();
        var used = 0;
        foreach (var c in text)
        {
            var w = c > 0x2E7F ? 2 : 1;
            if (used + w > width - 1)
            {
                break;
            }

            builder.Append(c);
            used += w;
        }

        return builder.Append('…').ToString();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>絞り込みの分け方。</summary>
public enum ClipCategory
{
    Text,
    Images,
    Links,
    Files,
}
