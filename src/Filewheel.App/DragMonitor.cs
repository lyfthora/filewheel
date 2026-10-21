using System;
using System.Runtime.InteropServices;

namespace Filewheel.App;

internal sealed class DragMonitor : IDisposable
{
    private const int VkShift = 0x10;
    private const int DragThresholdPixels = 6;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _hook;
    private bool _buttonDown;
    private bool _dragging;
    private int _originX;
    private int _originY;
    private bool _shiftArmed;

    internal DragMonitor()
    {
        _callback = OnMouseEvent;
    }
    /// <summary>
    /// shift + left click and mouse move enought
    /// </summary>
    internal event Action<int, int>? DragStarted;
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
        _shiftArmed = false;
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
        var shiftDown = IsShiftDown();
        if (!_shiftArmed)
        {
            if (!shiftDown)
            {
                return;
            }
            _shiftArmed = true;
            DragStarted?.Invoke(point.X, point.Y);
            return;
        }
        if (!shiftDown)
        {
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
        var wasArmed = _shiftArmed;
        _buttonDown = false;
        _dragging = false;
        _shiftArmed = false;
        if (!wasArmed)
        {
            return;
        }
        if (IsShiftDown())
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
        _shiftArmed = false;
        Cancelled?.Invoke();
    }
    private static bool IsShiftDown()
       => (NativeMethods.GetAsyncKeyState(VkShift) & 0x8000) != 0;
}