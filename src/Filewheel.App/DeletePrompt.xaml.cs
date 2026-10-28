using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Filewheel.App;

/// <summary>
/// Pregunta antes de mandar algo a la papelera. No borra nada: solo dice si el
/// usuario confirmo, y el que llama decide que hacer con esa respuesta.
/// </summary>
public partial class DeletePrompt : Window
{
    private const string Instructions = "Enter para borrar  ·  Esc para cancelar";
    private const int ArmingMilliseconds = 300;

    private readonly Stopwatch _sinceShown = new();

    public DeletePrompt(string subject)
    {
        InitializeComponent();

        SubjectText.Text = subject;
        HintText.Text = Instructions;

        Activated += (_, _) => Root.Focus();
    }

    public bool Confirmed { get; private set; }

    public void ShowNear(int screenX, int screenY)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            screenX - (width / 2),
            screenY - height - (int)Math.Round(24 * scale),
            width,
            height,
            0);

        _sinceShown.Reset();
        _sinceShown.Start();

        Show();

        NativeMethods.ForceForeground(hwnd);
        Activate();
        Root.Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;


        if (_sinceShown.ElapsedMilliseconds < ArmingMilliseconds)
        {
            HintText.Text = "Pulsa Enter otra vez para confirmar";
            return;
        }

        Confirmed = true;
        Close();
    }
}