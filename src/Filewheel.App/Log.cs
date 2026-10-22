using System;
using System.IO;
using System.Text;

namespace Filewheel.App;

internal static class Log
{
    private static readonly object Gate = new();
    private static string? _filePath;
    private static bool _consoleReady;

    internal static string? FilePath => _filePath;

    internal static void Start()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Filewheel");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "filewheel.log");

            var info = new FileInfo(_filePath);
            if (info.Exists && info.Length > 1024 * 1024)
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex)
        {
            _filePath = null;
            System.Diagnostics.Debug.WriteLine($"Log no disponible: {ex.Message}");
        }

        _consoleReady = NativeMethods.AttachConsole(NativeMethods.AttachParentProcess)
            || Console.IsOutputRedirected;
    }

    internal static void Info(string message) => Write("INFO ", message);

    internal static void Warn(string message) => Write("WARN ", message);

    internal static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null
            ? message
            : $"{message} -> {Describe(ex)}{Environment.NewLine}{ex}");

    internal static string Describe(Exception ex)
        => ex is System.Runtime.InteropServices.COMException com
            ? $"{com.GetType().Name} 0x{com.HResult:X8}: {com.Message}"
            : $"{ex.GetType().Name}: {ex.Message}";

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                if (_filePath is not null)
                {
                    File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                
            }

            System.Diagnostics.Debug.WriteLine(line);

            if (_consoleReady)
            {
                try
                {
                    Console.WriteLine(line);
                    Console.Out.Flush();
                }
                catch
                {
                    _consoleReady = false;
                }
            }
        }
    }
}