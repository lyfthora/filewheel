using System;
using System.Windows;
using System.Windows.Threading;

namespace Filewheel.App;

public partial class App : Application
{
    private MainWindow? _overlay;
    private DispatcherTimer? _timer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _overlay = new MainWindow();
        _overlay.ShowAtCursor();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _overlay.Close();
            Shutdown();
        };
        _timer.Start();
    }
}