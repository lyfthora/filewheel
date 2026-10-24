using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Filewheel.App;

/// <summary>
/// Pide un nombre nuevo. No sabe renombrar: solo pregunta y delega en el
/// </summary>
public partial class RenamePrompt : Window
{
    private const string Instructions = "Enter para guardar  ·  Esc para cancelar";
    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));

    private readonly Func<string, string?> _tryCommit;
    private bool _alreadySelected;

    public RenamePrompt(string originalName, string startText, Func<string, string?> tryCommit)
    {
        InitializeComponent();

        _tryCommit = tryCommit;
        OriginalText.Text = $"Renombrar  {originalName}";
        NameBox.Text = startText;
        ShowHint(Instructions, isError: false);

        Activated += (_, _) => FocusName();
    }

    /// <summary>Coloca la ventana encima del cursor y le da el teclado.</summary>
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

        Show();

        NativeMethods.ForceForeground(hwnd);
        Activate();
        FocusName();
    }

    private void FocusName()
    {
        NameBox.Focus();

        if (_alreadySelected)
        {
            return;
        }

        NameBox.SelectAll();
        _alreadySelected = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Commit();
        }
    }

    private void Commit()
    {
        var error = _tryCommit(NameBox.Text);

        if (error is null)
        {
            Close();
            return;
        }

        ShowHint(error, isError: true);
        NameBox.Focus();
    }

    private void ShowHint(string message, bool isError)
    {
        HintText.Text = message;
        HintText.Foreground = isError ? ErrorBrush : HintBrush;
    }
}