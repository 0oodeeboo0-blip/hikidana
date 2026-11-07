using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Hikidana.App.Demo;
using Hikidana.App.Shelf;
using Hikidana.App.Theme;
using Hikidana.Core.Diagnostics;
using Hikidana.Core.Paste;
using Hikidana.Core.Settings;
using Hikidana.Core.Storage;
using Hikidana.Win32;

namespace Hikidana.App;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private bool ownsMutex;
    private EventWaitHandle? openSignal;
    private RegisteredWaitHandle? openWait;
    private ClipboardWatcher? watcher;
    private ClipboardRecorder? recorder;
    private ShelfController? controller;

    /// <summary>版（例: 0.1.0+abc1234）。</summary>
    public static string Version =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = AppOptions.Parse(e.Args);
        if (options.Demo && options.DataRoot is null)
        {
            DemoData.Reset();
        }

        var dataRoot = options.DataRoot ?? (options.Demo ? DemoData.Root : ClipStore.DefaultRoot);
        Log.Initialize(Path.Combine(dataRoot, "logs"));
        Log.Info($"起動しました（Hikidana {Version}、{RuntimeInformation.OSDescription}、{RuntimeInformation.ProcessArchitecture}）");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("例外のため終了します。", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warn("拾われなかった例外", args.Exception);
            args.SetObserved();
        };

        // 2 つ目に起動されたら、1 つ目の棚を開いて終わる
        var instanceName = options.Demo ? "Hikidana.Demo" : "Hikidana";
        instanceMutex = new Mutex(true, $@"Local\{instanceName}", out ownsMutex);
        if (!ownsMutex)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting($@"Local\{instanceName}.Open");
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            Log.Info("もう動いているので、そちらの棚を開いて終わります。");
            Shutdown();
            return;
        }

        try
        {
            StartServices(options, dataRoot);
        }
        catch (Exception ex)
        {
            Log.Error("起動できませんでした。", ex);
            MessageBox.Show(
                $"Hikidana を起動できませんでした。\n\n{ex.Message}\n\n記録（ログ）: {Log.Folder}",
                "Hikidana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        recorder?.Dispose();
        watcher?.Dispose();
        openWait?.Unregister(null);
        openSignal?.Dispose();
        if (ownsMutex)
        {
            instanceMutex?.ReleaseMutex();
        }

        instanceMutex?.Dispose();
        Log.Info($"終了しました（終了コード {e.ApplicationExitCode}）。");
        base.OnExit(e);
    }

    private void StartServices(AppOptions options, string dataRoot)
    {
        var settingsStore = new SettingsStore(options.Demo || options.DataRoot is not null
            ? Path.Combine(dataRoot, "settings.json")
            : SettingsStore.DefaultPath);
        var settings = settingsStore.Load();
        options.ApplyTo(settings);

        // Windows 11 の Fluent（スクロールバーやメニュー）と、棚の色（案A）
        ThemeMode = settings.Theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        Palette.Apply(this, settings.Theme);
        Palette.Watch(this);

        var clock = new DemoClock();
        var store = new ClipStore(dataRoot, new ClipStoreOptions { MaxItems = settings.MaxItems }, clock);
        store.Load();
        if (options.Demo && store.Items.Count == 0)
        {
            DemoData.Fill(store, clock);
        }

        watcher = new ClipboardWatcher { ExcludedApps = settings.ExcludedApps, Paused = settings.Paused };
        watcher.Start();
        recorder = new ClipboardRecorder(store, watcher);
        recorder.Failed += ex => Log.Error("コピーしたものを、しまえませんでした。", ex);
        var paste = new PasteService(store, watcher) { MoveToTop = settings.MoveToTopOnPaste };

        controller = new ShelfController(store, watcher, paste, settings, settingsStore);
        controller.Start(open: options.Open);

        openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{(options.Demo ? "Hikidana.Demo" : "Hikidana")}.Open");
        openWait = ThreadPool.RegisterWaitForSingleObject(
            openSignal,
            (_, _) => Dispatcher.BeginInvoke(() => controller?.Open()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        Log.Info($"用意ができました（しまってあるもの {store.Items.Count} 件、しまう場所 {store.Root}）。");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("画面のスレッドで例外", e.Exception);
        e.Handled = true;
    }
}
