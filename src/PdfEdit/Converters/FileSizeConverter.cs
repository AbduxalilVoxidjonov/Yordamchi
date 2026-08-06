using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfEdit.Converters;

/// <summary>
/// Formats a byte count as a short human readable size, e.g. <c>"1.4 MB"</c>. Always formatted with the
/// invariant culture so the document-info panel reads identically on every machine, and always with a
/// binary (1024) base, which is what Explorer's size column shows.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
public sealed class FileSizeConverter : IValueConverter
{
    private const double Scale = 1024d;
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryGetBytes(value, out var bytes))
            return DependencyProperty.UnsetValue;

        if (bytes < 0)
            return DependencyProperty.UnsetValue;

        // Whole bytes never need a fractional part ("512 B", not "512.0 B").
        if (bytes < Scale)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} {Units[0]}");

        var size = bytes / Scale;
        var unit = 1;
        while (size >= Scale && unit < Units.Length - 1)
        {
            size /= Scale;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.0} {Units[unit]}");
    }

    private static bool TryGetBytes(object? value, out long bytes)
    {
        switch (value)
        {
            case long l:
                bytes = l;
                return true;
            case int i:
                bytes = i;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d) && d <= long.MaxValue:
                bytes = (long)d;
                return true;
            case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                bytes = parsed;
                return true;
            default:
                bytes = 0;
                return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: the formatted string is rounded and cannot be parsed back exactly.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(FileSizeConverter)} is a one-way converter.");
}
