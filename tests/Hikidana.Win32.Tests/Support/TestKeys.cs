namespace Hikidana.Win32.Tests.Support;

/// <summary>テストでキーを押したり離したりする（利用者が押したままにしている様子を作る）。</summary>
internal static class TestKeys
{
    public const byte VK_LSHIFT = 0xA0;
    public const byte VK_ESCAPE = 0x1B;
    public const byte VK_RETURN = 0x0D;
    public const byte VK_DOWN = 0x28;
    public const byte VK_LCONTROL = 0xA2;
    public const byte VK_LMENU = 0xA4;
    public const byte VK_LWIN = 0x5B;
    public const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void Down(byte virtualKey) => TestNative.keybd_event(virtualKey, Scan(virtualKey), 0, 0);

    public static void Up(byte virtualKey) => TestNative.keybd_event(virtualKey, Scan(virtualKey), KEYEVENTF_KEYUP, 0);

    public static bool IsDown(byte virtualKey) => TestNative.GetAsyncKeyState(virtualKey) < 0;

    private static byte Scan(byte virtualKey) => (byte)TestNative.MapVirtualKey(virtualKey, 0);
}
