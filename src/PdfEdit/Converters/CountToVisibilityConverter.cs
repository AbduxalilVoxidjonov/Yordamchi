using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfEdit.Converters;

/// <summary>
/// Converts a count — an <see cref="int"/>, an <see cref="ICollection"/> or any <see cref="IEnumerable"/> —
/// to a <see cref="Visibility"/>: visible when the count is greater than zero. Set <see cref="Invert"/>
/// to drive the "no pages loaded yet" empty-state panel from the same collection.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <summary>Gets or sets a value indicating whether the element is visible when the count is zero instead.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = HasItems(value);

        if (Invert)
            hasItems = !hasItems;

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool HasItems(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case int count:
                return count > 0;
            case long longCount:
                return longCount > 0;
            case ICollection collection:
                return collection.Count > 0;
            case IEnumerable enumerable:
            {
                // A bare IEnumerable may be a lazy, single-pass sequence (or an expensive query), so
                // pull exactly one element and dispose the enumerator: never enumerate it twice.
                var enumerator = enumerable.GetEnumerator();
                try
                {
                    return enumerator.MoveNext();
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            }
            default:
                return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a <see cref="Visibility"/> carries no count.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(CountToVisibilityConverter)} is a one-way converter.");
}
