using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfEdit.Converters;

/// <summary>
/// Negates a <see cref="bool"/>. Used for "enabled when not busy" style bindings.
/// The conversion is symmetric, so <see cref="ConvertBack"/> is supported and the converter
/// is safe on two-way bindings such as <c>IsChecked</c>.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Negate(value);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Negate(value);

    private static object Negate(object? value) => value switch
    {
        bool b => !b,
        null => true,
        // Not a boolean at all: refuse rather than guess, so the binding keeps its fallback/default.
        _ => DependencyProperty.UnsetValue
    };
}
