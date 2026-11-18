using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GrpDialer.Services;

namespace GrpDialer;

public partial class App : Application
{
    private const string MutexName = @"Local\HuroniaSystems.GrpDialer";
    private const string ShowEventName = @"Local\HuroniaSystems.GrpDialer.Show";

    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _tray;
    private DateTime _lastErrorBox = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: bring that window up instead of opening a second one.
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch { /* first copy is still starting or closing */ }
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) =>
            Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.BringUp()), null, Timeout.Infinite, false);

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log(ex.Exception); ex.SetObserved(); };

        base.OnStartup(e);

        // Lives in the notification area like OTC Monitor: no taskbar button, the window is a
        // flyout above the clock, and only Exit (tray menu or Settings) stops the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SessionEnding += (_, _) => Exiting = true;

        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayIcon();
        _tray.Toggle += window.ToggleFlyout;
        _tray.OpenSettings += window.OpenSettings;
        _tray.Exit += ExitApp;
        window.Tray = _tray;
        window.ShowFlyout();
    }

    /// <summary>True once the user chose Exit (or Windows is signing out), so closing really closes.</summary>
    public bool Exiting { get; private set; }

    public void ExitApp()
    {
        Exiting = true;
        Shutdown();
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep running: a glitch in one handler shouldn't take the dialer down mid-shift.
        e.Handled = true;
        Log(e.Exception);
        if (DateTime.Now - _lastErrorBox < TimeSpan.FromSeconds(30)) return;
        _lastErrorBox = DateTime.Now;
        MessageBox.Show("Something went wrong, but GRP Dialer is still running.\n\nDetails were saved to "
                        + Path.Combine(Settings.Dir, "error.log"),
            "GRP Dialer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var path = Path.Combine(Settings.Dir, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { /* nowhere left to report it */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();   // otherwise a dead icon lingers by the clock until hovered
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
