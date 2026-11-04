using System;
using System.Collections.Generic;
using System.IO;

namespace Filewheel.App;

/// <summary>
/// Que clase de archivo se esta arrastrando. Se deduce de la extension, que es lo unico
/// que sabemos sin abrir el archivo (abrir un video para leer su cabecera tarda y puede fallar).
/// </summary>
public enum FileKind
{
    None,
    Folder,
    Image,
    Video,
    Audio,
    Document,
    Archive,
    Other,
    Mixed,
}

/// <summary>Extensiones conocidas por familia y utilidades para describirlas en pantalla.</summary>
public static class FileKinds
{
    // Solo los que WPF sabe leer de verdad: si se anade webp o heic aqui, Convertir fallara.
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif",
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".ts", ".mlv",
    };

    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".aiff",
    };

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".txt", ".md", ".rtf", ".odt", ".xls", ".xlsx", ".csv", ".ppt", ".pptx",
    };

    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".iso",
    };

    /// <summary>La clase comun a todo lo arrastrado, o Mixed si no coinciden.</summary>
    public static FileKind FromPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return FileKind.None;
        }

        var first = KindOf(paths[0]);

        for (var index = 1; index < paths.Count; index++)
        {
            if (KindOf(paths[index]) != first)
            {
                return FileKind.Mixed;
            }
        }

        return first;
    }

    public static FileKind KindOf(string path)
    {
        if (Directory.Exists(path))
        {
            return FileKind.Folder;
        }

        var extension = Path.GetExtension(path);

        if (extension.Length == 0)
        {
            return FileKind.Other;
        }

        if (Images.Contains(extension)) return FileKind.Image;
        if (Videos.Contains(extension)) return FileKind.Video;
        if (Audios.Contains(extension)) return FileKind.Audio;
        if (Documents.Contains(extension)) return FileKind.Document;
        if (Archives.Contains(extension)) return FileKind.Archive;

        return FileKind.Other;
    }

    /// <summary>Nombre corto para el centro de la rueda.</summary>
    public static string Label(FileKind kind) => kind switch
    {
        FileKind.Folder => "Carpeta",
        FileKind.Image => "Imagen",
        FileKind.Video => "Video",
        FileKind.Audio => "Audio",
        FileKind.Document => "Documento",
        FileKind.Archive => "Comprimido",
        FileKind.Mixed => "Varios tipos",
        FileKind.Other => "Archivo",
        _ => string.Empty,
    };

    /// <summary>La extension en mayusculas, para ensenarla tal cual: PNG, JPG, MP4...</summary>
    public static string Extension(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        return extension.Length == 0 ? string.Empty : extension.ToUpperInvariant();
    }
}