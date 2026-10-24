using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace Filewheel.Core;

public static class DuplicateAction
{
    internal const int MaxNameAttempts = 1000;

    public static DuplicateResult Run(IReadOnlyList<string> paths, IProgress<DuplicateProgress>? progress = null)
    {
        var result = new DuplicateResult();

        if (paths is null || paths.Count == 0)
        {
            return result;
        }

        var reporter = new ProgressReporter(progress, KnownTotal(paths));

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var target = NextFreeName(path, isDirectory: true);
                    CopyDirectory(path, target, result, reporter);
                    result.Created.Add(target);
                }
                else if (File.Exists(path))
                {
                    var target = NextFreeName(path, isDirectory: false);
                    File.Copy(path, target, overwrite: false);
                    result.Created.Add(target);
                    reporter.ItemDone(Path.GetFileName(path));
                }
                else
                {
                    result.Failures.Add($"{path}: no existe o sin permisos");
                    reporter.ItemDone(Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                reporter.ItemDone(Path.GetFileName(path));
            }
        }

        reporter.Finish();
        return result;
    }

    private static int? KnownTotal(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                return null;
            }
        }

        return paths.Count;
    }

    private static string NextFreeName(string source, bool isDirectory)
    {
        var directory = Path.GetDirectoryName(source);
        if (directory is null)
        {
            throw new IOException("La ruta no tiene carpeta contenedora.");
        }

        var name = Path.GetFileName(source);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);

        if (extension.Length == name.Length)
        {
            extension = string.Empty;
        }

        var stem = name.Substring(0, name.Length - extension.Length);

        for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
        {
            var suffix = attempt == 1 ? " - copia" : $" - copia ({attempt})";
            var candidate = Path.Combine(directory, stem + suffix + extension);

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Sin nombre libre para '{name}' tras {MaxNameAttempts} intentos.");
    }

    private static void CopyDirectory(string source, string target, DuplicateResult result, ProgressReporter reporter)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            try
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
            }

            reporter.ItemDone(Path.GetFileName(file));
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(source))
        {
            var info = new DirectoryInfo(subdirectory);

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                result.Failures.Add($"{info.Name}: enlace no soportado");
                continue;
            }

            CopyDirectory(subdirectory, Path.Combine(target, info.Name), result, reporter);
        }
    }
}
internal sealed class ProgressReporter
{
    private const int IntervalMs = 100;

    private readonly IProgress<DuplicateProgress>? _progress;
    private readonly int? _total;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long? _lastReport;
    private int _completed;
    private string _current = string.Empty;

    internal ProgressReporter(IProgress<DuplicateProgress>? progress, int? total)
    {
        _progress = progress;
        _total = total;
    }

    internal void ItemDone(string name)
    {
        _completed++;
        _current = name;

        if (_progress is null)
        {
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        if (_lastReport is long last && now - last < IntervalMs)
        {
            return;
        }

        Report(now);
    }

    /// <summary>Ultimo aviso, para que el contador no se quede corto al terminar.</summary>
    internal void Finish()
    {
        if (_progress is null || _completed == 0)
        {
            return;
        }

        Report(_clock.ElapsedMilliseconds);
    }

    private void Report(long now)
    {
        _lastReport = now;
        _progress!.Report(new DuplicateProgress(_completed, _current, _total));
    }
}

/// <summary>cuantos elementos terminaron y cual fue el ultimo.</summary>
/// <param name="Completed">Elementos terminados, con exito o con fallo.</param>
/// <param name="Current">Nombre del ultimo elemento procesado.</param>
/// <param name="Total">Total esperado, o null si hay carpetas y no se conoce por adelantado.</param>
public sealed record DuplicateProgress(int Completed, string Current, int? Total);

public sealed class DuplicateResult
{
    public List<string> Created { get; } = new();

    public List<string> Failures { get; } = new();

    public string Summary
    {
        get
        {
            if (Created.Count == 0 && Failures.Count == 0)
            {
                return "Nada que duplicar";
            }

            if (Failures.Count == 0)
            {
                return Created.Count == 1 ? "Duplicado" : $"{Created.Count} duplicados";
            }

            if (Created.Count == 0)
            {
                return "No se pudo duplicar";
            }

            return $"{Created.Count} ok, {Failures.Count} fallo(s)";
        }
    }
}