using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Filewheel.Core;

namespace Filewheel.App;

/// <summary>
/// Convierte imagenes a otro formato usando los codificadores que ya trae WPF:
/// ni paquetes nuevos ni programas externos.
/// Convierte el archivo en su sitio: la imagen nueva ocupa el nombre del original
/// (o el alternativo, si ese ya esta ocupado) y el original se va a la papelera.
/// </summary>
public static class ConvertAction
{
    private const int JpegQuality = 90;

    /// <summary>Formatos ofrecidos en el submenu: en minusculas y sin punto. El orden manda en la rueda.</summary>
    public static readonly string[] Formats = { "png", "jpg", "bmp", "tiff", "gif" };

    public static ConvertResult Run(IReadOnlyList<string> paths, string format, IProgress<DuplicateProgress>? progress = null)
    {
        var result = new ConvertResult();

        if (paths is null || paths.Count == 0)
        {
            return result;
        }

        var target = Normalize(format);

        if (Array.IndexOf(Formats, target) < 0)
        {
            result.Error = "Ese formato no esta soportado.";
            return result;
        }

        var reporter = new ProgressReporter(progress, paths.Count);

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);

            try
            {
                if (!File.Exists(path))
                {
                    result.Failures.Add($"{name}: ya no existe");
                    continue;
                }

                if (SameFormat(path, target))
                {
                    result.Failures.Add($"{name}: ya es {target}");
                    continue;
                }

                // DestinationPath ya devuelve un nombre libre: si 'test.jpg' existe,
                // la conversion acaba en 'testjpg.jpg' y el que ya estaba no se toca.
                var destination = DestinationPath(path, target, out var renamed);

                ConvertInPlace(path, destination, target);
                result.Converted.Add(destination);

                if (renamed)
                {
                    result.Renamed.Add(Path.GetFileName(destination));
                }
            }
            catch (SkipException ex)
            {
                result.Failures.Add($"{name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{name}: {Describe(ex)}");
            }
            finally
            {
                reporter.ItemDone(name);
            }
        }

        reporter.Finish();
        return result;
    }

    /// <summary>
    /// Convierte sin dejar copia. Se escribe al lado con un nombre temporal, se comprueba
    /// que la imagen nueva se puede leer, ocupa el nombre final y solo entonces el original
    /// se va a la papelera. Cualquier fallo antes de ese ultimo paso deja el original intacto.
    /// </summary>
    private static void ConvertInPlace(string source, string destination, string format)
    {
        var temp = destination + ".convirtiendo";

        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            Convert(source, temp, format);
            Verify(temp, source);

            // El mismo volumen y la misma carpeta: el rename no puede fallar a medias.
            File.Move(temp, destination);

            var error = DeleteAction.Recycle(source);

            if (error.Length > 0)
            {
                // Sin original en la papelera no hay conversion en sitio: se deshace.
                File.Delete(destination);
                throw new IOException($"el original no se pudo quitar: {error}");
            }
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

    private static void Convert(string source, string output, string format)
    {
        BitmapFrame frame;

        using (var stream = File.OpenRead(source))
        {
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                throw new FileFormatException("El archivo no contiene imagenes.");
            }

            if (decoder.Frames.Count > 1)
            {
                // Antes sobraba con quedarse con el primer cuadro porque el original seguia
                // ahi. Ahora el original se va: conservar solo uno destruiria los demas.
                throw new SkipException($"tiene {decoder.Frames.Count} imagenes y solo se conservaria una");
            }

            frame = decoder.Frames[0];
        }

        var encoder = CreateEncoder(format);
        encoder.Frames.Add(BitmapFrame.Create(Prepare(frame, format)));

        using var target = File.Create(output);
        encoder.Save(target);
    }

    /// <summary>Relee la imagen recien escrita: si no cuadra con la original, no vale como resultado.</summary>
    private static void Verify(string produced, string source)
    {
        var expected = ReadFrame(source);
        var actual = ReadFrame(produced);

        if (actual.PixelWidth != expected.PixelWidth || actual.PixelHeight != expected.PixelHeight)
        {
            throw new IOException(
                $"la imagen nueva no mide lo mismo ({actual.PixelWidth}x{actual.PixelHeight})");
        }
    }

    private static BitmapFrame ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new FileFormatException("El archivo no contiene imagenes.");
        }

        return decoder.Frames[0];
    }

    private static BitmapSource Prepare(BitmapFrame frame, string format)
    {
        // JPEG y BMP no tienen canal alfa: si lo hay, se aplana sobre blanco para que
        // no salgan bordes negros donde la imagen era transparente.
        if (format is "jpg" or "bmp")
        {
            return HasAlpha(frame)
                ? Flatten(frame)
                : ToFormat(frame, PixelFormats.Bgr24);
        }

        return ToFormat(frame, PixelFormats.Bgra32);
    }

    private static BitmapEncoder CreateEncoder(string format) => format switch
    {
        "png" => new PngBitmapEncoder(),
        "jpg" => new JpegBitmapEncoder { QualityLevel = JpegQuality },
        "bmp" => new BmpBitmapEncoder(),
        "tiff" => new TiffBitmapEncoder(),
        "gif" => new GifBitmapEncoder(),
        _ => throw new ArgumentException($"Formato desconocido: {format}"),
    };

    private static bool HasAlpha(BitmapSource source) => source.Format.Masks.Count == 4;

    private static BitmapSource ToFormat(BitmapSource source, PixelFormat format)
        => source.Format == format ? source : new FormatConvertedBitmap(source, format, null, 0);

    /// <summary>Dibuja la imagen sobre blanco y devuelve el resultado ya sin transparencia.</summary>
    private static BitmapSource Flatten(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        try
        {
            var visual = new DrawingVisual();

            using (var context = visual.RenderOpen())
            {
                var area = new Rect(0, 0, width, height);
                context.DrawRectangle(Brushes.White, null, area);
                context.DrawImage(source, area);
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Dibujar exige un hilo de interfaz en algunas maquinas; si no se puede,
            // se pierde el blanco pero la conversion sigue adelante.
            return ToFormat(source, PixelFormats.Bgr24);
        }
    }

    /// <summary>
    /// El nombre que ocupara el original. Si ya hay algo ahi no se pisa: se prueba el nombre
    /// con el formato pegado ('test.png' -> 'testjpg.jpg') y, si tambien esta ocupado, se numera.
    /// 'renamed' avisa de que el nombre final no es el obvio, para poder decirlo en pantalla.
    /// </summary>
    private static string DestinationPath(string source, string format, out bool renamed)
    {
        var directory = Path.GetDirectoryName(source)
            ?? throw new IOException("La ruta no tiene carpeta contenedora.");

        var baseName = Path.GetFileNameWithoutExtension(source);
        var plain = Path.Combine(directory, baseName + "." + format);

        if (IsFree(plain))
        {
            renamed = false;
            return plain;
        }

        renamed = true;

        var candidate = Path.Combine(directory, baseName + format + "." + format);

        if (IsFree(candidate))
        {
            return candidate;
        }

        for (var attempt = 2; attempt <= 999; attempt++)
        {
            candidate = Path.Combine(directory, $"{baseName}{format} ({attempt}).{format}");

            if (IsFree(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("no hay ningun nombre libre en esa carpeta");
    }

    private static bool IsFree(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static bool SameFormat(string path, string format) => Normalize(Path.GetExtension(path)) == format;

    private static string Normalize(string value)
    {
        var text = (value ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return text == "jpeg" ? "jpg" : text;
    }

    private static string Describe(Exception ex) => ex switch
    {
        FileFormatException => "no es una imagen",
        NotSupportedException => "no es una imagen",
        UnauthorizedAccessException => "sin permisos",
        IOException => ex.Message,
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    /// <summary>Un archivo que se deja pasar sin contarlo como error de programa, con su motivo.</summary>
    private sealed class SkipException : Exception
    {
        internal SkipException(string message) : base(message)
        {
        }
    }
}

public sealed class ConvertResult
{
    public List<string> Converted { get; } = new();

    public List<string> Failures { get; } = new();

    /// <summary>Nombres finales que no coinciden con el obvio porque el destino estaba ocupado.</summary>
    public List<string> Renamed { get; } = new();

    public string Error { get; set; } = string.Empty;

    public string Summary
    {
        get
        {
            if (Error.Length > 0)
            {
                return Error;
            }

            if (Converted.Count == 0 && Failures.Count == 0)
            {
                return "Nada que convertir";
            }

            if (Failures.Count == 0)
            {
                if (Converted.Count == 1)
                {
                    // Si hubo que cambiar el nombre, se dice: el archivo no se llama como se esperaba.
                    return Renamed.Count == 1 ? $"Convertido como {Renamed[0]}" : "Convertido";
                }

                return $"Convertidos {Converted.Count}";
            }

            if (Converted.Count == 0)
            {
                return "No se pudo convertir";
            }

            return $"{Converted.Count} convertidos, {Failures.Count} fallo(s)";
        }
    }
}
