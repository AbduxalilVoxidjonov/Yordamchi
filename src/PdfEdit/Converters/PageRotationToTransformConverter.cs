using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PdfEdit.Models;

namespace PdfEdit.Converters;

/// <summary>
/// Converts a <see cref="PageRotation"/> (or a raw <see cref="int"/> angle) to a frozen
/// <see cref="RotateTransform"/> suitable for a thumbnail's <c>LayoutTransform</c>/<c>RenderTransform</c>.
/// The four possible transforms are created once and frozen, so binding thousands of thumbnails
/// costs no allocations and the instances are safe to share across threads.
/// </summary>
[ValueConversion(typeof(PageRotation), typeof(Transform))]
public sealed class PageRotationToTransformConverter : IValueConverter
{
    private static readonly Transform Rotate90 = CreateFrozen(90d);
    private static readonly Transform Rotate180 = CreateFrozen(180d);
    private static readonly Transform Rotate270 = CreateFrozen(270d);

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var angle = PageRotationConversion.ToAngle(value);
        if (angle is null)
            return DependencyProperty.UnsetValue;

        return angle.Value switch
        {
            // None (and any other multiple of 360) needs no transform at all; Transform.Identity is
            // itself frozen and is special-cased by the layout system.
            0d => Transform.Identity,
            90d => Rotate90,
            180d => Rotate180,
            270d => Rotate270,
            var other => CreateFrozen(other)
        };
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: transforms are not converted back to page state.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(PageRotationToTransformConverter)} is a one-way converter.");

    private static Transform CreateFrozen(double angle)
    {
        var transform = new RotateTransform(angle);
        transform.Freeze();
        return transform;
    }
}

/// <summary>
/// Converts a <see cref="PageRotation"/> (or a raw <see cref="int"/> angle) to a <see cref="double"/>
/// angle in degrees, for animating a rotation or feeding an existing <see cref="RotateTransform"/>.
/// </summary>
[ValueConversion(typeof(PageRotation), typeof(double))]
public sealed class PageRotationToAngleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => PageRotationConversion.ToAngle(value) ?? (object)DependencyProperty.UnsetValue;

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: an arbitrary angle is not a valid <see cref="PageRotation"/>.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(PageRotationToAngleConverter)} is a one-way converter.");
}

/// <summary>Shared parsing for the two rotation converters in this file.</summary>
internal static class PageRotationConversion
{
    /// <summary>Normalizes a bound value to an angle in <c>[0, 360)</c>, or <see langword="null"/> when it is not a rotation.</summary>
    internal static double? ToAngle(object? value)
    {
        double raw;
        switch (value)
        {
            case PageRotation rotation:
                raw = (double)(int)rotation;
                break;
            case int degrees:
                raw = degrees;
                break;
            case double degrees:
                raw = degrees;
                break;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                raw = parsed;
                break;
            default:
                return null;
        }

        raw %= 360d;
        return raw < 0d ? raw + 360d : raw;
    }
}
