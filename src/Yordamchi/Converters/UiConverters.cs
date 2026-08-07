using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Yordamchi.Converters;

// =====================================================================================
//  Bosh sahifa (Dashboard) va universal ishchi oyna (ToolWorkspace) uchun qo'shimcha
//  konverterlar. Mavjud Converters/*.cs fayllariga tegilmagan — bu yerdagilar faqat
//  yangi UI ga kerak bo'lgan uchta holatni yopadi:
//    1. "#RRGGBB" matnidan rang namunasi (suv belgisi rangi tanlash),
//    2. 0..1 oralig'idagi kasrni foizga aylantirish (shaffoflik slayderi yorlig'i),
//    3. enum qiymati bo'yicha elementni ko'rsatish/yashirish (sozlamalar paneli).
// =====================================================================================

/// <summary>
/// <c>"#RRGGBB"</c> yoki <c>"#AARRGGBB"</c> ko'rinishidagi matnni muzlatilgan
/// <see cref="SolidColorBrush"/> ga aylantiradi. Noto'g'ri matn kelsa — shaffof cho'tka.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class ColorHexToBrushConverter : IValueConverter
{
    /// <summary>Bir xil matn uchun har safar yangi cho'tka yasamaslik uchun kichik keshdir.</summary>
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object CacheLock = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(hex, out var cached))
                return cached;

            SolidColorBrush brush;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                brush = new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                // Foydalanuvchi qo'lda yozgan matn hali to'liq bo'lmasligi mumkin ("#E5"):
                // bunday holatda bindingni buzmaymiz, shunchaki shaffof qaytaramiz.
                return Brushes.Transparent;
            }

            // Muzlatilgan cho'tka — har qanday oqimdan xavfsiz ishlatiladi va tezroq chiziladi.
            brush.Freeze();
            Cache[hex] = brush;
            return brush;
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Har doim: cho'tkadan asl matnni tiklash talab qilinmaydi.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(ColorHexToBrushConverter)} — bir tomonlama konverter.");
}

/// <summary>
/// 0…1 oralig'idagi kasrni <c>"25%"</c> ko'rinishidagi matnga aylantiradi (shaffoflik slayderi).
/// <c>ConverterParameter</c> orqali boshqa ko'paytuvchi berish mumkin (masalan 1 — qiymat allaqachon foizda).
/// </summary>
[ValueConversion(typeof(double), typeof(string))]
public sealed class FractionToPercentConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double fraction || double.IsNaN(fraction) || double.IsInfinity(fraction))
            return string.Empty;

        var scale = 100d;
        if (parameter is string raw && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            scale = parsed;

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(fraction * scale)}%");
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Har doim: matn faqat ko'rsatish uchun.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(FractionToPercentConverter)} — bir tomonlama konverter.");
}

/// <summary>
/// Bog'langan enum qiymati <c>ConverterParameter</c> ga teng bo'lsa elementni ko'rsatadi.
/// Sozlamalar panelida bir rejimga tegishli maydonlarni yashirish uchun ishlatiladi
/// (masalan "oraliqlar" matn maydoni faqat <c>SplitMode.Ranges</c> da ko'rinadi).
/// </summary>
[ValueConversion(typeof(Enum), typeof(Visibility), ParameterType = typeof(object))]
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
    /// <summary>Natijani teskarisiga o'giradi: qiymat mos kelmaganda ko'rinadi.</summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var matches = Matches(value, parameter);

        if (Invert)
            matches = !matches;

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(object? value, object? parameter)
    {
        if (value is null || parameter is null)
            return false;

        if (value.Equals(parameter))
            return true;

        // XAML parametrni matn sifatida uzatadi — uni bog'langan qiymatning enum turiga o'tkazamiz.
        var valueType = value.GetType();
        if (valueType.IsEnum && parameter is string name)
        {
            return Enum.TryParse(valueType, name, ignoreCase: true, out var parsed)
                && value.Equals(parsed);
        }

        return false;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Har doim: <see cref="Visibility"/> da enum qiymati saqlanmaydi.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(EnumEqualsToVisibilityConverter)} — bir tomonlama konverter.");
}
