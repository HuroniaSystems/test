using System.Threading;
using System.Windows;

namespace GrpDialer;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, @"Local\HuroniaSystems.GrpDialer", out bool first);
        if (!first)
        {
            MessageBox.Show("GRP Dialer is already running.", "GRP Dialer",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
