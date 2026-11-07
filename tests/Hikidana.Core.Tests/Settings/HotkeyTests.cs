using Hikidana.Core.Settings;
using Hikidana.Testing;

namespace Hikidana.Core.Tests.Settings;

public sealed class HotkeyTests
{
    [Fact]
    public void DefaultIsWinAltV()
    {
        var hotkey = Hotkey.Parse(Hotkey.Default)!.Value;
        Assert.Equal(HotkeyModifiers.Win | HotkeyModifiers.Alt, hotkey.Modifiers);
        Assert.Equal('V', hotkey.Key);
        Assert.Equal(0x56u, hotkey.VirtualKey);
        Assert.Equal("Win + Alt + V", hotkey.ToString());
        Assert.Equal("win+alt+v", hotkey.ToSetting());
    }

    [Fact]
    public void OrderAndCaseDoNotMatter()
    {
        var hotkey = Hotkey.Parse(" Shift + CTRL + space ")!.Value;
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, hotkey.Modifiers);
        Assert.Equal(0x20u, hotkey.VirtualKey);
        Assert.Equal("Ctrl + Shift + Space", hotkey.ToString());
        Assert.Equal("ctrl+shift+space", hotkey.ToSetting());
        Assert.Equal('3', Hotkey.Parse("alt+3")!.Value.Key);
    }

    [Fact]
    public void UnusableCombinationsAreRejected()
    {
        Assert.Null(Hotkey.Parse(null));
        Assert.Null(Hotkey.Parse(""));
        Assert.Null(Hotkey.Parse("none"));
        Assert.Null(Hotkey.Parse("v"), "文字だけだと、ふつうの入力を取ってしまう。");
        Assert.Null(Hotkey.Parse("shift+v"), "Shift だけも、ふつうの入力（大文字）になる。");
        Assert.Null(Hotkey.Parse("win+alt"), "文字のキーが要る。");
        Assert.Null(Hotkey.Parse("win+alt+v+b"), "文字のキーは 1 つだけ。");
        Assert.Null(Hotkey.Parse("win+alt+f13"));
    }
}
