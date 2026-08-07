using System.Globalization;
using System.Windows.Data;

namespace Yordamchi.Converters;

/// <summary>
/// Compares a bound enum value with a converter parameter and returns <see langword="true"/> when they
/// match. The parameter may be the enum member's name as a string (the usual XAML case,
/// <c>ConverterParameter=Rotate90</c>) or a real enum instance.
/// <para>
/// <see cref="ConvertBack"/> returns the parameter when the target reports <see langword="true"/>, which
/// is exactly what <c>RadioButton.IsChecked</c> needs to drive a two-way enum selection.
/// </para>
/// </summary>
[ValueConversion(typeof(Enum), typeof(bool), ParameterType = typeof(object))]
public sealed class EnumEqualsConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        if (value.Equals(parameter))
            return true;

        // XAML hands the parameter over as a string; coerce it to the bound value's enum type.
        var valueType = value.GetType();
        if (valueType.IsEnum && parameter is string name)
        {
            return Enum.TryParse(valueType, name, ignoreCase: true, out var parsed)
                && value.Equals(parsed);
        }

        return false;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only the newly checked option writes back; the one being unchecked must leave the source alone.
        if (value is not true || parameter is null)
            return Binding.DoNothing;

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (enumType.IsEnum && parameter is string name)
        {
            return Enum.TryParse(enumType, name, ignoreCase: true, out var parsed)
                ? parsed
                : Binding.DoNothing;
        }

        return parameter;
    }
}
