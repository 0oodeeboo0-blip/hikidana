using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Hikidana.Core.Diagnostics;
using Microsoft.Win32;

namespace Hikidana.App.Interop;

/// <summary>
/// 棚の「すりガラス」（後ろがぼけて透ける）。Windows の DWM に後ろをぼかしてもらう（ACCENT_ENABLE_ACRYLICBLURBEHIND）。
/// この方法は、ウインドウが前面になっていなくても効く（棚はフォーカスを奪わないので、前面にならない）。
/// Windows の「透明効果」が切ってあるときや、使えないときは、ふつうの色の背景のままにする。
/// </summary>
internal static unsafe partial class Backdrop
{
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public void* Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    /// <summary>この PC で使えて、利用者が「透明効果」を切っていなければ true。</summary>
    public static bool IsAvailable
    {
        get
        {
            // Windows 10 1809 より前は、ぼかしが重い・崩れることがある
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// すりガラスにする。<paramref name="tint"/> はぼかしの上に重ねる色（少し透ける色）。できたら true。
    /// できなかったときは、ウインドウには何もしない（呼んだ側が、ふつうの背景のままにする）。
    /// </summary>
    public static bool Apply(Window window, Color tint)
    {
        var hwnd = WindowHelper.HandleOf(window);
        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 0,
            GradientColor = ((uint)tint.A << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R,
        };
        var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = &accent, SizeOfData = sizeof(AccentPolicy) };
        if (!SetWindowCompositionAttribute(hwnd, &data))
        {
            Log.Warn($"すりガラスにできませんでした（エラー {Marshal.GetLastPInvokeError()}）。");
            return false;
        }

        // WPF の描く背景を透かして、ぼかしが見えるようにする
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, &margins);
        return true;
    }

    /// <summary>すりガラスをやめる（呼んだ側は、ふつうの背景に戻す）。</summary>
    public static void Remove(Window window)
    {
        var hwnd = WindowHelper.HandleOf(window);
        var accent = new AccentPolicy { AccentState = ACCENT_DISABLED };
        var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = &accent, SizeOfData = sizeof(AccentPolicy) };
        SetWindowCompositionAttribute(hwnd, &data);
        var margins = default(Margins);
        DwmExtendFrameIntoClientArea(hwnd, &margins);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowCompositionAttribute(nint hwnd, WindowCompositionAttributeData* data);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, Margins* margins);
}
