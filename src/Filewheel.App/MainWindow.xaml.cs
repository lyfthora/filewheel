using System;
using System.Windows;
using System.Windows.Interop;

namespace Filewheel.App;

public partial class MainWindow : Window
{
    private static readonly string[] MainLabels =
{
        "Comprimir",
        "Convertir",
        "Copiar ruta",
        "Renombrar",
        "Mover a",
        "Duplicar",
        "Eliminar",
        "Ajustes",
    };

    private double _scale = 1.0;
    private double _left;
    private double _top;
    private IReadOnlyList<string>? _destinations;
    private IReadOnlyList<string>? _destinationFolders;

    public MainWindow()
    {
        InitializeComponent();

        Wheel.Labels = MainLabels;
    }

    public void ShowAt(int screenX, int screenY)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.ApplyOverlayStyles(hwnd);

        _scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var size = (int)Math.Round(Width * _scale);
        _left = screenX - (size / 2.0);
        _top = screenY - (size / 2.0);

        Opacity = 0;
        Show();
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            (int)_left,
            (int)_top,
            size,
            size,
            NativeMethods.SwpNoActivate);
        Opacity = 1;
    }

    /// <summary>Resalta el sector que hay bajo el cursor.</summary>
    public void UpdateHover(int screenX, int screenY)
    {
        Wheel.HoverIndex = ResolveSector(screenX, screenY);
    }

    /// <summary>Devuelve el índice del sector bajo un punto de pantalla, o -1 si no hay ninguno.</summary>
    public int ResolveSector(int screenX, int screenY)
    {
        if (!IsVisible || _scale <= 0)
        {
            return -1;
        }

        var local = new Point(
            (screenX - _left) / _scale,
            (screenY - _top) / _scale);
        return Wheel.HitTestSector(local);
    }

    public void ShowDestinations(IReadOnlyList<string> labels, IReadOnlyList<string> folders)
    {
        _destinations = labels;
        _destinationFolders = folders;
        Wheel.Labels = labels;
        Wheel.HoverIndex = -1;
        Wheel.CenterText = "Suelta en el destino";
    }

    public void RestoreMainRing()
    {
        if (_destinations is null)
        {
            return;
        }

        _destinations = null;
        _destinationFolders = null;
        Wheel.Labels = MainLabels;
        Wheel.HoverIndex = -1;
        Wheel.CenterText = string.Empty;
    }

    public bool InMoveMode => _destinations is not null;

    public int HoverSector => Wheel.HoverIndex;

    /// <summary>La carpeta del destino, no la etiqueta que se pinta.</summary>
    public string? DestinationAt(int sector)
        => _destinationFolders is not null && sector >= 0 && sector < _destinationFolders.Count
            ? _destinationFolders[sector]
            : null;

    public void HideWheel()
    {
        RestoreMainRing();
        Wheel.HoverIndex = -1;
        Wheel.CenterText = string.Empty;
        if (IsVisible)
        {
            Hide();
        }
    }
    // se soltaron archivbos sobre la rueda; (sector, rutas). sector = -1 si nno habia sector
    public event Action<DropRequest>? FilesDropped;
    public string LabelFor(int sector) => sector >= 0 && sector < Wheel.Labels.Count ? Wheel.Labels[sector] : string.Empty;
    public void ShowStatus(string message)
    {
        Wheel.CenterText = message;
    }
    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = AcceptDrop(e.Data);
        e.Handled = true;
    }
    private void OnDropFiles(object sender, DragEventArgs e)
    {
        e.Handled = true;
        NativeMethods.GetCursorPos(out var cursor);
        HandleDrop(e.Data, cursor.X, cursor.Y);
    }
    // solo aceptmosa arrastres que traen rutas de archivos
    private static DragDropEffects AcceptDrop(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    //convierte el contenido sltado en (sector, rutas)y avisa
    private void HandleDrop(IDataObject data, int screenX, int screenY)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var sector = ResolveSector(screenX, screenY);
        FilesDropped?.Invoke(new DropRequest(sector, DestinationAt(sector), paths));
    }
}

public sealed record DropRequest(int Sector, string? Destination, string[] Paths);