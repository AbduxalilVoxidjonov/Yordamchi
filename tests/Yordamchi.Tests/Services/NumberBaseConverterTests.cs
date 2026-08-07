using Yordamchi.Services.Conversion;

namespace Yordamchi.Tests.Services;

/// <summary>
/// Sanoq sistemalari orasida o'tkazish qoidalari. Sof mantiq — mock ham, fayl ham yo'q.
/// <para>
/// E'tibor qaratiladigan asosiy narsa — <b>aniqlik</b>: hisob <c>BigInteger</c> va ratsional
/// arifmetika ustida boradi, shuning uchun uzun sonlar ham, kasrlar ham buzilmasligi kerak.
/// </para>
/// </summary>
public sealed class NumberBaseConverterTests
{
    private const int Digits = 16;

    private static string Convert(string text, int from, int to, int fractionDigits = Digits)
        => NumberBaseConverter.Convert(text, from, to, fractionDigits).Value;

    // =================================================================================
    //  Butun sonlar
    // =================================================================================

    [Theory]
    [InlineData("255", 10, 2, "11111111")]
    [InlineData("255", 10, 8, "377")]
    [InlineData("255", 10, 16, "FF")]
    [InlineData("255", 10, 32, "7V")]
    [InlineData("11111111", 2, 10, "255")]
    [InlineData("377", 8, 10, "255")]
    [InlineData("FF", 16, 10, "255")]
    [InlineData("7V", 32, 10, "255")]
    public void Whole_numbers_move_between_the_usual_bases(string text, int from, int to, string expected)
        => Assert.Equal(expected, Convert(text, from, to));

    [Fact]
    public void Lower_case_hexadecimal_letters_are_understood()
        => Assert.Equal("255", Convert("ff", 16, 10));

    [Theory]
    [InlineData("0", 10, 2, "0")]
    [InlineData("0", 2, 32, "0")]
    [InlineData("007", 10, 2, "111")]      // boshidagi nollar qiymatga ta'sir qilmaydi
    [InlineData("-42", 10, 2, "-101010")]
    [InlineData("-101010", 2, 10, "-42")]
    [InlineData("-0", 10, 2, "0")]         // "-0" degan son yo'q
    public void Edge_cases_produce_the_expected_shape(string text, int from, int to, string expected)
        => Assert.Equal(expected, Convert(text, from, to));

    [Fact]
    public void Grouping_spaces_may_be_typed_back_in()
    {
        // Ro'yxatdagi natija "1111 1111" ko'rinishida ko'rinadi — uni qaytadan kiritish ishlashi kerak.
        Assert.Equal("FF", Convert("1111 1111", 2, 16));
    }

    // =================================================================================
    //  Kasrlar
    // =================================================================================

    [Theory]
    [InlineData("25.5", 10, 2, "11001.1")]
    [InlineData("11001.1", 2, 10, "25.5")]
    [InlineData("1A.8", 16, 10, "26.5")]
    [InlineData("0.5", 10, 16, "0.8")]
    [InlineData("0.5", 10, 32, "0.G")]
    [InlineData("-0.75", 10, 2, "-0.11")]
    [InlineData("1.500", 10, 2, "1.1")]    // oxiridagi nollar qiymatga ta'sir qilmaydi
    [InlineData(".5", 10, 2, "0.1")]       // butun qismsiz yozuv
    public void Fractions_move_between_bases(string text, int from, int to, string expected)
        => Assert.Equal(expected, Convert(text, from, to));

    [Fact]
    public void An_endless_fraction_is_cut_at_the_chosen_length()
    {
        // 0.1₁₀ ikkilikda davriy kasr: 0.0001100110011…
        Assert.Equal("0.00011001", Convert("0.1", 10, 2, 8));
        Assert.Equal("0.0001100110011001", Convert("0.1", 10, 2, 16));
    }

    [Fact]
    public void A_cut_result_is_marked_as_inexact()
    {
        Assert.False(NumberBaseConverter.Convert("0.1", 10, 2, Digits).IsExact);
        Assert.True(NumberBaseConverter.Convert("0.5", 10, 2, Digits).IsExact);
        Assert.True(NumberBaseConverter.Convert("255", 10, 16, Digits).IsExact);
    }

    [Fact]
    public void A_fraction_is_never_rounded_up()
    {
        // Kesish — darslikdagi "ketma-ket ko'paytirish" algoritmi qiladigan ish. Yaxlitlansa
        // natija qadam-baqadam yechim bilan mos kelmay qolardi.
        Assert.Equal("0.0111", Convert("0.499", 10, 2, 4));
    }

    // =================================================================================
    //  Aniqlik — bu yerda double yiqilardi
    // =================================================================================

    [Fact]
    public void A_number_longer_than_any_machine_type_survives_a_round_trip()
    {
        var value = new string('9', 40);

        var hex = Convert(value, 10, 16);

        Assert.Equal(value, Convert(hex, 16, 10));
    }

