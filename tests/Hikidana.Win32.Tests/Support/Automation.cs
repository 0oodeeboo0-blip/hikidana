using System.Runtime.InteropServices;

namespace Hikidana.Win32.Tests.Support;

/// <summary>
/// UI オートメーション（画面の読み上げなどが使う Windows の仕組み）で、ほかのアプリの部品を探し、画面の上の場所を調べる。
/// WPF では、部品の x:Name がそのまま AutomationId になる。NuGet もほかのプロセスも使わず、COM の関数の表を直接呼ぶ。
/// 呼ぶ順番（表の何番目か）は UIAutomationClient.h の IUIAutomation と IUIAutomationElement のとおり。
/// </summary>
internal static partial class Automation
{
    private const int UIA_NamePropertyId = 30005;
    private const int UIA_AutomationIdPropertyId = 30011;
    private const int TreeScope_Descendants = 4;
    private const ushort VT_BSTR = 8;
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0;

    // IUIAutomation の表の位置（0〜2 は IUnknown）
    private const int ElementFromHandleSlot = 6;
    private const int CreatePropertyConditionSlot = 23;

    // IUIAutomationElement の表の位置
    private const int FindFirstSlot = 5;
    private const int CurrentBoundingRectangleSlot = 43;

    private static readonly Guid CuiAutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid UiAutomationInterface = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }

    /// <summary>
    /// <paramref name="window"/> の中で、AutomationId が <paramref name="automationId"/> の部品の四角（本当の画素）。
    /// 見つからなければ null。
    /// </summary>
    public static TestNative.RECT? FindBounds(nint window, string automationId) => FindBounds(window, UIA_AutomationIdPropertyId, automationId);

    /// <summary><paramref name="window"/> の中で、名前（見えている文字）が <paramref name="name"/> の部品の四角（本当の画素）。</summary>
    public static TestNative.RECT? FindBoundsByName(nint window, string name) => FindBounds(window, UIA_NamePropertyId, name);

    private static unsafe TestNative.RECT? FindBounds(nint window, int propertyId, string value)
    {
        _ = CoInitializeEx(0, COINIT_MULTITHREADED);
        var clsid = CuiAutomationClass;
        var iid = UiAutomationInterface;
        nint automation = 0;
        nint root = 0;
        nint condition = 0;
        nint found = 0;
        var text = SysAllocString(value);
        try
        {
            Check(CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, &automation), "CoCreateInstance(CUIAutomation)");
            var table = *(nint**)automation;

            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)table[ElementFromHandleSlot])(automation, window, &root), "ElementFromHandle");
            var variant = new Variant { Type = VT_BSTR, Value = text };
            Check(((delegate* unmanaged[Stdcall]<nint, int, Variant, nint*, int>)table[CreatePropertyConditionSlot])(automation, propertyId, variant, &condition), "CreatePropertyCondition");

            var rootTable = *(nint**)root;
            Check(((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)rootTable[FindFirstSlot])(root, TreeScope_Descendants, condition, &found), "FindFirst");
            if (found == 0)
            {
                return null;
            }

            TestNative.RECT rect;
            var foundTable = *(nint**)found;
            Check(((delegate* unmanaged[Stdcall]<nint, TestNative.RECT*, int>)foundTable[CurrentBoundingRectangleSlot])(found, &rect), "CurrentBoundingRectangle");
            return rect;
        }
        finally
        {
            Release(found);
            Release(condition);
            Release(root);
            Release(automation);
            SysFreeString(text);
        }
    }

    /// <summary>部品（AutomationId）が見つかるまで待って、その真ん中（本当の画素）を返す。</summary>
    public static Task<(int X, int Y)?> WaitForCenterAsync(nint window, string automationId, TimeSpan timeout) =>
        WaitForCenterAsync(() => FindBounds(window, automationId), timeout);

    /// <summary>名前（見えている文字）の部品が見つかるまで待って、その真ん中（本当の画素）を返す。</summary>
    public static Task<(int X, int Y)?> WaitForNamedCenterAsync(nint window, string name, TimeSpan timeout) =>
        WaitForCenterAsync(() => FindBoundsByName(window, name), timeout);

    private static async Task<(int X, int Y)?> WaitForCenterAsync(Func<TestNative.RECT?> find, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            if (find() is { } rect && rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
            }

            if (DateTime.UtcNow >= until)
            {
                return null;
            }

            await Task.Delay(200);
        }
    }

    private static void Check(int result, string what)
    {
        if (result < 0)
        {
            throw new COMException($"UI オートメーションの {what} が失敗しました（0x{result:X8}）。", result);
        }
    }

    private static unsafe void Release(nint unknown)
    {
        if (unknown != 0)
        {
            var table = *(nint**)unknown;
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)table[2])(unknown);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    [LibraryImport("oleaut32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SysAllocString(string text);

    [LibraryImport("oleaut32.dll")]
    private static partial void SysFreeString(nint text);
}
