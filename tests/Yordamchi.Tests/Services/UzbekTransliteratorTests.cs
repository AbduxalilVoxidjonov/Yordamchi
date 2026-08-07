using Yordamchi.Models;
using Yordamchi.Services.Conversion;

namespace Yordamchi.Tests.Services;

/// <summary>
/// O'girish qoidalari. Bu yerda mock ham, fayl ham yo'q — sof mantiq, shuning uchun har bir
/// qoida aynan misol bilan qulflanadi: qoidalarning ko'pchiligi atrofdagi harflarga bog'liq va
/// bitta o'zgartirish jimgina boshqa so'zni buzib qo'yishi mumkin.
/// </summary>
public sealed class UzbekTransliteratorTests
{
    private static readonly TransliterationOptions ToLatin = new()
    {
        Direction = TransliterationDirection.CyrillicToLatin
    };

    private static readonly TransliterationOptions ToCyrillic = new()
    {
        Direction = TransliterationDirection.LatinToCyrillic
    };

    // =================================================================================
    //  Kirill → Lotin
    // =================================================================================

    [Theory]
    [InlineData("Ўзбекистон Республикаси", "O'zbekiston Respublikasi")]
    [InlineData("қишлоқ ғалаба ҳовли", "qishloq g'alaba hovli")]
    [InlineData("шошилинч чойхона", "shoshilinch choyxona")]
    [InlineData("ёмон ёзувчи юрак ялпи", "yomon yozuvchi yurak yalpi")]
    [InlineData("халқ хат ҳовуз", "xalq xat hovuz")]
    public void Cyrillic_letters_become_their_latin_pairs(string cyrillic, string latin)
        => Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));

    [Theory]
    [InlineData("Эшик", "Eshik")]           // э doim "e"
    [InlineData("ердан етти", "yerdan yetti")] // е so'z boshida "ye"
    [InlineData("мен келдим", "men keldim")]   // undoshdan keyin "e"
    [InlineData("поезд", "poyezd")]            // unlidan keyin "ye"
    public void The_letter_e_depends_on_what_comes_before_it(string cyrillic, string latin)
        => Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));

    [Theory]
    [InlineData("революция конституция", "revolyutsiya konstitutsiya")] // unlidan keyin "ts"
    [InlineData("лекция функция", "leksiya funksiya")]                  // undoshdan keyin "s"
    [InlineData("цирк цех", "sirk sex")]                                // so'z boshida "s"
    public void The_letter_tse_depends_on_what_comes_before_it(string cyrillic, string latin)
        => Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));

    [Theory]
    [InlineData("маъно санъат шеър", "ma'no san'at she'r")] // ъ — tutuq belgisi
    [InlineData("объект субъект", "obyekt subyekt")]        // ъ + е → faqat "ye"
    [InlineData("фильм", "film")]                           // ь butunlay tushadi
    public void The_hard_and_soft_signs_follow_the_official_spelling(string cyrillic, string latin)
        => Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));

    [Theory]
    [InlineData("ЎЗБЕКИСТОН", "O'ZBEKISTON")]
    [InlineData("ШАҲАР ЧОЙ", "SHAHAR CHOY")]
    [InlineData("Шаҳар Чой Ёмон", "Shahar Choy Yomon")]
    [InlineData("Ў", "O'")]
    public void Capital_letters_keep_the_shape_of_the_word(string cyrillic, string latin)
        => Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));

    [Fact]
    public void The_official_apostrophes_are_used_when_asked_for()
    {
        var options = ToLatin with { Apostrophe = ApostropheStyle.Typographic };

        Assert.Equal("toʻgʻri maʼno", UzbekTransliterator.Convert("тўғри маъно", options));
    }

    // =================================================================================
    //  Lotin → Kirill
    // =================================================================================

    [Theory]
    [InlineData("O'zbekiston Respublikasi", "Ўзбекистон Республикаси")]
    [InlineData("qishloq g'alaba hovli", "қишлоқ ғалаба ҳовли")]
    [InlineData("shoshilinch choyxona", "шошилинч чойхона")]
    [InlineData("xalq hovli", "халқ ҳовли")]
    public void Latin_letters_become_their_cyrillic_pairs(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    [Theory]
    [InlineData("eshik", "эшик")]        // so'z boshidagi "e" — э
    [InlineData("men keldim", "мен келдим")]
    [InlineData("yerdan yetti", "ердан етти")]
    [InlineData("poyezd", "поезд")]
    public void The_letter_e_depends_on_where_it_stands(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    [Theory]
    [InlineData("yo'l yo'q", "йўл йўқ")]   // "yo" emas: y + o'
    [InlineData("yog'och", "ёғоч")]        // bu esa aynan "yo"
    [InlineData("quyosh sayohat dunyo", "қуёш саёҳат дунё")]
    [InlineData("may tuya", "май туя")]
    [InlineData("ya'ni", "яъни")]
    public void The_letter_y_is_read_together_with_the_vowel_after_it(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    [Theory]
    [InlineData("revolyutsiya konstitutsiya", "революция конституция")]
    [InlineData("aviatsion", "авиацион")]
    [InlineData("tsex", "цех")]
    public void The_pair_ts_becomes_tse_only_where_it_is_certain(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    [Fact]
    public void Uzbek_verbs_are_not_mistaken_for_loan_words()
    {
        // "ketsa" → "кеца" eng og'riqli xato bo'lardi: bunday shakl matnda juda ko'p uchraydi.
        Assert.Equal("кетса айтса сотса", UzbekTransliterator.Convert("ketsa aytsa sotsa", ToCyrillic));
    }

    [Theory]
    [InlineData("toʻgʻri boʻldi")]  // U+02BB
    [InlineData("to‘g‘ri bo‘ldi")]  // U+2018
    [InlineData("to’g’ri bo’ldi")]  // U+2019
    [InlineData("to'g'ri bo'ldi")]  // ASCII
    public void Every_apostrophe_the_user_may_type_is_understood(string latin)
        => Assert.Equal("тўғри бўлди", UzbekTransliterator.Convert(latin, ToCyrillic));

    [Theory]
    [InlineData("O'ZBEKISTON", "ЎЗБЕКИСТОН")]
    [InlineData("Shahar Choy Yomon", "Шаҳар Чой Ёмон")]
    public void Capital_latin_letters_keep_their_case(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    // =================================================================================
    //  Tegilmaydigan joylar
    // =================================================================================

    [Theory]
    [InlineData("sayt www.google.com da", "сайт www.google.com да")]
    [InlineData("ischoolk@gmail.com ga", "ischoolk@gmail.com га")]
    [InlineData("https://github.com/Yordamchi ochildi", "https://github.com/Yordamchi очилди")]
    public void Links_and_e_mail_addresses_stay_as_they_are(string latin, string cyrillic)
        => Assert.Equal(cyrillic, UzbekTransliterator.Convert(latin, ToCyrillic));

    [Fact]
    public void Numbers_and_punctuation_pass_through_untouched()
        => Assert.Equal("PDF hujjat — 25 bet, 3.5 MB", UzbekTransliterator.Convert("PDF hujjat — 25 бет, 3.5 MB", ToLatin));

    [Fact]
    public void An_empty_text_produces_an_empty_result()
    {
        Assert.Equal(string.Empty, UzbekTransliterator.Convert(null, ToLatin));
        Assert.Equal(string.Empty, UzbekTransliterator.Convert(string.Empty, ToCyrillic));
    }

    // =================================================================================
    //  Aylanma (round-trip)
    // =================================================================================

    [Theory]
    [InlineData("O'zbekiston Respublikasi Prezidenti")]
    [InlineData("Toshkent shahri, Yunusobod tumani")]
    [InlineData("qishloq xo'jaligi mahsulotlari")]
    [InlineData("Bugun havo issiq bo'ladi, deb aytishdi.")]
    [InlineData("Yangi o'quv yili 2-sentyabrda boshlanadi.")]
    public void Latin_survives_a_round_trip_through_cyrillic(string latin)
    {
        var cyrillic = UzbekTransliterator.Convert(latin, ToCyrillic);

        Assert.Equal(latin, UzbekTransliterator.Convert(cyrillic, ToLatin));
    }

    // =================================================================================
    //  Yo'nalishni aniqlash
    // =================================================================================

    [Fact]
    public void The_direction_is_read_from_the_text_itself()
    {
        Assert.Equal(TransliterationDirection.CyrillicToLatin, UzbekTransliterator.Detect("Ўзбекистон"));
        Assert.Equal(TransliterationDirection.LatinToCyrillic, UzbekTransliterator.Detect("O'zbekiston"));
    }

    [Fact]
    public void Without_a_single_letter_there_is_nothing_to_detect()
    {
        Assert.Null(UzbekTransliterator.Detect("12345 — 6.7"));
        Assert.Null(UzbekTransliterator.Detect(string.Empty));
        Assert.Null(UzbekTransliterator.Detect(null));
    }

    [Fact]
    public void Resolve_locks_the_direction_in_before_the_work_starts()
    {
        var auto = new TransliterationOptions
        {
            Direction = TransliterationDirection.CyrillicToLatin,
            AutoDetectDirection = true
        };

        var resolved = UzbekTransliterator.Resolve(auto, "Toshkent");

        Assert.Equal(TransliterationDirection.LatinToCyrillic, resolved.Direction);

        // Aniqlangandan keyin bayroq o'chadi: quyi qatlamlar ikkinchi marta taxmin qilmasin.
        Assert.False(resolved.AutoDetectDirection);
    }

    [Fact]
    public void An_undetectable_sample_leaves_the_chosen_direction_alone()
    {
        var auto = new TransliterationOptions
        {
            Direction = TransliterationDirection.LatinToCyrillic,
            AutoDetectDirection = true
        };

        var resolved = UzbekTransliterator.Resolve(auto, "2026");

        Assert.Equal(TransliterationDirection.LatinToCyrillic, resolved.Direction);
        Assert.False(resolved.AutoDetectDirection);
    }

    // =================================================================================
    //  Bo'lak-bo'lak o'girish (Word hujjati shunga tayanadi)
    // =================================================================================

    [Fact]
    public void Segments_report_where_every_piece_came_from()
    {
        var pieces = new List<(int Start, int Length, string Output)>();

        UzbekTransliterator.Convert("Ўш", ToLatin, (start, length, output) =>
            pieces.Add((start, length, output.ToString())));

        Assert.Equal(new[] { (0, 1, "O'"), (1, 1, "sh") }, pieces);
    }

    [Fact]
    public void A_two_letter_latin_pair_is_reported_as_one_segment()
    {
        var pieces = new List<(int Start, int Length, string Output)>();

        UzbekTransliterator.Convert("o'sh", ToCyrillic, (start, length, output) =>
            pieces.Add((start, length, output.ToString())));

        Assert.Equal(new[] { (0, 2, "ў"), (2, 2, "ш") }, pieces);
    }
}
