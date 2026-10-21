using System;
using System.Windows;
using System.Windows.Interop;

namespace Filewheel.App;

public partial class MainWindow : Window
{
    private double _scale = 1.0;
    private double _left;
    private double _top;

    public MainWindow()
    {
        InitializeComponent();

        Wheel.Labels = new[]
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

    public void HideWheel()
    {
        Wheel.HoverIndex = -1;
        if (IsVisible)
        {
            Hide();
        }
    }
}