    [Fact]
    public void The_largest_unsigned_64_bit_value_is_exact()
        => Assert.Equal("FFFFFFFFFFFFFFFF", Convert("18446744073709551615", 10, 16));

    [Fact]
    public void Every_pair_of_bases_survives_a_round_trip()
    {
        // Butun son har qanday asosda aniq ifodalanadi, ya'ni 31 × 31 juftlikning hammasi
        // asl qiymatni qaytarishi shart.
        foreach (var from in NumberBaseConverter.SupportedBases)
        {
            foreach (var to in NumberBaseConverter.SupportedBases)
            {
                var inFrom = Convert("123456789", 10, from);
                var inTo = Convert(inFrom, from, to);

                Assert.Equal("123456789", Convert(inTo, to, 10));
            }
        }
    }

    [Fact]
    public void An_exact_fraction_survives_a_round_trip_through_even_bases()
    {
        // 0.75 = 3/4 — u faqat 2 ga karrali asosda aniq yoziladi. Toq asosda cheksiz kasrga
        // aylanishi matematik haqiqat, xato emas.
        foreach (var from in NumberBaseConverter.SupportedBases.Where(radix => radix % 2 == 0))
        {
            var inFrom = NumberBaseConverter.Convert("123456.75", 10, from, 40);

            Assert.True(inFrom.IsExact);
            Assert.Equal("123456.75", Convert(inFrom.Value, from, 10, 40));
        }
    }

    // =================================================================================
    //  Tekshirish
    // =================================================================================

    [Fact]
    public void A_digit_outside_the_base_is_named_in_the_message()
    {
        var error = NumberBaseConverter.Validate("102", 2);

        Assert.NotNull(error);
        Assert.Contains("«2»", error);
        Assert.Contains("0 va 1", error);
    }

    [Theory]
    [InlineData("1.2.3", 10)]
    [InlineData(".", 10)]
    [InlineData("Z", 32)]
    [InlineData("8", 8)]
    [InlineData("G", 16)]
    public void Impossible_input_is_refused(string text, int radix)
        => Assert.NotNull(NumberBaseConverter.Validate(text, radix));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_field_is_not_an_error(string? text)
    {
        // Foydalanuvchi hali yozishni boshlamagan — unga qizil xabar ko'rsatish keraksiz.
        Assert.Null(NumberBaseConverter.Validate(text, 10));
    }

    [Fact]
    public void A_valid_number_reports_no_error()
    {
        Assert.Null(NumberBaseConverter.Validate("1A.8", 16));
        Assert.Null(NumberBaseConverter.Validate("-11.01", 2));
    }

