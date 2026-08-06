using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfEdit.Converters;

/// <summary>
/// Shows an element only when the bound value carries content: non-<see langword="null"/>, and for
/// <see cref="string"/> values also non-whitespace. Handy for optional captions such as the source
/// file name shown under a page thumbnail.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether the result is negated, i.e. the element is visible
    /// exactly when the value is null/empty (placeholder text).
    /// </summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasContent = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };

        if (Invert)
            hasContent = !hasContent;

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: the original value cannot be recovered from a <see cref="Visibility"/>.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(NullOrEmptyToVisibilityConverter)} is a one-way converter.");
}
