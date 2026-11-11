using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Filewheel.App;

/// <summary>
/// El contenedor de una cuna: el equivalente a un div dentro de su sector. Su forma es la
/// del propio sector, recortada y con las esquinas redondeadas, asi que el contenido vive
/// dentro de la geometria de la cuna y no se dibuja nada suelto sobre el arco.
/// Todo se configura por separado:
///   tamano   -> Width / Height (sin ellos ocupa la forma del sector; con ellos se escala)
///   posicion -> OffsetX / OffsetY, en DIPs desde el centro de la cuna
///   margen   -> Margin (reduce el area del contenido)
///   fondo    -> Background (rellena la forma, no un rectangulo)
///   borde    -> BorderBrush / BorderThickness (dibuja el contorno de la forma)
///   esquinas -> CornerRadius.TopLeft (radio del redondeo de la forma)
///   contenido-> Child (o Text, que crea un TextBlock por defecto)
/// </summary>
public sealed class WheelSector : Border
{
    private TextBlock? _label;
    private double _offsetX;
    private double _offsetY;

    // CornerRadius es una propiedad heredada de Border: no se puede interceptar su setter,
    // asi que se avisa del cambio por los metadatos de la propiedad.
    static WheelSector()
    {
        CornerRadiusProperty.OverrideMetadata(
            typeof(WheelSector),
            new FrameworkPropertyMetadata(
                new CornerRadius(0),
                FrameworkPropertyMetadataOptions.AffectsArrange,
                (d, _) => ((WheelSector)d).InvalidateShape()));
    }

    /// <summary>Forma recortada de la cuna, en coordenadas locales del contenedor.</summary>
    internal Geometry? SectorShape { get; set; }

    /// <summary>Desplazamiento del contenedor respecto al centro de la cuna, en DIPs.</summary>
    public double OffsetX
    {
        get => _offsetX;
        set
        {
            if (_offsetX == value)
            {
                return;
            }

            _offsetX = value;
            InvalidateShape();
        }
    }

    /// <summary>Desplazamiento del contenedor respecto al centro de la cuna, en DIPs.</summary>
    public double OffsetY
    {
        get => _offsetY;
        set
        {
            if (_offsetY == value)
            {
                return;
            }

            _offsetY = value;
            InvalidateShape();
        }
    }

    /// <summary>
    /// La forma de la cuna se calcula en WheelView.ArrangeOverride, no aqui, y se guarda en
    /// SectorShape. Cambiar tamano, posicion o redondeo no dispara ese calculo por si solo:
    /// hay que pedirle un pase de layout a la rueda o la geometria guardada se queda igual.
    /// </summary>
    private void InvalidateShape()
    {
        InvalidateArrange();
        (Parent as WheelView)?.InvalidateArrange();
    }

    /// <summary>
    /// Texto por defecto del contenedor. Si quieres otra cosa dentro (un StackPanel, una
    /// imagen, un boton...), asigna Child y deja de usar Text.
    /// </summary>
    public string Text
    {
        get => _label?.Text ?? string.Empty;
        set
        {
            _label ??= new TextBlock
            {
                FontFamily = WheelView.LabelTypeface.FontFamily,
                FontSize = 13.0,
                Foreground = WheelView.LabelBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _label.Text = value ?? string.Empty;

            if (!ReferenceEquals(Child, _label))
            {
                Child = _label;
            }
        }
    }

    /// <summary>
    /// Resaltado de la cuna: cambia el texto por defecto y el relleno de la forma, pero solo
    /// si sigues usando los valores por defecto (si pusiste un fondo propio, se respeta).
    /// </summary>
    internal void SetHighlighted(bool highlighted, Brush normalFill, Brush hoverFill)
    {
        if (ReferenceEquals(Background, normalFill) || ReferenceEquals(Background, hoverFill))
        {
            Background = highlighted ? hoverFill : normalFill;
        }

        if (_label is not null)
        {
            _label.Foreground = highlighted ? WheelView.HoverLabelBrush : WheelView.LabelBrush;
        }
    }

    /// <summary>
    /// No se usa el relleno rectangular de Border: el fondo y el borde se pintan con la
    /// forma de la cuna. El contenido queda recortado por esa misma forma (Clip).
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (SectorShape is null)
        {
            base.OnRender(drawingContext);
            return;
        }

        var fill = Background;

        if (fill is not null)
        {
            drawingContext.DrawGeometry(fill, null, SectorShape);
        }

        var edge = BorderBrush;
        var thickness = BorderThickness.Left;

        if (edge is not null && thickness > 0)
        {
            var pen = new Pen(edge, thickness) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, SectorShape);
        }
    }
}
