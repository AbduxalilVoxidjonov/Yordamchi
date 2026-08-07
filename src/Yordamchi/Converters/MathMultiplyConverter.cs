using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Yordamchi.Converters;

/// <summary>
/// Multiplies a bound <see cref="double"/> by the converter parameter, e.g. to derive a thumbnail's
/// height from the bound card width: <c>Height="{Binding ThumbnailWidth, Converter={StaticResource Multiply}, ConverterParameter=1.414}"</c>.
/// The parameter is parsed with <see cref="CultureInfo.InvariantCulture"/> because it comes from XAML,
/// which is invariant regardless of the user's regional settings.
/// </summary>
[ValueConversion(typeof(double), typeof(double), ParameterType = typeof(string))]
public sealed class MathMultiplyConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryGetDouble(value, out var input) || !TryGetDouble(parameter, out var factor))
            return DependencyProperty.UnsetValue;

        return input * factor;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Multiplication is reversible as long as the factor is not zero.
        if (!TryGetDouble(value, out var input) || !TryGetDouble(parameter, out var factor) || factor == 0d)
            return DependencyProperty.UnsetValue;

        return input / factor;
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return !double.IsNaN(d) && !double.IsInfinity(d);
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0d;
                return false;
        }
    }
}
