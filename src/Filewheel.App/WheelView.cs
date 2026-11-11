using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Filewheel.App;

/// <summary>
/// Anillo de sectores con un contenedor hijo por cuna. Los arcos se pintan en
/// <see cref="OnRender"/> y encima se colocan los hijos: cada uno dentro de los limites
/// geometricos de su sector, como un div dentro de su columna. El contenido de la cuna no
/// se dibuja sobre el sector, vive en su WheelSector.
/// </summary>
public sealed class WheelView : Panel
{
    private const double MarginPixels = 52;
    private const double InnerRatio = 0.44;
    private const double GapDegrees = 1.5;

    // El bloque de cada cuna es la propia forma del sector, recortada: se separa de sus
    // bordes y del anillo para que se vea el hueco, y redondea las esquinas.
    private const double SectorInsetAngle = 3;
    private const double SectorInsetPixels = 7;
    private const double SectorCorner = 12;
    private const double CardInset = (GapDegrees / 2) + SectorInsetAngle;

    private static readonly Brush BaseFill = CreateBrush(Color.FromArgb(0xE8, 0x18, 0x1A, 0x21));
    private static readonly Brush HoverFill = CreateBrush(Color.FromArgb(0xF5, 0x2F, 0x6F, 0xE8));

    // El bloque interno de cada cuna: el 'div' visible dentro del sector.
    private static readonly Brush SectorFill = CreateBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HoverSectorFill = CreateBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SectorEdge = CreateBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    internal static readonly Brush LabelBrush = CreateBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    internal static readonly Brush HoverLabelBrush = CreateBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Pen EdgePen = CreateEdgePen();
    internal static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<string> _labels = Array.Empty<string>();
    private int _hoverIndex = -1;
    private string _centerText = string.Empty;
    private double _startAngle;
    private Action<WheelSector, int>? _sectorSetup;

    // Copia de los textos que estan pintados ahora mismo. Sin ella no hay forma de saber si
    // un array mutado en sitio sigue diciendo lo mismo que la rueda.
    private string[] _renderedLabels = Array.Empty<string>();

