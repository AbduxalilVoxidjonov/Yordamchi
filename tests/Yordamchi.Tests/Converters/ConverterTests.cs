using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Yordamchi.Converters;
using Yordamchi.Models;

namespace Yordamchi.Tests.Converters;

/// <summary>
/// Konvertorlar — UI ning eng jim buziladigan joyi: xato natija istisno tashlamaydi, shunchaki
/// element ko'rinmay qoladi yoki noto'g'ri matn chiqadi. Shuning uchun ular chegaraviy
/// qiymatlar (null, manfiy, noto'g'ri tur) bilan alohida tekshiriladi.
/// </summary>
public sealed class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // =================================================================================
    //  FileSizeConverter
    // =================================================================================

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    public void FileSize_formats_bytes_in_the_largest_fitting_unit(long bytes, string expected)
    {
        var result = new FileSizeConverter().Convert(bytes, typeof(string), null, Culture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FileSize_stops_at_gigabytes_instead_of_inventing_a_unit()
    {
        // Units massivi GB bilan tugaydi; undan kattasi ham GB da ko'rsatilishi kerak,
        // massivdan chiqib ketmasligi emas.
        var result = new FileSizeConverter().Convert(5L * 1024 * 1024 * 1024, typeof(string), null, Culture);

        Assert.Equal("5.0 GB", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData("hajm emas")]
    public void FileSize_returns_unset_for_values_it_cannot_format(object? value)
    {
        var result = new FileSizeConverter().Convert(value, typeof(string), null, Culture);

        Assert.Equal(DependencyProperty.UnsetValue, result);
    }

    [Fact]
    public void FileSize_is_one_way_only() =>
        Assert.Throws<NotSupportedException>(
            () => new FileSizeConverter().ConvertBack("1 KB", typeof(long), null, Culture));

    // =================================================================================
    //  EnumEqualsConverter — RadioButton'larni enum ga bog'laydi
    // =================================================================================

    [Fact]
    public void EnumEquals_matches_the_parameter_by_name()
    {
        var converter = new EnumEqualsConverter();

        Assert.Equal(true, converter.Convert(ArchiveCompressionLevel.Maximum, typeof(bool), "Maximum", Culture));
        Assert.Equal(false, converter.Convert(ArchiveCompressionLevel.Maximum, typeof(bool), "Fast", Culture));
    }

    [Fact]
    public void EnumEquals_ignores_case_because_xaml_authors_do()
    {
        var result = new EnumEqualsConverter()
            .Convert(ArchiveCompressionLevel.Store, typeof(bool), "store", Culture);

        Assert.Equal(true, result);
    }

    [Theory]
    [InlineData(null, "Store")]
    [InlineData(ArchiveCompressionLevel.Store, null)]
    [InlineData(ArchiveCompressionLevel.Store, "BundayQiymatYoq")]
    public void EnumEquals_is_false_when_it_cannot_decide(object? value, object? parameter) =>
        Assert.Equal(false, new EnumEqualsConverter().Convert(value, typeof(bool), parameter, Culture));

    [Fact]
    public void EnumEquals_writes_the_enum_back_when_the_radio_is_checked()
    {
        var result = new EnumEqualsConverter()
            .ConvertBack(true, typeof(ArchiveCompressionLevel), "Maximum", Culture);

        Assert.Equal(ArchiveCompressionLevel.Maximum, result);
    }

    [Fact]
    public void EnumEquals_ignores_the_unchecked_radio_instead_of_clearing_the_choice()
    {
        // Guruhda tanlov almashganda eski RadioButton IsChecked=false yuboradi. Agar bu
        // yozib qo'yilsa, yangi tanlov darhol o'chib ketardi.
        var result = new EnumEqualsConverter()
            .ConvertBack(false, typeof(ArchiveCompressionLevel), "Maximum", Culture);

        Assert.Equal(Binding.DoNothing, result);
    }

    // =================================================================================
    //  BooleanToVisibilityConverter
    // =================================================================================

    [Theory]
    [InlineData(true, false, Visibility.Visible)]
    [InlineData(false, false, Visibility.Collapsed)]
    [InlineData(true, true, Visibility.Collapsed)]
    [InlineData(false, true, Visibility.Visible)]
    public void BoolToVisibility_honours_the_invert_flag(bool value, bool invert, Visibility expected)
    {
        var converter = new BooleanToVisibilityConverter { Invert = invert };

        Assert.Equal(expected, converter.Convert(value, typeof(Visibility), null, Culture));
    }

    [Fact]
    public void BoolToVisibility_can_reserve_the_layout_slot_with_hidden()
    {
        var converter = new BooleanToVisibilityConverter { UseHidden = true };

        Assert.Equal(Visibility.Hidden, converter.Convert(false, typeof(Visibility), null, Culture));
    }

    [Fact]
    public void BoolToVisibility_treats_null_as_false()
    {
        var converter = new BooleanToVisibilityConverter();

        Assert.Equal(Visibility.Collapsed, converter.Convert(null, typeof(Visibility), null, Culture));
    }

    // =================================================================================
    //  NullOrEmptyToVisibilityConverter
    // =================================================================================

    [Theory]
    [InlineData("matn", Visibility.Visible)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("   ", Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void NullOrEmpty_hides_blank_text(string? value, Visibility expected)
    {
        var converter = new NullOrEmptyToVisibilityConverter();

        Assert.Equal(expected, converter.Convert(value, typeof(Visibility), null, Culture));
    }

    // =================================================================================
    //  CountToVisibilityConverter
    // =================================================================================

    [Fact]
    public void Count_shows_the_panel_only_when_the_collection_has_items()
    {
        var converter = new CountToVisibilityConverter();

        Assert.Equal(Visibility.Collapsed, converter.Convert(Array.Empty<string>(), typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Visible, converter.Convert(new[] { "a" }, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(0, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Visible, converter.Convert(3, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(null, typeof(Visibility), null, Culture));
    }

    [Fact]
    public void Count_inverted_drives_the_empty_state_panel()
    {
        var converter = new CountToVisibilityConverter { Invert = true };

        Assert.Equal(Visibility.Visible, converter.Convert(0, typeof(Visibility), null, Culture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(2, typeof(Visibility), null, Culture));
    }

    // =================================================================================
    //  MathMultiplyConverter
    // =================================================================================

    [Fact]
    public void Multiply_scales_the_value_and_can_scale_it_back()
    {
        var converter = new MathMultiplyConverter();

        Assert.Equal(30d, converter.Convert(10d, typeof(double), 3d, Culture));
        Assert.Equal(10d, converter.ConvertBack(30d, typeof(double), 3d, Culture));
    }

    [Fact]
    public void Multiply_refuses_to_divide_by_zero()
    {
        var converter = new MathMultiplyConverter();

        Assert.Equal(DependencyProperty.UnsetValue, converter.ConvertBack(30d, typeof(double), 0d, Culture));
    }

    [Theory]
    [InlineData(null, 2d)]
    [InlineData(10d, null)]
    [InlineData("son emas", 2d)]
    [InlineData(double.NaN, 2d)]
    public void Multiply_returns_unset_for_unusable_input(object? value, object? parameter) =>
        Assert.Equal(
            DependencyProperty.UnsetValue,
            new MathMultiplyConverter().Convert(value, typeof(double), parameter, Culture));
}
