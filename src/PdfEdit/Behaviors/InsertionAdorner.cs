using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfEdit.Behaviors;

/// <summary>
/// The drop indicator drawn while <see cref="DragDropReorder"/> is dragging a page card: a rounded accent
/// bar down the leading or trailing edge of the container the item would be inserted next to, capped at
/// both ends, mirroring the Windows 11 taskbar / Edge tab-strip reorder affordance.
/// </summary>
/// <remarks>
/// Adorners live in an <see cref="AdornerLayer"/> above the adorned element, so this never disturbs the
/// <c>WrapPanel</c> layout and never causes a re-measure while the pointer moves.
/// </remarks>
public sealed class InsertionAdorner : Adorner
{
    /// <summary>Resource key looked up on the application for the indicator colour.</summary>
    public const string BrushResourceKey = "InsertionIndicatorBrush";

    private const double Thickness = 3d;
    private const double VerticalInset = 4d;
    private const double CapRadius = 3d;

    /// <summary>Windows 11 "Accent Dark 1" — used when the theme has not supplied <see cref="BrushResourceKey"/>.</summary>
    private static readonly Brush FallbackBrush = CreateFallbackBrush();

    private static InsertionAdorner? _current;
    private static AdornerLayer? _currentLayer;

    private readonly bool _isVerticalInsert;
    private readonly bool _insertAfter;

    /// <summary>Initializes a new indicator over <paramref name="adornedElement"/>.</summary>
    /// <param name="adornedElement">The item container the indicator is drawn against.</param>
    /// <param name="isVerticalInsert">
    /// <see langword="true"/> to draw a vertical line on the left/right edge (items flow horizontally, the
    /// <c>WrapPanel</c> case); <see langword="false"/> to draw a horizontal line on the top/bottom edge.
    /// </param>
    /// <param name="insertAfter"><see langword="true"/> to draw on the trailing edge, otherwise the leading edge.</param>
    public InsertionAdorner(UIElement adornedElement, bool isVerticalInsert, bool insertAfter)
        : base(adornedElement)
    {
        _isVerticalInsert = isVerticalInsert;
        _insertAfter = insertAfter;

        // The adorner must never swallow the drag events targeted at the containers underneath it.
        IsHitTestVisible = false;
    }

    /// <summary>Gets a value indicating whether this indicator is drawn on the trailing edge.</summary>
    public bool InsertAfter => _insertAfter;

    /// <summary>Gets a value indicating whether this indicator is drawn as a vertical line.</summary>
    public bool IsVerticalInsert => _isVerticalInsert;

    /// <summary>
    /// Shows the indicator next to <paramref name="target"/>, reusing the existing adorner when nothing
    /// changed so a moving pointer does not cause a flicker on every <c>DragOver</c>.
    /// </summary>
    /// <param name="target">The item container to adorn.</param>
    /// <param name="insertAfter"><see langword="true"/> for the trailing edge, otherwise the leading edge.</param>
    /// <param name="isVerticalInsert"><see langword="true"/> (the default) for a vertical line, as used by a horizontal wrap flow.</param>
    public static void Attach(UIElement target, bool insertAfter, bool isVerticalInsert = true)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_current is not null
            && ReferenceEquals(_current.AdornedElement, target)
            && _current._insertAfter == insertAfter
            && _current._isVerticalInsert == isVerticalInsert)
        {
            return;
        }

        Detach();

        // No layer exists when the target is not yet in a rendered tree (e.g. a virtualized container that
        // has just been recycled). Silently skip: the indicator is decoration, never a precondition.
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null)
            return;

        var adorner = new InsertionAdorner(target, isVerticalInsert, insertAfter);
        layer.Add(adorner);

        _current = adorner;
        _currentLayer = layer;
    }

    /// <summary>Removes the indicator if one is showing. Safe to call repeatedly and from any exit path.</summary>
    public static void Detach()
    {
        if (_current is not null)
            _currentLayer?.Remove(_current);

        _current = null;
        _currentLayer = null;
    }

    /// <summary>Gets a value indicating whether an indicator is currently attached.</summary>
    public static bool IsAttached => _current is not null;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var size = AdornedElement.RenderSize;
        if (size.Width <= 0d || size.Height <= 0d)
            return;

        var brush = ResolveBrush();
        var half = Thickness / 2d;

        Rect bar;
        Point capA;
        Point capB;

        if (_isVerticalInsert)
        {
            // Straddle the edge so the bar reads as sitting *between* two cards rather than on top of one.
            var x = _insertAfter ? size.Width : 0d;
            var top = Math.Min(VerticalInset, size.Height / 2d);
            var bottom = Math.Max(size.Height - top, top);

            bar = new Rect(x - half, top, Thickness, bottom - top);
            capA = new Point(x, top);
            capB = new Point(x, bottom);
        }
        else
        {
            var y = _insertAfter ? size.Height : 0d;
            var left = Math.Min(VerticalInset, size.Width / 2d);
            var right = Math.Max(size.Width - left, left);

            bar = new Rect(left, y - half, right - left, Thickness);
            capA = new Point(left, y);
            capB = new Point(right, y);
        }

        drawingContext.DrawRoundedRectangle(brush, pen: null, bar, half, half);
        drawingContext.DrawEllipse(brush, pen: null, capA, CapRadius, CapRadius);
        drawingContext.DrawEllipse(brush, pen: null, capB, CapRadius, CapRadius);
    }

    private static Brush ResolveBrush()
    {
        try
        {
            if (Application.Current?.TryFindResource(BrushResourceKey) is Brush themed)
                return themed;
        }
        catch (InvalidOperationException)
        {
            // A resource dictionary can throw while it is still being loaded; the fallback keeps rendering alive.
        }

        return FallbackBrush;
    }

    private static Brush CreateFallbackBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
        brush.Freeze();
        return brush;
    }
}
