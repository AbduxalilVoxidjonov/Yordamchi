using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Yordamchi.Converters;

/// <summary>
/// Converts a <see cref="bool"/> (or <see langword="null"/>, treated as <see langword="false"/>) to a
/// <see cref="Visibility"/>. Unlike the framework converter of the same name this one can be inverted
/// and can fall back to <see cref="Visibility.Hidden"/> instead of <see cref="Visibility.Collapsed"/>,
/// which matters for layout that must reserve space (e.g. the per-page hover toolbar).
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Gets or sets a value indicating whether the boolean is negated before conversion.</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the "not visible" result is
    /// <see cref="Visibility.Hidden"/> (space reserved) rather than <see cref="Visibility.Collapsed"/>.
    /// </summary>
    public bool UseHidden { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            null => false,
            // Nullable<bool> arrives already unboxed as bool; anything else is a binding mistake but
            // must not crash the visual tree, so a non-null non-bool counts as "present" => true.
            _ => true
        };

        if (Invert)
            flag = !flag;

        return flag ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: <see cref="Visibility.Hidden"/> and
    /// <see cref="Visibility.Collapsed"/> both map back to <see langword="false"/>, so the round trip is lossy.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BooleanToVisibilityConverter)} is a one-way converter.");
}
