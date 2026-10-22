using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Filewheel.App;

internal static class PathClipboard
{
    private const int MaxAttempts = 5;
    private const int RetryDelayMs = 40;

    internal static bool TryCopy(string[] paths, out string message)
    {
        var text = string.Join(Environment.NewLine, paths);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (TrySet(text, out var error))
            {
                Log.Info($"Portapapeles: {paths.Length} ruta(s) copiadas en el intento {attempt}.");
                message = "Ruta copiada";
                return true;
            }

            Log.Warn($"Portapapeles: intento {attempt}/{MaxAttempts} fallo: {error}.");

            if (attempt < MaxAttempts)
            {
                System.Threading.Thread.Sleep(RetryDelayMs);
            }
        }

        Log.Error($"Portapapeles: se agotaron los {MaxAttempts} intentos.");
        message = "No se pudo copiar";
        return false;
    }

    private static bool TrySet(string text, out string error)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            error = $"OpenClipboard fallo (Win32 {Marshal.GetLastWin32Error()})";
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                error = $"EmptyClipboard fallo (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }

            // Texto UTF-16 terminado en cero, en un bloque que Windows pueda mover.
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
            {
                error = $"GlobalAlloc fallo (Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }

            var target = NativeMethods.GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                error = $"GlobalLock fallo (Win32 {Marshal.GetLastWin32Error()})";
                NativeMethods.GlobalFree(handle);
                return false;
            }

            Marshal.Copy(bytes, 0, target, bytes.Length);
            NativeMethods.GlobalUnlock(handle);

            if (NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, handle) == IntPtr.Zero)
            {
                error = $"SetClipboardData fallo (Win32 {Marshal.GetLastWin32Error()})";
                NativeMethods.GlobalFree(handle);
                return false;
            }

            error = string.Empty;
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }
}