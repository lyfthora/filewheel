using System;
using System.IO;

namespace Filewheel.Core;

/// <summary>
/// Renombra un solo archivo o carpeta en el mismo sitio donde esta.
/// </summary>
public static class RenameAction
{
    public static RenameResult Run(string path, string typedName)
    {
        var result = new RenameResult { Source = path };
        var name = (typedName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            result.Error = "Escribe un nombre.";
            return result;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            result.Error = "El nombre no puede llevar < > : \" / \\ | ? *";
            return result;
        }

        if (name is "." or "..")
        {
            result.Error = "Ese nombre no vale.";
            return result;
        }

        if (name.EndsWith('.'))
        {
            result.Error = "Windows no admite nombres que acaben en punto.";
            return result;
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            result.Error = "La ruta no tiene carpeta contenedora.";
            return result;
        }

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            result.Error = "El original ya no existe.";
            return result;
        }

        // Si se escribe "foto" sobre "foto.jpg", el resultado es "foto.jpg":
        // perder la extension por no teclearla seria una sorpresa muy cara.
        var extension = isDirectory ? string.Empty : Path.GetExtension(path);
        var finalName = Path.GetExtension(name).Length == 0 ? name + extension : name;
        var target = Path.Combine(directory, finalName);

        if (target == path)
        {
            result.Error = "El nombre no ha cambiado.";
            return result;
        }

        var onlyCaseChanges = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);

        if (!onlyCaseChanges && (File.Exists(target) || Directory.Exists(target)))
        {
            result.Error = $"Ya existe algo llamado '{finalName}'.";
            return result;
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            return result;
        }

        result.Success = true;
        result.Target = target;
        return result;
    }
}

public sealed class RenameResult
{
    public string Source { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Target { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public string Summary => Success ? "Renombrado" : Error;
}