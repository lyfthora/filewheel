using System;
using System.Drawing;
using System.Windows.Forms;

namespace Filewheel.App;

/// <summary>
/// El icono de la bandeja: la unica cara visible de Filewheel, que no tiene ventana propia.
/// Se crea en el hilo de UI y hay que liberarlo en OnExit, o el icono fantasma se queda
/// en la bandeja hasta que pasas el raton por encima.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    internal TrayIcon()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Abrir rueda (Shift + Space)");
        open.Click += (_, _) => OpenRequested?.Invoke();

        var settings = new ToolStripMenuItem("Ajustes") { Enabled = false };   // hasta B2

        var toggle = new ToolStripMenuItem("Desactivar") { Enabled = false };
        toggle.Click += (_, _) => ToggleRequested?.Invoke();

        var quit = new ToolStripMenuItem("Salir");
        quit.Click += (_, _) => QuitRequested?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggle);
        menu.Items.Add(quit);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Filewheel  ·  Ctrl+arrastrar / Shift+Space",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Un clic izquierdo abre la rueda: el mismo gesto que Shift + Space.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    internal event Action? OpenRequested;
    internal event Action? ToggleRequested;
    internal event Action? QuitRequested;

    public void Dispose()
    {
        _icon.Visible = false;   // sin esto queda el icono fantasma
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        var exe = Environment.ProcessPath;

        if (exe is not null)
        {
            var embedded = Icon.ExtractAssociatedIcon(exe);

            if (embedded is not null)
            {
                return embedded;   // el .ico que pusiste en <ApplicationIcon>
            }
        }

        return SystemIcons.Application;
    }
}