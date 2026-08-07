using System.Globalization;
using System.Windows.Data;

namespace Yordamchi.Converters;

/// <summary>
/// Maps a <see cref="bool"/> to an opacity, so a disabled or excluded page card can be dimmed without a
/// dedicated style trigger. <see langword="null"/> is treated as <see langword="false"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(double))]
public sealed class BooleanToOpacityConverter : IValueConverter
{
    /// <summary>Gets or sets the opacity used when the value is <see langword="true"/>. Defaults to <c>1.0</c>.</summary>
    public double TrueOpacity { get; set; } = 1.0;

    /// <summary>Gets or sets the opacity used when the value is <see langword="false"/>. Defaults to <c>0.4</c>.</summary>
    public double FalseOpacity { get; set; } = 0.4;

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueOpacity : FalseOpacity;

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: an arbitrary opacity does not map back to a boolean.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BooleanToOpacityConverter)} is a one-way converter.");
}
