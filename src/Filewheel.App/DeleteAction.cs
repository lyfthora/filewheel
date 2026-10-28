using System;
using System.Collections.Generic;
using System.IO;
using Filewheel.Core;

namespace Filewheel.App;

/// <summary>
/// Manda a la papelera lo que se suelta. Nunca borra para siempre: si el shell
/// no puede reciclar, lo dice y deja el archivo donde esta.
/// </summary>
public static class DeleteAction
{
    public static DeleteResult Run(IReadOnlyList<string> paths, IProgress<DuplicateProgress>? progress = null)
    {
        var result = new DeleteResult();

        if (paths is null || paths.Count == 0)
        {
            return result;
        }

        var reporter = new ProgressReporter(progress, paths.Count);

        var before = NativeMethods.RecycleBinItems();

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                result.Failures.Add($"{name}: ya no existe");
                reporter.ItemDone(name);
                continue;
            }

            if (!CanRecycle(path, out var reason))
            {
                result.Failures.Add($"{name}: {reason}");
                reporter.ItemDone(name);
                continue;
            }

            var code = NativeMethods.DeleteToRecycleBin(path);

            if (code != 0)
            {
                result.Failures.Add($"{name}: {Describe(code)}");
                reporter.ItemDone(name);
                continue;
            }

            result.Deleted.Add(path);
            reporter.ItemDone(name);
        }

        var after = NativeMethods.RecycleBinItems();

        if (before >= 0 && after >= 0 && after - before < result.Deleted.Count)
        {
            result.Warnings.Add($"{result.Deleted.Count - (after - before)} de {result.Deleted.Count} sin pasar por la papelera");
        }

        reporter.Finish();
        return result;
    }

    public static bool CanRecycle(string path, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrEmpty(path))
        {
            reason = "Ruta vacia.";
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "En la red no hay papelera: no se borra.";
            return false;
        }

        var root = Path.GetPathRoot(path);

        if (string.IsNullOrEmpty(root))
        {
            reason = "La ruta no tiene unidad.";
            return false;
        }

        try
        {
            var type = new DriveInfo(root).DriveType;

            if (type is DriveType.Network or DriveType.NoRootDirectory)
            {
                reason = "Esa unidad no tiene papelera: no se borra.";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = $"No se pudo consultar la unidad: {ex.GetType().Name}";
            return false;
        }

        return true;
    }

    private static string Describe(int code) => code switch
    {
        0x2 => "ya no existe",
        0x5 => "sin permisos",
        0x20 or 0x21 => "esta abierto en otro programa",
        0x7C => "algo de dentro esta abierto, o sin permisos",
        0x75 => "cancelado",
        _ => $"el shell devolvio 0x{code:X}",
    };
}

public sealed class DeleteResult
{
    public List<string> Deleted { get; } = new();

    public List<string> Failures { get; } = new();

    public List<string> Warnings { get; } = new();

    public string Summary
    {
        get
        {
            if (Deleted.Count == 0 && Failures.Count == 0)
            {
                return "Nada que borrar";
            }

            if (Failures.Count == 0)
            {
                return Deleted.Count == 1 ? "A la papelera" : $"{Deleted.Count} a la papelera";
            }

            if (Deleted.Count == 0)
            {
                return "No se pudo borrar";
            }

            return $"{Deleted.Count} ok, {Failures.Count} fallo(s)";
        }
    }
}