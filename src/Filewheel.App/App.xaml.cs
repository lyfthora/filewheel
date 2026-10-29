using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Filewheel.Core;
using System.IO;
using static System.IO.Directory;

namespace Filewheel.App;

public partial class App : Application
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private const int CopyPathSector = 2;
    private const int DuplicateSector = 5;
    private const int MaxNameChars = 16;
    private const int RenameSector = 3;
    private const int CompressSector = 0;
    private const int DeleteSector = 6;
    private static readonly TimeSpan SubmenuDelay = TimeSpan.FromMilliseconds(250);
    private const int MoveSector = 4;

    private MainWindow? _overlay;
    private DragMonitor? _monitor;
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _hideTimer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _busy;
    private DispatcherTimer? _submenuTimer;
    private static readonly List<Destination> Destinations = BuildDestinations();

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
            if (_busy)
            {
                return;
            }

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
        UpdateSubmenu();
    }

    private void OnMouseReleased(int x, int y)
    {
        _lastActivity = DateTime.UtcNow;
        // El drop real (OLE) llega justo despues del boton. Si no llega -porque se solto
        // fuera de la rueda- este plazo oculta la ventana.
        HideAfter(TimeSpan.FromMilliseconds(400));
    }

    private void OnFilesDropped(DropRequest drop)
    {
        _lastActivity = DateTime.UtcNow;
        _hideTimer?.Stop();
        _submenuTimer?.Stop();
        _submenuTimer = null;

        var paths = drop.Paths;
        var sector = drop.Sector;

        if (_overlay is null)
        {
            Log.Warn("Drop recibido sin ventana disponible.");
            return;
        }
        if (_busy)
        {
            Log.Warn("Drop ignorado: hay una accion en curso.");
            return;
        }
        _overlay.RestoreMainRing();

        if (drop.Destination is not null)
        {
            BeginMove(paths, drop.Destination);
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

        if (sector == RenameSector)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => BeginRename(paths)));
            return;
        }
        if (sector == DeleteSector)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => BeginDelete(paths)));
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await RunActionAsync(label, sector, paths, count);
        }));
    }

    private async Task RunActionAsync(string label, int sector, string[] paths, string count, string? destination = null)
    {
        _busy = true;
        try
        {
            var progress = new Progress<DuplicateProgress>(p => ShowProgress(label, p));

            var result = await Task.Run(() => Execute(sector, paths, progress, destination));

            Log.Info($"Accion '{label}' terminada: {result}");
            _overlay?.ShowStatus($"{count}{Environment.NewLine}{label}{Environment.NewLine}{result}");
        }
        catch (Exception ex)
        {
            Log.Error($"Fallo ejecutando la accion '{label}'.", ex);
            _overlay?.ShowStatus($"{label}{Environment.NewLine}Error: {Log.Describe(ex)}");
        }
        finally
        {
            _busy = false;
            _lastActivity = DateTime.UtcNow;
            HideAfter(TimeSpan.FromMilliseconds(1400));
        }
    }
    private void BeginRename(string[] paths)
    {
        if (paths.Length != 1)
        {
            Log.Warn($"Renombrar con {paths.Length} elementos: solo se admite uno.");
            _overlay?.ShowStatus("Renombrar: uno a la vez");
            HideAfter(TimeSpan.FromMilliseconds(1400));
            return;
        }

        var path = paths[0];
        var isDirectory = Directory.Exists(path);

        if (!isDirectory && !File.Exists(path))
        {
            Log.Warn($"Renombrar sobre algo que ya no existe: {path}");
            _overlay?.ShowStatus("Ese archivo ya no esta");
            HideAfter(TimeSpan.FromMilliseconds(1400));
            return;
        }

        var name = Path.GetFileName(path);
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);

        if (stem.Length == 0)
        {
            stem = name;
        }

        NativeMethods.GetCursorPos(out var cursor);

        var prompt = new RenamePrompt(name, stem, typed => CommitRename(path, typed));

        _busy = true;
        _lastActivity = DateTime.UtcNow;
        prompt.Closed += (_, _) =>
        {
            _busy = false;
            _lastActivity = DateTime.UtcNow;
            _overlay?.HideWheel();
        };

        prompt.ShowNear(cursor.X, cursor.Y);
    }

    private static string? CommitRename(string path, string typed)
    {
        var result = RenameAction.Run(path, typed);

        if (result.Success)
        {
            Log.Info($"Renombrado: {result.Source} -> {result.Target}");
            return null;
        }

        Log.Warn($"No renombrado: {path} ({result.Error})");
        return result.Error;
    }
    private void BeginDelete(string[] paths)
    {
        if (paths.Length == 0)
        {
            return;
        }

        foreach (var path in paths)
        {
            if (!DeleteAction.CanRecycle(path, out var reason))
            {
                Log.Warn($"Borrado rechazado: {path} ({reason})");
                _overlay?.ShowStatus(reason);
                HideAfter(TimeSpan.FromMilliseconds(1800));
                return;
            }
        }

        NativeMethods.GetCursorPos(out var cursor);

        var subject = paths.Length == 1
            ? Path.GetFileName(paths[0])
            : $"{paths.Length} elementos";

        var label = _overlay?.LabelFor(DeleteSector) ?? "Eliminar";
        var count = paths.Length == 1 ? "1 archivo" : $"{paths.Length} archivos";
        var prompt = new DeletePrompt(subject);

        _busy = true;
        _lastActivity = DateTime.UtcNow;

        prompt.Closed += (_, _) =>
        {
            _lastActivity = DateTime.UtcNow;

            if (!prompt.Confirmed)
            {
                _busy = false;
                _overlay?.HideWheel();
                return;
            }

            // _busy sigue en true: el borrado lo releva sin dejar hueco.
            _ = RunActionAsync(label, DeleteSector, paths, count);
        };

        prompt.ShowNear(cursor.X, cursor.Y);
    }
    private void BeginMove(string[] paths, string destination)
    {
        var label = _overlay?.LabelFor(MoveSector) ?? "Mover a";
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(destination));
        var target = folder.Length > 0 ? folder : destination;
        var count = paths.Length == 1 ? "1 archivo" : $"{paths.Length} archivos";

        Log.Info($"Drop en '{label}': {string.Join(" | ", paths)} -> {destination}");

        _ = RunActionAsync($"{label}: {target}", MoveSector, paths, count, destination);
    }

    /// <summary>
    /// Abre el submenu de 'Mover a' cuando el cursor se queda quieto en ese sector,
    /// y lo cierra si vuelve al centro. Siempre con retardo: pasar de largo no cuenta.
    /// </summary>
    private void UpdateSubmenu()
    {
        if (_overlay is null || _busy)
        {
            return;
        }

        var hover = _overlay.HoverSector;

        if (_overlay.InMoveMode)
        {
            if (hover < 0 && _submenuTimer is null)
            {
                DeferSubmenu(() =>
                {
                    if (_overlay is { InMoveMode: true } && _overlay.HoverSector < 0 && !_busy)
                    {
                        _overlay.RestoreMainRing();
                        Log.Info("Submenu 'Mover a' cerrado.");
                    }
                });
            }

            return;
        }

        if (hover != MoveSector || _submenuTimer is not null)
        {
            return;
        }

        DeferSubmenu(() =>
        {
            if (_overlay is null || _busy || _overlay.InMoveMode || _overlay.HoverSector != MoveSector)
            {
                return;
            }

            if (Destinations.Count == 0)
            {
                Log.Warn("'Mover a' sin carpetas de destino.");
                _overlay.ShowStatus("Sin destinos");
                HideAfter(TimeSpan.FromMilliseconds(1400));
                return;
            }

            var labels = new string[Destinations.Count];
            var folders = new string[Destinations.Count];

            for (var index = 0; index < Destinations.Count; index++)
            {
                labels[index] = Destinations[index].Label;
                folders[index] = Destinations[index].Folder;
            }

            _overlay.ShowDestinations(labels, folders);
            Log.Info($"Submenu 'Mover a': {string.Join(", ", labels)}");
        });
    }

    private void DeferSubmenu(Action action)
    {
        _submenuTimer = new DispatcherTimer { Interval = SubmenuDelay };
        _submenuTimer.Tick += (_, _) =>
        {
            _submenuTimer?.Stop();
            _submenuTimer = null;
            action();
        };
        _submenuTimer.Start();
    }

    private static List<Destination> BuildDestinations()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new[]
        {
            new Destination("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new Destination("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new Destination("Descargas", Path.Combine(profile, "Downloads")),
            new Destination("Imagenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            new Destination("Musica", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            new Destination("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };

        var found = new List<Destination>();

        foreach (var candidate in candidates)
        {
            if (candidate.Folder.Length > 0 && Directory.Exists(candidate.Folder))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    private sealed record Destination(string Label, string Folder);

    private void ShowProgress(string label, DuplicateProgress progress)
    {
        if (_overlay is null)
        {
            return;
        }

        var line = progress.Total is int total
            ? $"{progress.Completed} de {total}"
            : $"{progress.Completed} elementos";

        _overlay.ShowStatus($"{label}{Environment.NewLine}{line}{Environment.NewLine}{Shorten(progress.Current)}");
    }

    private static string Shorten(string name)
        => name.Length <= MaxNameChars ? name : name.Substring(0, MaxNameChars - 3) + "...";
    private static string Execute(int sector, string[] paths, IProgress<DuplicateProgress>? progress = null, string? destination = null)
    {
        if (sector == MoveSector)
        {
            if (destination is null)
            {
                return "Sin destino";
            }

            var moved = MoveAction.Run(paths, destination, progress);

            foreach (var path in moved.Moved)
            {
                Log.Info($"Movido: {path}");
            }

            foreach (var failure in moved.Failures)
            {
                Log.Warn($"No movido: {failure}");
            }

            return moved.Summary;
        }

        if (sector == CompressSector)

        {
            var result = CompressAction.Run(paths, progress);

            if (result.Archive.Length > 0)
            {
                Log.Info($"Comprimido: {result.Archive} ({result.Files} archivos)");
            }

            if (result.Error.Length > 0)
            {
                Log.Warn($"Compresion fallida: {result.Error}");
            }

            foreach (var failure in result.Failures)
            {
                Log.Warn($"No comprimido: {failure}");
            }

            return result.Summary;
        }
        if (sector == DeleteSector)
        {
            var result = DeleteAction.Run(paths, progress);

            foreach (var deleted in result.Deleted)
            {
                Log.Info($"A la papelera: {deleted}");
            }

            foreach (var failure in result.Failures)
            {
                Log.Warn($"No borrado: {failure}");
            }

            foreach (var warning in result.Warnings)
            {
                Log.Warn($"Papelera: {warning}");
            }

            return result.Summary;
        }

        if (sector == CopyPathSector)
        {
            PathClipboard.TryCopy(paths, out var message);
            return message;
        }
        if (sector == DuplicateSector)
        {
            var result = DuplicateAction.Run(paths, progress);
            foreach (var created in result.Created)
            {
                Log.Info($"Duplicado: {created}");
            }

            foreach (var failure in result.Failures)
            {
                Log.Warn($"No duplicado: {failure}");
            }

            return result.Summary;
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