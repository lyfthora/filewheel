using System;
using System.Windows;
using System.Windows.Threading;

namespace Filewheel.App;

public partial class App : Application
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    private MainWindow? _overlay;
    private DragMonitor? _monitor;
    private DispatcherTimer? _watchdog;
    private DateTime _lastActivity = DateTime.UtcNow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _overlay = new MainWindow();
        _monitor = new DragMonitor();
        _monitor.DragStarted += OnDragStarted;
        _monitor.Moved += OnMoved;
        _monitor.Dropped += OnDropped;
        _monitor.Cancelled += OnCancelled;
        _monitor.Start();

        //each dragging reset count
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _watchdog.Tick += (_, _) =>
        {
            if (DateTime.UtcNow - _lastActivity > IdleTimeout)
            {
                _watchdog?.Stop();
                Shutdown();
            }
        };
        _watchdog.Start();
    }

    private void OnDragStarted(int x, int y)
    {
        _lastActivity = DateTime.UtcNow;
        _overlay?.ShowAt(x, y);
    }

    private void OnMoved(int x, int y)
    {
        _lastActivity = DateTime.UtcNow;
        _overlay?.UpdateHover(x, y);
    }

    private void OnDropped(int x, int y)
    {
        _lastActivity = DateTime.UtcNow;
        if (_overlay is null)
        {
            return;
        }

        var index = _overlay.ResolveSector(x, y);
        if (index < 0)
        {
            _overlay.HideWheel();
            return;
        }

        _overlay.UpdateHover(x, y);
        System.Diagnostics.Debug.WriteLine($"Sector {index}: {_overlay.Wheel.Labels[index]}");

        var confirm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        confirm.Tick += (_, _) =>
        {
            confirm.Stop();
            _overlay.HideWheel();
        };
        confirm.Start();
    }

    private void OnCancelled()
    {
        _lastActivity = DateTime.UtcNow;
        _overlay?.HideWheel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watchdog?.Stop();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}