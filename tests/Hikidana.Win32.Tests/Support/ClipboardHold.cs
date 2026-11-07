namespace Hikidana.Win32.Tests.Support;

/// <summary>ほかのアプリがクリップボードを開いたままにしている様子を作る（開くのと閉じるのは同じスレッドで）。</summary>
internal static class ClipboardHold
{
    public static bool Open(nint owner) => TestNative.OpenClipboard(owner);

    public static bool Close(nint owner) => TestNative.CloseClipboard();
}