    /// <summary>
    /// Textos de las cunas. Al cambiar la lista se recrean los contenedores con sus valores
    /// por defecto; si vuelves a pasar la misma instancia sin cambiar sus textos, se conserva
    /// lo que hayas configurado. Si mutas el array en sitio, la rueda se entera igual.
    /// </summary>
    public IReadOnlyList<string> Labels
    {
        get => _labels;
        set
        {
            var next = value ?? Array.Empty<string>();

            // Misma instancia y mismo contenido: "no cambies nada". Cualquier otro caso,
            // incluido haber mutado el array en sitio, reconstruye las cunas.
            if (ReferenceEquals(next, _labels) && SameContent(next, _renderedLabels))
            {
                return;
            }

            _labels = next;
            _renderedLabels = Snapshot(next);
            RebuildSectors();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Se llama al crear cada contenedor, con su indice. Aqui se le da el estilo propio de
    /// esa cuna (tamano, margen, fondo, borde...). Se puede cambiar en cualquier momento.
    /// </summary>
    public Action<WheelSector, int>? SectorSetup
    {
        get => _sectorSetup;
        set
        {
            _sectorSetup = value;
            ApplySectorSetup();
            InvalidateMeasure();
        }
    }

    private static bool SameContent(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var index = 0; index < a.Count; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Snapshot(IReadOnlyList<string> source)
    {
        var copy = new string[source.Count];

        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }

    /// <summary>El contenedor de una cuna, o null si el indice se sale.</summary>
    public WheelSector? SectorAt(int index)
        => index >= 0 && index < Children.Count ? Children[index] as WheelSector : null;

    public int HoverIndex
    {
        get => _hoverIndex;
        set
        {
            if (_hoverIndex == value)
            {
                return;
            }

            _hoverIndex = value;
            ApplyHighlights();
            InvalidateVisual();
        }
    }

    public string CenterText
    {
        get => _centerText;
        set
        {
            _centerText = value ?? string.Empty;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Grados que se gira todo el anillo (horario, desde arriba). Con dos sectores sirve
    /// para dejarlos a izquierda y derecha en vez de arriba y abajo.
    /// </summary>
    public double StartAngle
    {
        get => _startAngle;
        set
        {
            var normalized = value % 360.0;

            if (normalized < 0)
            {
                normalized += 360.0;
            }

            if (Math.Abs(normalized - _startAngle) < 0.01)
            {
                return;
            }

            _startAngle = normalized;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public int HitTestSector(Point position)
    {
        var count = _labels.Count;

        if (count == 0)
        {
            return -1;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = position.X - center.X;
        var dy = position.Y - center.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var radius = Radius;

        if (distance < radius * InnerRatio || distance > radius)
        {
            return -1;
        }

        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;

        if (angle < 0)
        {
            angle += 360.0;
        }

        // El mismo giro que usa OnRender, o el hover no coincidiria con lo pintado.
        angle = (angle - _startAngle + 360.0) % 360.0;

        var sweep = 360.0 / count;
        var index = (int)((angle + (sweep / 2)) % 360.0 / sweep);
        return index >= count ? count - 1 : index;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Finite(availableSize);

        // UIElementCollection no es generica: sin el tipo explicito, 'child' seria object.
        // FrameworkElement y no UIElement, porque Width/Height viven en FrameworkElement.
        for (var index = 0; index < Children.Count; index++)
        {
            var child = (FrameworkElement)Children[index];
            var natural = NaturalRect(size, Children.Count, _startAngle, index);
            var width = double.IsNaN(child.Width) ? natural.Width : child.Width;
            var height = double.IsNaN(child.Height) ? natural.Height : child.Height;

            child.Measure(new Size(Math.Max(0, width), Math.Max(0, height)));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var radius = RadiusFor(finalSize);
        var innerRadius = radius * InnerRatio;
        var outer = Math.Max(0, radius - SectorInsetPixels);
        var inner = innerRadius + SectorInsetPixels;
        var count = Children.Count;
        var sweep = Sweep(count);
        var cx = finalSize.Width / 2;
        var cy = finalSize.Height / 2;
        var mid = (radius + innerRadius) / 2;
        var wheelCenter = new Point(cx, cy);

        for (var index = 0; index < count; index++)
        {
            var child = (FrameworkElement)Children[index];
            var natural = NaturalRect(finalSize, count, _startAngle, index);

            // Sin Width/Height propios el bloque ocupa la forma del sector; con ellos, esa
            // forma se escala al tamano pedido.
            var width = Math.Max(0, double.IsNaN(child.Width) ? natural.Width : child.Width);
            var height = Math.Max(0, double.IsNaN(child.Height) ? natural.Height : child.Height);
            var angle = _startAngle + (index * sweep);
            var radians = angle * Math.PI / 180.0;
            var x = cx + (mid * Math.Sin(radians)) - (width / 2);
            var y = cy - (mid * Math.Cos(radians)) - (height / 2);

            if (child is WheelSector sector)
            {
                x += sector.OffsetX;
                y += sector.OffsetY;
            }

            child.Arrange(new Rect(x, y, width, height));

            if (child is WheelSector card)
            {
                var rounding = Math.Max(0, card.CornerRadius.TopLeft) > 0
                    ? card.CornerRadius.TopLeft
                    : SectorCorner;
                var (cardStart, cardEnd) = CardAngles(index, count, _startAngle);
                var geometry = CreateSectorCard(wheelCenter, outer, inner, cardStart, cardEnd, rounding);

                // La forma se calcula en coordenadas de la rueda; se lleva al espacio local
                // del contenedor, escalando si le diste un Width/Height propio.
                var scaleX = natural.Width > 0 ? width / natural.Width : 1;
                var scaleY = natural.Height > 0 ? height / natural.Height : 1;
                var centerX = x + (width / 2);
                var centerY = y + (height / 2);

                geometry.Transform = new MatrixTransform(new Matrix(
                    scaleX, 0, 0, scaleY,
                    (width / 2) - (scaleX * centerX),
                    (height / 2) - (scaleY * centerY)));
                geometry.Freeze();

                card.SectorShape = geometry;
                card.Clip = geometry;
            }
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var count = _labels.Count;

        if (count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Radius;
        var innerRadius = radius * InnerRatio;
        var sweep = 360.0 / count;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var index = 0; index < count; index++)
        {
            var start = (index * sweep) - (sweep / 2) + (GapDegrees / 2) + _startAngle;
            var end = (index * sweep) + (sweep / 2) - (GapDegrees / 2) + _startAngle;

            var geometry = CreateSector(center, radius, innerRadius, start, end);
            var isHovered = index == _hoverIndex;
            drawingContext.DrawGeometry(isHovered ? HoverFill : BaseFill, EdgePen, geometry);
        }

        if (_centerText.Length > 0)
        {
            var status = new FormattedText(
            _centerText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            12.0,
            LabelBrush,
            pixelsPerDip)
            {
                MaxTextWidth = innerRadius * 1.5,
                TextAlignment = TextAlignment.Center,
            };
            drawingContext.DrawText(
            status,
            new Point(center.X - (innerRadius * 0.75), center.Y - (status.Height / 2)));
        }
    }

    private void RebuildSectors()
    {
        Children.Clear();

        foreach (var text in _labels)
        {
            Children.Add(new WheelSector
            {
                Text = text,
                Background = SectorFill,
                BorderBrush = SectorEdge,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(SectorCorner),
                Padding = new Thickness(6),
            });
        }

        ApplySectorSetup();
        ApplyHighlights();
    }

    private void ApplySectorSetup()
    {
        if (_sectorSetup is null)
        {
            return;
        }

        for (var index = 0; index < Children.Count; index++)
        {
            if (Children[index] is WheelSector sector)
            {
                _sectorSetup(sector, index);
            }
        }
    }

    private void ApplyHighlights()
    {
        for (var index = 0; index < Children.Count; index++)
        {
            if (Children[index] is WheelSector sector)
            {
                sector.SetHighlighted(index == _hoverIndex, SectorFill, HoverSectorFill);
            }
        }
    }

    /// <summary>
    /// Caja natural del bloque de una cuna: centrada en la cuna y lo bastante grande para
    /// contener toda su forma. El centro del rectangulo que encierra el trozo de anillo no
    /// cae en el centro del sector, asi que se toma el mayor saliente hacia cada lado.
    /// </summary>
    private static Rect NaturalRect(Size size, int count, double startAngle, int index)
    {
        var radius = RadiusFor(size);
        var innerRadius = radius * InnerRatio;
        var mid = (radius + innerRadius) / 2;
        var center = new Point(size.Width / 2, size.Height / 2);
        var (start, end) = CardAngles(index, count, startAngle);
        var bounds = SectorBounds(
            center,
            Math.Max(1, radius - SectorInsetPixels),
            Math.Max(1, innerRadius + SectorInsetPixels),
            start,
            end);

        var angle = (startAngle + (index * Sweep(count))) * Math.PI / 180.0;
        var origin = new Point(
            center.X + (mid * Math.Sin(angle)),
            center.Y - (mid * Math.Cos(angle)));
        var halfWidth = Math.Max(origin.X - bounds.Left, bounds.Right - origin.X);
        var halfHeight = Math.Max(origin.Y - bounds.Top, bounds.Bottom - origin.Y);

        return new Rect(
            origin.X - halfWidth,
            origin.Y - halfHeight,
            Math.Max(1, halfWidth * 2),
            Math.Max(1, halfHeight * 2));
    }

    /// <summary>
    /// Angulos que abarca el bloque de una cuna: los mismos que su arco (centrados en el
    /// sector), recortados por <see cref="CardInset"/> a cada lado.
    /// </summary>
    private static (double Start, double End) CardAngles(int index, int count, double startAngle)
    {
        var sweep = Sweep(count);
        var center = startAngle + (index * sweep);

        return (center - (sweep / 2) + CardInset, center + (sweep / 2) - CardInset);
    }

    private static Rect SectorBounds(Point center, double outer, double inner, double start, double end)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var span = Math.Max(0, end - start);
        var steps = Math.Max(2, (int)Math.Ceiling(span));

        foreach (var radius in new[] { inner, outer })
        {
            for (var step = 0; step <= steps; step++)
            {
                var point = PointOn(center, radius, start + (span * step / steps));
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>
    /// La forma del bloque: un trozo de anillo (el propio sector) con las cuatro esquinas
    /// redondeadas. Es el 'div' de la cuna, recortado a la geometria del sector.
    /// </summary>
    private static StreamGeometry CreateSectorCard(
        Point center,
        double outer,
        double inner,
        double start,
        double end,
        double corner)
    {
        var span = Math.Max(1, end - start);
        var rounding = Math.Max(0, Math.Min(corner, Math.Min((outer - inner) / 2, outer * 0.12)));
        var dOuter = Math.Min(rounding / Math.Max(outer, 1) * 180.0 / Math.PI, span / 4);
        var dInner = Math.Min(rounding / Math.Max(inner, 1) * 180.0 / Math.PI, span / 4);
        var largeOuter = span - (2 * dOuter) > 180;
        var largeInner = span - (2 * dInner) > 180;
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(center, outer, start + dOuter), true, true);
            context.ArcTo(PointOn(center, outer, end - dOuter), new Size(outer, outer), 0, largeOuter, SweepDirection.Clockwise, true, false);
            context.QuadraticBezierTo(PointOn(center, outer, end), PointOn(center, outer - rounding, end), true, false);
            context.LineTo(PointOn(center, inner + rounding, end), true, false);
            context.QuadraticBezierTo(PointOn(center, inner, end), PointOn(center, inner, end - dInner), true, false);
            context.ArcTo(PointOn(center, inner, start + dInner), new Size(inner, inner), 0, largeInner, SweepDirection.Counterclockwise, true, false);
            context.QuadraticBezierTo(PointOn(center, inner, start), PointOn(center, inner + rounding, start), true, false);
            context.LineTo(PointOn(center, outer - rounding, start), true, false);
            context.QuadraticBezierTo(PointOn(center, outer, start), PointOn(center, outer, start + dOuter), true, false);
        }

        return geometry;
    }

    private double Radius => RadiusFor(new Size(ActualWidth, ActualHeight));

    private static double RadiusFor(Size size)
        => Math.Max(0, (Math.Min(size.Width, size.Height) / 2) - MarginPixels);

    private static double Sweep(int count) => count <= 0 ? 0 : 360.0 / count;

    private static Size Finite(Size size)
        => new(
            double.IsInfinity(size.Width) ? 440 : size.Width,
            double.IsInfinity(size.Height) ? 440 : size.Height);

    private static StreamGeometry CreateSector(Point center, double radius, double innerRadius, double start, double end)
    {
        var largeArc = end - start > 180.0;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(center, radius, start), true, true);
            context.ArcTo(PointOn(center, radius, end), new Size(radius, radius), 0, largeArc, SweepDirection.Clockwise, true, false);
            context.LineTo(PointOn(center, innerRadius, end), true, false);
            context.ArcTo(PointOn(center, innerRadius, start), new Size(innerRadius, innerRadius), 0, largeArc, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOn(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(center.X + (radius * Math.Sin(radians)), center.Y - (radius * Math.Cos(radians)));
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateEdgePen()
    {
        var pen = new Pen(CreateBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 1.0);
        pen.Freeze();
        return pen;
    }
}
