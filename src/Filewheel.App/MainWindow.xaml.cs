using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace Filewheel.App;

public partial class MainWindow : Window
{
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

    public void ShowAtCursor()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.ApplyOverlayStyles(hwnd);

        NativeMethods.GetCursorPos(out var cursor);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var size = (int)Math.Round(Width * scale);

        Opacity = 0;
        Show();
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            cursor.X - (size / 2),
            cursor.Y - (size / 2),
            size,
            size,
            NativeMethods.SwpNoActivate);
        Opacity = 1;
    }

    private void OnWheelMouseMove(object sender, MouseEventArgs e)
    {
        Wheel.HoverIndex = Wheel.HitTestSector(e.GetPosition(Wheel));
    }
    private void OnWheelMouseLeave(object sender, MouseEventArgs e)
    {
        Wheel.HoverIndex = -1;
    }
}