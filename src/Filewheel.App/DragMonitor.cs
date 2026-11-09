using System;
using System.Runtime.InteropServices;

namespace Filewheel.App;

internal sealed class DragMonitor : IDisposable
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int DragThresholdPixels = 6;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _hook;
    private bool _buttonDown;
    private bool _dragging;
    private int _originX;
    private int _originY;
    private bool _armed;
    private DragModifier _modifier;

    internal DragMonitor()
    {
        _callback = OnMouseEvent;
    }
    /// <summary>
    /// shift o ctrl + left click y movimiento suficiente. El modificador dice cual de las
    /// dos ruedas toca: Shift (formatos/submenus) o Control (la general).
    /// </summary>
    internal event Action<int, int, DragModifier>? DragStarted;
    /// <summary>
    /// cursor moved while the left mouse button was held down
    /// </summary>
    internal event Action<int, int>? Moved;
    /// <summary>
    /// the left mouse button was released while Shift was still held down
    /// </summary>
    internal event Action<int, int>? Dropped;
    /// <summary>
    /// the drag ended without a selection (Shift was released before the mouse button)
    /// </summary>
    internal event Action? Cancelled;
    /// <summary>El cursor se movio, con boton pulsado o sin el: la rueda general tambien lo necesita.</summary>
    internal event Action<int, int>? PointerMoved;
    internal void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
            $"No se pudo instalar el hook del ratón (error {Marshal.GetLastWin32Error()}).");

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
    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
            switch ((int)wParam)
            {
                case NativeMethods.WmLButtonDown:
                    HandleButtonDown(data.Point);
                    break;
                case NativeMethods.WmMouseMove:
                    PointerMoved?.Invoke(data.Point.X, data.Point.Y);
                    HandleMove(data.Point);
                    break;
                case NativeMethods.WmLButtonUp:
                    HandleButtonUp(data.Point);
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
    private void HandleButtonDown(NativeMethods.Point point)
    {
        if (_buttonDown)
        {
            return;
        }
        _buttonDown = true;
        _dragging = false;
        _armed = false;
        _modifier = DragModifier.None;
        _originX = point.X;
        _originY = point.Y;
    }
    private void HandleMove(NativeMethods.Point point)
    {
        if (!_buttonDown)
        {
            return;
        }
        if (!_dragging)
        {
            var dx = point.X - _originX;
            var dy = point.Y - _originY;
            if ((dx * dx) + (dy * dy) < DragThresholdPixels * DragThresholdPixels)
            {
                return;
            }
            _dragging = true;
        }

        var modifier = CurrentModifier();

        if (!_armed)
        {
            if (modifier == DragModifier.None)
            {
                return;
            }
            _armed = true;
            _modifier = modifier;
            DragStarted?.Invoke(point.X, point.Y, modifier);
            return;
        }

        if (modifier != _modifier)
        {
            // Cambiar de tecla a mitad del arrastre (o soltarla) cancela: la rueda ya no vale.
            Cancel();
            return;
        }

        Moved?.Invoke(point.X, point.Y);
    }
    private void HandleButtonUp(NativeMethods.Point point)
    {
        if (!_buttonDown)
        {
            return;
        }
        var wasArmed = _armed;
        _buttonDown = false;
        _dragging = false;
        _armed = false;
        _modifier = DragModifier.None;
        if (!wasArmed)
        {
            return;
        }
        if (CurrentModifier() != DragModifier.None)
        {
            Dropped?.Invoke(point.X, point.Y);
        }
        else
        {
            Cancelled?.Invoke();
        }
    }
    private void Cancel()
    {
        _buttonDown = false;
        _dragging = false;
        _armed = false;
        _modifier = DragModifier.None;
        Cancelled?.Invoke();
    }

    /// <summary>Shift manda: si estan las dos pulsadas, el arrastre es el de formatos.</summary>
    private static DragModifier CurrentModifier()
    {
        if (IsDown(VkShift))
        {
            return DragModifier.Shift;
        }

        return IsDown(VkControl) ? DragModifier.Control : DragModifier.None;
    }

    private static bool IsDown(int virtualKey)
       => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}

/// <summary>Con que tecla se armo el arrastre: Shift (formatos) o Control (rueda general).</summary>
internal enum DragModifier
{
    None,
    Shift,
    Control,
}