using System.Windows;

namespace Hikidana.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = AppOptions.Parse(e.Args);

        // Windows 11 の Fluent デザイン。ライト・ダークは Windows の設定に合わせる（--theme で指定もできる）
        ThemeMode = options.Theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        var window = new StatusWindow();
        MainWindow = window;
        window.Show();
    }
}