    [Fact]
    public void An_absurdly_long_number_is_refused_before_the_work_starts()
    {
        var error = NumberBaseConverter.Validate(new string('1', NumberBaseConverter.MaxInputLength + 1), 10);

        Assert.NotNull(error);
        Assert.Contains("uzun", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(0)]
    public void A_base_outside_the_supported_range_is_a_programming_error(int radix)
        => Assert.Throws<ArgumentOutOfRangeException>(() => NumberBaseConverter.Validate("1", radix));

    // =================================================================================
    //  Nomlash
    // =================================================================================

    [Theory]
    [InlineData(2, "ikkilik")]
    [InlineData(8, "sakkizlik")]
    [InlineData(10, "o'nlik")]
    [InlineData(16, "o'n oltilik")]
    [InlineData(20, "yigirmalik")]
    [InlineData(21, "yigirma birlik")]
    [InlineData(30, "o'ttizlik")]
    [InlineData(32, "o'ttiz ikkilik")]
    public void Bases_are_named_in_uzbek(int radix, string expected)
        => Assert.Equal(expected, NumberBaseConverter.DescribeBase(radix));

    [Fact]
    public void The_list_label_carries_both_the_number_and_the_name()
        => Assert.Equal("16-lik — o'n oltilik", NumberBaseConverter.LabelBase(16));

    [Theory]
    [InlineData(2, "0 va 1")]
    [InlineData(8, "0–7")]
    [InlineData(16, "0–9 va A–F")]
    [InlineData(32, "0–9 va A–V")]
    public void The_allowed_digits_are_spelled_out(int radix, string expected)
        => Assert.Equal(expected, NumberBaseConverter.DigitsOf(radix));

    [Fact]
    public void Every_supported_base_has_a_name_and_a_digit_list()
    {
        Assert.Equal(31, NumberBaseConverter.SupportedBases.Count);

        foreach (var radix in NumberBaseConverter.SupportedBases)
        {
            Assert.False(string.IsNullOrWhiteSpace(NumberBaseConverter.DescribeBase(radix)));
            Assert.False(string.IsNullOrWhiteSpace(NumberBaseConverter.DigitsOf(radix)));
        }
    }

    // =================================================================================
    //  Guruhlash (faqat ko'rsatish uchun)
    // =================================================================================

    [Theory]
    [InlineData("11111111", 2, "1111 1111")]
    [InlineData("DEADBEEF", 16, "DEAD BEEF")]
    [InlineData("1234567", 10, "1 234 567")]
    [InlineData("-11111111", 2, "-1111 1111")]
    [InlineData("11001.1011001", 2, "1 1001.1011 001")]
    [InlineData("12341234", 5, "12341234")]   // 5-likda guruhlashning odatiy qoidasi yo'q
    [InlineData("101", 2, "101")]             // guruhdan qisqa
    public void Long_results_are_grouped_for_reading(string value, int radix, string expected)
        => Assert.Equal(expected, NumberBaseConverter.Group(value, radix));

    // =================================================================================
    //  Qadam-baqadam yechim
    // =================================================================================

    [Fact]
    public void Converting_from_hex_to_binary_takes_three_steps_plus_the_answer()
    {
        var sections = NumberBaseConverter.Explain("1A.8", 16, 2, Digits);

        // 10-likka yoyish → butun qismni bo'lish → kasr qismini ko'paytirish → natija.
        Assert.Equal(4, sections.Count);
        Assert.Contains("10-lik", sections[0].Title);
        Assert.Contains("butun qismni", sections[1].Title);
        Assert.Contains("kasr qismini", sections[2].Title);
        Assert.Equal("Natija", sections[3].Title);
        Assert.Equal("1A.8₁₆ = 11010.1₂", sections[3].Summary);
    }

    [Fact]
    public void A_decimal_source_skips_the_expansion_step()
    {
        var sections = NumberBaseConverter.Explain("25", 10, 2, Digits);

        Assert.Equal(2, sections.Count);
        Assert.Contains("butun qismni", sections[0].Title);
        Assert.Equal("25₁₀ = 11001₂", sections[1].Summary);
    }

    [Fact]
    public void A_decimal_target_skips_the_division_step()
    {
        var sections = NumberBaseConverter.Explain("FF", 16, 10, Digits);

        Assert.Equal(2, sections.Count);
        Assert.Contains("10-lik", sections[0].Title);
        Assert.Equal("FF₁₆ = 255₁₀", sections[1].Summary);
    }

    [Fact]
    public void The_division_steps_show_every_remainder()
    {
        var section = NumberBaseConverter.Explain("25", 10, 2, Digits)[0];

        Assert.Equal(
            new[]
            {
                "25 ÷ 2 = 12, qoldiq 1",
                "12 ÷ 2 = 6, qoldiq 0",
                "6 ÷ 2 = 3, qoldiq 0",
                "3 ÷ 2 = 1, qoldiq 1",
                "1 ÷ 2 = 0, qoldiq 1"
            },
            section.Lines);

        Assert.Contains("11001", section.Summary);
    }

    [Fact]
    public void The_expansion_shows_the_decimal_value_of_letter_digits()
    {
        var lines = NumberBaseConverter.Explain("1A.8", 16, 2, Digits)[0].Lines;

        Assert.Equal("1 × 16¹ = 16", lines[0]);
        Assert.Equal("A (10) × 16⁰ = 10", lines[1]);
        Assert.Equal("8 × 16⁻¹ = 0.5", lines[2]);
    }

    [Fact]
    public void An_endless_fraction_says_so_in_the_summary()
    {
        var sections = NumberBaseConverter.Explain("0.1", 10, 2, 8);
        var multiplication = sections.Single(section => section.Title.Contains("kasr qismini"));

        Assert.Contains("cheksiz", multiplication.Summary);
        Assert.Contains("0.00011001", multiplication.Summary);

        // Taqribiy natija oxirgi bo'limda "=" emas, "≈" bilan yoziladi.
        Assert.Equal("0.1₁₀ ≈ 0.00011001₂", sections[^1].Summary);
    }

    [Fact]
    public void The_same_base_on_both_sides_needs_no_work()
    {
        var sections = NumberBaseConverter.Explain("25", 10, 10, Digits);

        Assert.Single(sections);
        Assert.Contains("kerak emas", sections[0].Title);
    }

    [Fact]
    public void An_invalid_number_has_nothing_to_explain()
        => Assert.Empty(NumberBaseConverter.Explain("2", 2, 10, Digits));

    [Fact]
    public void A_very_long_explanation_is_shortened()
    {
        // 64 xonali kasr 64 ta qatorni bermasligi kerak — o'ng panel o'qib bo'lmas holga kelardi.
        var section = NumberBaseConverter.Explain("0.1", 10, 2, 64)
            .Single(part => part.Title.Contains("kasr qismini"));

        Assert.True(section.Lines.Count < 20);
        Assert.Contains(section.Lines, line => line.StartsWith('…'));
    }
}
