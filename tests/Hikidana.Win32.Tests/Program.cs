using Hikidana.Win32.Tests.Support;

if (OperatingSystem.IsWindows())
{
    // 画面写真や座標を、拡大の設定にかかわらず本当の画素で扱う
    TestNative.SetProcessDpiAwarenessContext(TestNative.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
}

var code = Hikidana.Testing.TestRunner.Run(typeof(Program).Assembly, args);
ChromiumPage.CloseShared();
return code;
