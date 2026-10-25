using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Filewheel.Core;

/// <summary>
/// Comprime lo que se suelta en un unico .zip junto al original.
/// No conoce WPF ni el log: devuelve el zip creado y lo que fallo.
/// </summary>
public static class CompressAction
{
    internal const int MaxNameAttempts = 1000;
    private const int BufferSize = 81920;

    public static CompressResult Run(IReadOnlyList<string> paths, IProgress<DuplicateProgress>? progress = null)
    {
        var result = new CompressResult();

        if (paths is null || paths.Count == 0)
        {
            return result;
        }

        var baseDirectory = Path.GetDirectoryName(paths[0]);
        if (string.IsNullOrEmpty(baseDirectory))
        {
            result.Error = "La ruta no tiene carpeta contenedora.";
            return result;
        }

        string target;
        string temporary;

        try
        {
            target = PickTarget(baseDirectory, StemFor(paths));
            temporary = Path.Combine(baseDirectory, $"{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            return result;
        }

        var reporter = new ProgressReporter(progress, KnownTotal(paths));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var path in paths)
                {
                    AddItem(archive, path, string.Empty, result, reporter, used);
                }
            }

            if (result.Files == 0 && result.Failures.Count > 0)
            {
                TryDelete(temporary);
                result.Error = $"Nada comprimido: {result.Failures[0]}";
                return result;
            }

            File.Move(temporary, target);
        }
        catch (Exception ex)
        {
            TryDelete(temporary);
            result.Failures.Clear();
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            return result;
        }

        reporter.Finish();
        result.Archive = target;
        return result;
    }

    private static void AddItem(
        ZipArchive archive,
        string path,
        string prefix,
        CompressResult result,
        ProgressReporter reporter,
        HashSet<string> used)
    {
        if (Directory.Exists(path))
        {
            AddDirectory(archive, path, Join(prefix, Path.GetFileName(path)), result, reporter, used);
            return;
        }

        if (!File.Exists(path))
        {
            result.Failures.Add($"{Path.GetFileName(path)}: no existe o sin permisos");
            reporter.ItemDone(Path.GetFileName(path));
            return;
        }

        AddFile(archive, path, EntryName(used, prefix, Path.GetFileName(path)), result, reporter);
    }

    private static void AddDirectory(
        ZipArchive archive,
        string directory,
        string prefix,
        CompressResult result,
        ProgressReporter reporter,
        HashSet<string> used)
    {
        string[] files;
        string[] subdirectories;

        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                result.Failures.Add($"{prefix}: enlace no soportado");
                return;
            }

            files = Directory.GetFiles(directory);
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (Exception ex)
        {
            result.Failures.Add($"{prefix}: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            AddFile(archive, file, EntryName(used, prefix, Path.GetFileName(file)), result, reporter);
        }

        // Una carpeta vacia sin entrada propia no sobrevive al descomprimir.
        if (files.Length == 0 && subdirectories.Length == 0 && prefix.Length > 0)
        {
            archive.CreateEntry(prefix + "/");
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            AddDirectory(
                archive,
                subdirectory,
                Join(prefix, Path.GetFileName(subdirectory)),
                result,
                reporter,
                used);
        }
    }

    private static void AddFile(
        ZipArchive archive,
        string path,
        string entryName,
        CompressResult result,
        ProgressReporter reporter)
    {
        var name = Path.GetFileName(path);

        FileStream source;

        try
        {
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex)
        {
            result.Failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            reporter.ItemDone(name);
            return;
        }

        using (source)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

            var stamp = File.GetLastWriteTime(path);
            if (stamp.Year >= 1980)
            {
                entry.LastWriteTime = stamp;
            }

            using var entryStream = entry.Open();
            source.CopyTo(entryStream, BufferSize);
        }

        result.Files++;
        reporter.ItemDone(name);
    }

    private static string EntryName(HashSet<string> used, string prefix, string name)
    {
        var candidate = Join(prefix, name);

        if (used.Add(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(name);
        var stem = extension.Length == 0 ? name : name.Substring(0, name.Length - extension.Length);

        for (var attempt = 2; attempt <= MaxNameAttempts; attempt++)
        {
            var alternative = Join(prefix, $"{stem} ({attempt}){extension}");

            if (used.Add(alternative))
            {
                return alternative;
            }
        }

        throw new IOException($"Sin nombre libre para '{candidate}' dentro del zip.");
    }

    private static string Join(string prefix, string name)
        => prefix.Length == 0 ? name : prefix + "/" + name;

    private static string PickTarget(string directory, string stem)
    {
        for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
        {
            var suffix = attempt == 1 ? string.Empty : $" ({attempt})";
            var candidate = Path.Combine(directory, stem + suffix + ".zip");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Sin nombre libre para '{stem}.zip' tras {MaxNameAttempts} intentos.");
    }

    private static string StemFor(IReadOnlyList<string> paths)
        => BareName(paths[0]);

    private static string BareName(string path)
    {
        var name = Path.GetFileName(path);

        if (Directory.Exists(path))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length == 0 ? name : stem;
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Si el temporal tampoco se deja borrar, el error
            // explica mas que este.
        }
    }
}

public sealed class CompressResult
{
    public string Archive { get; set; } = string.Empty;

    public int Files { get; set; }

    public string Error { get; set; } = string.Empty;

    public List<string> Failures { get; } = new();

    public string Summary
    {
        get
        {
            if (Error.Length > 0)
            {
                return Error;
            }

            if (Files == 0 && Failures.Count == 0)
            {
                return "Comprimido: vacio";
            }

            if (Failures.Count == 0)
            {
                return Files == 1 ? "Comprimido" : $"Comprimido, {Files} archivos";
            }

            if (Files == 0)
            {
                return "No se pudo comprimir";
            }

            return $"{Files} ok, {Failures.Count} fallo(s)";
        }
    }
}