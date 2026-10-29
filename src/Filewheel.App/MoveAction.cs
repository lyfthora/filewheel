using System;
using System.Collections.Generic;
using System.IO;

namespace Filewheel.Core;

/// <summary>
/// Mueve lo que se suelta a una carpeta destino. Nunca sobrescribe: si ya hay
/// algo con ese nombre, lo dice y deja las dos cosas donde estan.
/// </summary>
public static class MoveAction
{
    public static MoveResult Run(IReadOnlyList<string> paths, string destination, IProgress<DuplicateProgress>? progress = null)
    {
        var result = new MoveResult();

        if (paths is null || paths.Count == 0)
        {
            return result;
        }

        if (string.IsNullOrEmpty(destination) || !Directory.Exists(destination))
        {
            result.Error = "Esa carpeta de destino no existe.";
            return result;
        }

        var reporter = new ProgressReporter(progress, paths.Count);

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);

            try
            {
                var isDirectory = Directory.Exists(path);
                var isFile = File.Exists(path);

                if (!isDirectory && !isFile)
                {
                    result.Failures.Add($"{name}: ya no existe");
                    reporter.ItemDone(name);
                    continue;
                }

                if (SameFolder(Path.GetDirectoryName(path), destination))
                {
                    result.Failures.Add($"{name}: ya esta ahi");
                    reporter.ItemDone(name);
                    continue;
                }

                var target = Path.Combine(destination, name);

                if (File.Exists(target) || Directory.Exists(target))
                {
                    result.Failures.Add($"{name}: ya existe en el destino");
                    reporter.ItemDone(name);
                    continue;
                }

                if (isDirectory && !SameVolume(path, destination))
                {
                    result.Failures.Add($"{name}: carpeta en otra unidad, no se mueve");
                    reporter.ItemDone(name);
                    continue;
                }

                if (isDirectory)
                {
                    Directory.Move(path, target);
                }
                else
                {
                    File.Move(path, target);
                }

                result.Moved.Add(target);
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }

            reporter.ItemDone(name);
        }

        reporter.Finish();
        return result;
    }

    private static bool SameFolder(string? folder, string destination)
        => folder is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(folder),
            Path.TrimEndingDirectorySeparator(destination),
            StringComparison.OrdinalIgnoreCase);

    private static bool SameVolume(string source, string destination)
        => string.Equals(
            Path.GetPathRoot(source),
            Path.GetPathRoot(destination),
            StringComparison.OrdinalIgnoreCase);
}

public sealed class MoveResult
{
    public List<string> Moved { get; } = new();

    public List<string> Failures { get; } = new();

    public string Error { get; set; } = string.Empty;

    public string Summary
    {
        get
        {
            if (Error.Length > 0)
            {
                return Error;
            }

            if (Moved.Count == 0 && Failures.Count == 0)
            {
                return "Nada que mover";
            }

            if (Failures.Count == 0)
            {
                return Moved.Count == 1 ? "Movido" : $"Movidos {Moved.Count}";
            }

            if (Moved.Count == 0)
            {
                return "No se pudo mover";
            }

            return $"{Moved.Count} movidos, {Failures.Count} fallo(s)";
        }
    }
}