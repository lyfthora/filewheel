using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Filewheel.App;

public sealed class WheelView : FrameworkElement
{
    private const double MarginPixels = 52;
    private const double InnerRatio = 0.44;
    private const double GapDegrees = 1.5;

    private static readonly Brush BaseFill = CreateBrush(Color.FromArgb(0xE8, 0x18, 0x1A, 0x21));
    private static readonly Brush HoverFill = CreateBrush(Color.FromArgb(0xF5, 0x2F, 0x6F, 0xE8));
    private static readonly Brush LabelBrush = CreateBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly Brush HoverLabelBrush = CreateBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Pen EdgePen = CreateEdgePen();
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<string> _labels = Array.Empty<string>();
    private int _hoverIndex = -1;

    public IReadOnlyList<string> Labels
    {
        get => _labels;
        set
        {
            _labels = value ?? Array.Empty<string>();
            InvalidateVisual();
        }
    }

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
        var sweep = 360.0 / count;
        var index = (int)((angle + (sweep / 2)) % 360.0 / sweep);
        return index >= count ? count - 1 : index;
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
            var start = (index * sweep) - (sweep / 2) + (GapDegrees / 2);
            var end = (index * sweep) + (sweep / 2) - (GapDegrees / 2);

            var geometry = CreateSector(center, radius, innerRadius, start, end);
            var isHovered = index == _hoverIndex;
            drawingContext.DrawGeometry(isHovered ? HoverFill : BaseFill, EdgePen, geometry);

            var text = new FormattedText(
            _labels[index],
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            13.0,
            isHovered ? HoverLabelBrush : LabelBrush,
            pixelsPerDip);

            var textCenter = PointOn(center, (radius + innerRadius) / 2, start + ((end - start) / 2));
            drawingContext.DrawText(text, new Point(textCenter.X - (text.Width / 2), textCenter.Y - (text.Height / 2)));
        }
    }

    private double Radius => Math.Max(0, (Math.Min(ActualWidth, ActualHeight) / 2) - MarginPixels);

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
