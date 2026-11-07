namespace Hikidana.Core.Settings;

/// <summary>ショートカットで一緒に押すキー（Windows の RegisterHotKey の MOD_ で始まる値と同じ）。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// 棚を開くショートカット（例: Win+Alt+V）。設定のファイルには "win+alt+v" の形で書く（空なら使わない）。
/// 文字のキー（A〜Z・0〜9・Space）に、Win・Ctrl・Alt のどれかを組み合わせたものだけを使う
/// （Shift だけや、文字だけだと、ふつうの入力を横取りしてしまうため）。
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, char Key)
{
    /// <summary>初めの設定（Win+Alt+V）。</summary>
    public const string Default = "win+alt+v";

    /// <summary>設定の書き方を読む。空や "none"、読めない・使えない組み合わせなら null。</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        char? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.ToLowerInvariant();
            switch (name)
            {
                case "win" or "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "space" when key is null:
                    key = ' ';
                    break;
                case { Length: 1 } when key is null && char.IsAsciiLetterOrDigit(name[0]):
                    key = char.ToUpperInvariant(name[0]);
                    break;
                default:
                    return null;
            }
        }

        if (key is null || (modifiers & (HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt)) == 0)
        {
            return null;
        }

        return new Hotkey(modifiers, key.Value);
    }

    /// <summary>Windows の仮想キーの番号（A〜Z と 0〜9 は文字の番号と同じ）。</summary>
    public uint VirtualKey => Key == ' ' ? 0x20u : Key;

    /// <summary>設定のファイルに書く形（例: win+alt+v）。</summary>
    public string ToSetting() => string.Join('+', Parts(forDisplay: false));

    /// <summary>画面に出す形（例: Win + Alt + V）。</summary>
    public override string ToString() => string.Join(" + ", Parts(forDisplay: true));

    private IEnumerable<string> Parts(bool forDisplay)
    {
        if ((Modifiers & HotkeyModifiers.Win) != 0)
        {
            yield return forDisplay ? "Win" : "win";
        }

        if ((Modifiers & HotkeyModifiers.Control) != 0)
        {
            yield return forDisplay ? "Ctrl" : "ctrl";
        }

        if ((Modifiers & HotkeyModifiers.Alt) != 0)
        {
            yield return forDisplay ? "Alt" : "alt";
        }

        if ((Modifiers & HotkeyModifiers.Shift) != 0)
        {
            yield return forDisplay ? "Shift" : "shift";
        }

        yield return Key == ' ' ? (forDisplay ? "Space" : "space") : forDisplay ? Key.ToString() : char.ToLowerInvariant(Key).ToString();
    }
}
