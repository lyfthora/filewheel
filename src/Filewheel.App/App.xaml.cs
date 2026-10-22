using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Filewheel.App;

public partial class App : Application
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private const int CopyPathSector = 2;

    private MainWindow? _overlay;
    private DragMonitor? _monitor;
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _hideTimer;
    private DateTime _lastActivity = DateTime.UtcNow;

    protected override void OnStartup(StartupEventArgs e)
    {
         Log.Start();
        Log.Info("=== Filewheel arrancando ===");
        Log.Info(Log.FilePath is null ? "Sin archivo de log." : $"Log: {Log.FilePath}");

        //  cualquier fallo inesperado queda escrito en el log 
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Excepcion no controlada (AppDomain).", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Log.Error("Tarea no observada.", args.Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Excepcion no controlada (UI).", args.Exception);
            args.Handled = true;
            _overlay?.ShowStatus("Error: ver log");
        };


        base.OnStartup(e);

        _overlay = new MainWindow();
        _overlay.FilesDropped += OnFilesDropped;

        _monitor = new DragMonitor();
        _monitor.DragStarted += OnDragStarted;
        _monitor.Moved += OnMoved;
        _monitor.Dropped += OnMouseReleased;
        _monitor.Cancelled += OnCancelled;
        try
        {
            _monitor.Start();
            Log.Info("Hook del raton instalado.");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo instalar el hook del raton.", ex);
            MessageBox.Show(
                $"Filewheel no puede escuchar el raton:{Environment.NewLine}{Log.Describe(ex)}",
                "Filewheel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

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

    private void OnMouseReleased(int x, int y)
    {
        _lastActivity = DateTime.UtcNow;
        // El drop real (OLE) llega justo despues del boton. Si no llega -porque se solto
        // fuera de la rueda- este plazo oculta la ventana.
        HideAfter(TimeSpan.FromMilliseconds(400));
    }

    private void OnFilesDropped(int sector, string[] paths)
    {
        _lastActivity = DateTime.UtcNow;
        _hideTimer?.Stop();
                if (_overlay is null)
        {
            Log.Warn("Drop recibido sin ventana disponible.");
            return;
        }

        if (sector < 0)
        {
            Log.Info($"Drop fuera de un sector con {paths.Length} ruta(s).");
            _overlay.ShowStatus("Suelta en un sector");
            HideAfter(TimeSpan.FromMilliseconds(900));
            return;
        }

        var label = _overlay.LabelFor(sector);
        var count = paths.Length == 1 ? "1 archivo" : $"{paths.Length} archivos";
        Log.Info($"Drop en '{label}' (sector {sector}): {string.Join(" | ", paths)}");
        _overlay.ShowStatus($"{count}{Environment.NewLine}{label}");

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                var result = Execute(sector, paths);
                Log.Info($"Accion '{label}' terminada: {result}");
                _overlay?.ShowStatus($"{count}{Environment.NewLine}{label}{Environment.NewLine}{result}");
            }
            catch (Exception ex)
            {
                Log.Error($"Fallo ejecutando la accion '{label}'.", ex);
                _overlay?.ShowStatus($"{label}{Environment.NewLine}Error: {Log.Describe(ex)}");
            }
            HideAfter(TimeSpan.FromMilliseconds(1400));
        }));
    }
    private static string Execute(int sector, string[] paths)
    {
        if (sector == CopyPathSector)
        {
            PathClipboard.TryCopy(paths, out var message);
            return message;
        }

        Log.Warn($"Sector {sector} sin accion implementada todavia.");
        return "Pendiente";
    }

    private void HideAfter(TimeSpan delay)
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = delay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            _overlay?.HideWheel();
        };
        _hideTimer.Start();
    }

    private void OnCancelled()    
    {
         Log.Info("Arrastre cancelado.");
        _lastActivity = DateTime.UtcNow;
        _overlay?.HideWheel();
    }

    protected override void OnExit(ExitEventArgs e)
            
    {
        Log.Info("=== Filewheel cerrando ===");
        _watchdog?.Stop();
        _hideTimer?.Stop();
        _monitor?.Dispose();
        base.OnExit(e);
    }
}