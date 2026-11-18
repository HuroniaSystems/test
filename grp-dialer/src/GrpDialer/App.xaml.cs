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
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
