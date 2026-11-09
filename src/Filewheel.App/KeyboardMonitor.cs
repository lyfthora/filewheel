using System;
using System.Runtime.InteropServices;

namespace Filewheel.App;

/// <summary>
/// Hermano de DragMonitor, pero para el teclado: avisa cuando se pulsa Shift + Space
/// sin arrastrar nada y cuando se suelta Shift.
/// Mira los eventos y los deja pasar (no los roba): lo que escribas sigue llegando a la
/// ventana de delante, porque Filewheel solo necesita enterarse, no interceptar.
/// </summary>
internal sealed class KeyboardMonitor : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _spaceConsumed;   // Space ya avisado; la autorepeticion no cuenta

    internal KeyboardMonitor()
    {
        _callback = OnKeyboardEvent;
    }

    /// <summary>Shift pulsado y Space pulsado: el usuario quiere la rueda general.</summary>
    internal event Action? GeneralRequested;

    /// <summary>Se solto Shift (o se pulso Escape): lo abierto con el gesto se cierra.</summary>
    internal event Action? DismissRequested;

    internal void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookExKeyboard(
            NativeMethods.WhKeyboardLl,
            _callback,
            IntPtr.Zero,
            0);

        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No se pudo instalar el hook del teclado (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);

            if (message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
            {
                HandleKeyDown(data.VirtualKey);
            }
            else if (message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
            {
                HandleKeyUp(data.VirtualKey);
            }
        }

        // Siempre al siguiente hook: este programa mira, no bloquea.
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void HandleKeyDown(int virtualKey)
    {
        if (virtualKey == NativeMethods.VkSpace)
        {
            if (!IsShiftDown() || _spaceConsumed)
            {
                return;
            }

            _spaceConsumed = true;
            GeneralRequested?.Invoke();
            return;
        }

        if (virtualKey == NativeMethods.VkEscape)
        {
            _spaceConsumed = false;
            DismissRequested?.Invoke();
        }
    }

    private void HandleKeyUp(int virtualKey)
    {
        // Soltar Space no cierra nada: el gesto es mantener Shift y tocar Space.
        if (virtualKey == NativeMethods.VkSpace)
        {
            _spaceConsumed = false;
            return;
        }

        // Windows manda LShift/RShift al soltar, y VK_SHIFT en algunos equipos.
        if (virtualKey is NativeMethods.VkLShift or NativeMethods.VkRShift or NativeMethods.VkShift)
        {
            _spaceConsumed = false;
            DismissRequested?.Invoke();
        }
    }

    private static bool IsShiftDown()
        => (NativeMethods.GetAsyncKeyState(NativeMethods.VkShift) & 0x8000) != 0;
}
