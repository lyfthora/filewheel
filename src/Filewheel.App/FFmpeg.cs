using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Filewheel.App;

/// <summary>
/// Conversion de video y audio delegando en ffmpeg: WPF solo sabe de imagenes.
/// No se empaqueta con la app, se busca al lado del exe (carpeta 'tools') o en el PATH,
/// asi que sin ffmpeg la app sigue funcionando para todo lo demas.
/// </summary>
internal static class FFmpeg
{
    private const string FileName = "ffmpeg.exe";
    private static string? _exe;
    private static bool _searched;

    internal static bool Available => Exe is not null;

    internal static string? Exe
    {
        get
        {
            if (!_searched)
            {
                _searched = true;
                _exe = Locate();
            }

            return _exe;
        }
    }

    private static string? Locate()
    {
        // 1) al lado del ejecutable: tools\ffmpeg.exe (mover la carpeta y sigue funcionando)
        var beside = Path.Combine(AppContext.BaseDirectory, "tools", FileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        var flat = Path.Combine(AppContext.BaseDirectory, FileName);
        if (File.Exists(flat))
        {
            return flat;
        }

        // 2) en el PATH (winget, chocolatey, scoop...)
        // 3) y en el PATH del usuario guardado en el registro: un proceso arrancado con el
        //    entorno viejo no ve lo que se instalo despues de abrir la sesion.
        return Scan(Environment.GetEnvironmentVariable("PATH"))
            ?? Scan(RegistryPath());
    }

    private static string? Scan(string? pathValue)
    {
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));

            if (expanded.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(expanded, FileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? RegistryPath()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Environment", "Path", null) as string;
        }
        catch (Exception)
        {
            // Sin acceso al registro se sigue con lo que diga el entorno.
            return null;
        }
    }

    /// <summary>
    /// Convierte a un temporal con la extension final (ffmpeg elige el contenedor por la
    /// extension) y solo deja el nombre definitivo si salio bien. El original no se toca.
    /// </summary>
    internal static void ConvertTo(string source, string destination, string format)
    {
        var exe = Exe ?? throw new InvalidOperationException("Falta ffmpeg");

        // '.convirtiendo' delante de la extension: ffmpeg necesita verla para saber el contenedor.
        var temp = destination + ".convirtiendo." + format;

        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            info.ArgumentList.Add("-y");
            info.ArgumentList.Add("-nostdin");
            info.ArgumentList.Add("-loglevel");
            info.ArgumentList.Add("error");
            info.ArgumentList.Add("-i");
            info.ArgumentList.Add(source);
            info.ArgumentList.Add(temp);

            using var process = Process.Start(info)
                ?? throw new IOException("ffmpeg no arranco");

            // Los dos flujos se leen antes de esperar: si se llena el buffer, ffmpeg se cuelga.
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();

            process.WaitForExit();

            var message = errors.GetAwaiter().GetResult();
            _ = output.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new IOException(FirstLine(message, process.ExitCode));
            }

            var produced = new FileInfo(temp);

            if (!produced.Exists || produced.Length == 0)
            {
                throw new IOException("ffmpeg no escribio nada");
            }

            File.Move(temp, destination);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // Un temporal que no se deja borrar no debe tapar el resultado.
                }
            }
        }
    }

    /// <summary>
    /// La primera linea util del error de ffmpeg. Se quita el prefijo con la direccion de
    /// memoria ('[in#0 @ 000001eb...]') porque no le dice nada a nadie.
    /// </summary>
    private static string FirstLine(string message, int exitCode)
    {
        foreach (var line in message.Split('\n'))
        {
            var text = StripContext(line.Trim());

            if (text.Length == 0)
            {
                continue;
            }

            // Los dos avisos tipicos de "esto no es un video": mejor decirlo en castellano.
            if (text.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase)
                || text.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
            {
                return "no parece un video valido";
            }

            return text;
        }

        return $"ffmpeg fallo (codigo {exitCode})";
    }

    private static string StripContext(string line)
    {
        if (!line.StartsWith('['))
        {
            return line;
        }

        var close = line.IndexOf(']');
        return close > 0 && close + 1 < line.Length
            ? line.Substring(close + 1).Trim()
            : line;
    }
}
