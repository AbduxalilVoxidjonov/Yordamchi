using System.Globalization;
using System.Numerics;
using System.Text;
using Yordamchi.Models;

namespace Yordamchi.Services.Conversion;

/// <summary>
/// Sanoq sistemalari orasida o'tkazish qoidalari: ikkining darajalari (2, 4, 8, 16, 32, 64,
/// 128, 256) va kundalik 10-lik; butun va kasr qism, hamda qadam-baqadam yechim. Sof mantiq —
/// fayl ham, UI ham bilmaydi.
/// <para>
/// <b>Aniqlik.</b> Hisob <see cref="BigInteger"/> ustida olib boriladi, ya'ni butun qism
/// uzunligidan qat'i nazar aniq qoladi. Kasr qism esa <c>surat/maxraj</c> ko'rinishida —
/// <c>0.1₁₀</c> = <c>1/10</c> — saqlanadi va yangi asosga ratsional arifmetika bilan
/// o'tkaziladi. <c>double</c> ishlatilganda <c>0.1</c> allaqachon taqribiy bo'lardi va
/// uzun kasrlarda xato to'planib borardi.
/// </para>
/// <para>
/// <b>Kesish, yaxlitlash emas.</b> Cheksiz kasr belgilangan xonada shunchaki kesiladi —
/// aynan shu darslikdagi "ketma-ket ko'paytirish" algoritmi qiladigan ish, ya'ni natija
/// qadam-baqadam yechim bilan bir xil chiqadi. Bunday natija
/// <see cref="NumberConversionResult.IsExact"/> orqali belgilanadi.
/// </para>
/// <para>
/// <b>Ikki xil yozuv.</b> 32-likkacha har bir raqam bitta belgi bilan yoziladi: 0–9, so'ng
/// A–V. 64, 128 va 256-lik uchun bunday belgi yetmaydi, shuning uchun har bir raqam o'nlikda
/// yoziladi va <c>:</c> bilan ajratiladi — <c>12345678₁₀</c> = <c>188:97:78₂₅₆</c>. Base64
/// alifbosi 64-lik uchun ixcham bo'lardi, lekin 128 va 256-likka baribir yetmaydi: bitta
/// qoida uchala asos uchun ham ishlagani ma'qul.
/// </para>
/// </summary>
public static class NumberBaseConverter
{
    /// <summary>
    /// Qo'llab-quvvatlanadigan asoslar: ikkining darajalari va kundalik 10-lik. Oraliqdagi
    /// asoslar (3, 5, 6, 7, …) ataylab yo'q — amalda ular ishlatilmaydi.
    /// </summary>
    public static IReadOnlyList<int> SupportedBases { get; } = [2, 4, 8, 10, 16, 32, 64, 128, 256];

    private static readonly HashSet<int> SupportedSet = [.. SupportedBases];

    /// <summary>Eng kichik asos (2).</summary>
    public const int MinBase = 2;

    /// <summary>Eng katta asos (256).</summary>
    public const int MaxBase = 256;

    /// <summary>
    /// Shu asosgacha raqam bitta belgi bilan yoziladi; undan kattasida raqamlar o'nlikda
    /// yozilib <see cref="DigitSeparator"/> bilan ajratiladi.
    /// </summary>
    public const int MaxSymbolBase = 32;

    /// <summary>64, 128 va 256-likda raqamlarni ajratuvchi belgi.</summary>
    public const char DigitSeparator = ':';

    /// <summary>Kiritish uzunligi chegarasi — juda uzun sondan UI sekinlashmasligi uchun.</summary>
    public const int MaxInputLength = 512;

    public const int MinFractionDigits = 4;
    public const int MaxFractionDigits = 64;

    /// <summary>32 tagacha raqam: 0–9, so'ng A–V.</summary>
    private const string DigitAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";
    private const string Subscripts = "₀₁₂₃₄₅₆₇₈₉";

    /// <summary>Yoyilma ko'rsatiladigan eng ko'p raqam soni — undan uzun son uchun mantiqsiz.</summary>
    private const int MaxExplainedDigits = 20;

    /// <summary>Bitta bo'limda ko'rsatiladigan qatorlar soni.</summary>
    private const int MaxExplanationLines = 14;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Eng ko'p ishlatiladigan to'rttasi — UI ularni ajratib ko'rsatadi.</summary>
    public static IReadOnlyList<int> PopularBases { get; } = [2, 8, 10, 16];

    public static bool IsSupportedBase(int radix) => SupportedSet.Contains(radix);

    /// <summary>Shu asosda raqamlar o'nlikda yozilib «:» bilan ajratiladimi.</summary>
    public static bool UsesDigitGroups(int radix) => radix > MaxSymbolBase;

    // =================================================================================
    //  Nomlash
    // =================================================================================

    private static readonly string[] UnitNames =
        ["", "bir", "ikki", "uch", "to'rt", "besh", "olti", "yetti", "sakkiz", "to'qqiz"];

    private static readonly string[] TensNames =
        ["", "o'n", "yigirma", "o'ttiz", "qirq", "ellik", "oltmish", "yetmish", "sakson", "to'qson"];

    /// <summary>Asosning o'zbekcha nomi: <c>2</c> → "ikkilik", <c>256</c> → "ikki yuz ellik oltilik".</summary>
    public static string DescribeBase(int radix)
    {
        EnsureBase(radix);
        return SpellOut(radix) + "lik";
    }

    /// <summary>Sonni o'zbekcha yozadi: <c>16</c> → "o'n olti", <c>128</c> → "bir yuz yigirma sakkiz".</summary>
    private static string SpellOut(int number)
    {
        // Bo'sh qismlar (o'nlik yoki birlik nol bo'lsa) tushib qoladi: 10 → "o'n", 200 → "ikki yuz".
        var parts = new List<string>(3);

        if (number / 100 > 0)
            parts.Add($"{UnitNames[number / 100]} yuz");

        if (number / 10 % 10 > 0)
            parts.Add(TensNames[number / 10 % 10]);

        if (number % 10 > 0)
            parts.Add(UnitNames[number % 10]);

        return string.Join(' ', parts);
    }

    /// <summary>Ro'yxatlar uchun to'liq yorliq: "16-lik — o'n oltilik".</summary>
    public static string LabelBase(int radix) => $"{radix.ToString(Inv)}-lik — {DescribeBase(radix)}";

    /// <summary>Shu asosda ishlatiladigan belgilar tavsifi: "0–9 va A–F".</summary>
    public static string DigitsOf(int radix)
    {
        EnsureBase(radix);

        if (radix == 2)
            return "0 va 1";

        if (radix <= 10)
            return $"0–{radix - 1}";

        if (radix <= MaxSymbolBase)
            return $"0–9 va A–{DigitAlphabet[radix - 1]}";

        return $"0 dan {(radix - 1).ToString(Inv)} gacha bo'lgan sonlar, «{DigitSeparator}» bilan ajratiladi";
    }

    /// <summary>Bitta raqamning yozuvi: 32-likkacha belgi, undan keyin o'nlikdagi son.</summary>
    private static string DigitSymbol(int digit, int radix)
        => UsesDigitGroups(radix) ? digit.ToString(Inv) : DigitAlphabet[digit].ToString();

    /// <summary>Raqamlarni bitta songa yig'adi: 32-likkacha yonma-yon, undan keyin «:» bilan.</summary>
    private static string JoinDigits(IEnumerable<string> digits, int radix)
        => UsesDigitGroups(radix) ? string.Join(DigitSeparator, digits) : string.Concat(digits);

    /// <summary>
    /// O'qishga qulaylik uchun qo'yilgan bezak belgisimi. Bunday belgilar qiymatga ta'sir
    /// qilmaydi va e'tiborsiz qoldiriladi — ekrandagi guruhlangan natijani ("1111 1111")
    /// nusxa olib qaytadan kiritish ishlashi uchun.
    /// </summary>
    private static bool IsSpacer(char symbol) => char.IsWhiteSpace(symbol) || symbol is '_' or '\'';

    // =================================================================================
    //  Tekshirish va o'tkazish
    // =================================================================================

    /// <summary>
    /// Kiritilgan son shu asosga mos keladimi. Xato bo'lsa tushunarli xabar, aks holda
    /// <c>null</c>. Bo'sh matn xato emas — foydalanuvchi hali yozishni boshlamagan.
    /// </summary>
    public static string? Validate(string? text, int fromBase)
    {
        EnsureBase(fromBase);
        TryParse(text, fromBase, out _, out var error);
        return error;
    }

    /// <summary>Sonni <paramref name="fromBase"/> dan <paramref name="toBase"/> ga o'tkazadi.</summary>
    public static NumberConversionResult Convert(string? text, int fromBase, int toBase, int fractionDigits)
    {
        EnsureBase(fromBase);
        EnsureBase(toBase);

        if (!TryParse(text, fromBase, out var parsed, out var error))
            return new NumberConversionResult(string.Empty, true, error);

        var (value, exact) = Render(parsed, toBase, ClampDigits(fractionDigits));
        return new NumberConversionResult(value, exact, null);
    }

    /// <summary>
    /// Uzun natijani o'qishga qulay qilib ajratadi: ikkilik, to'rtlik va o'n oltilikda 4 talab,
    /// sakkizlik va o'nlikda 3 talab. O'ttiz ikkilikda guruhlashning odatiy qoidasi yo'q,
    /// 64/128/256-likda esa raqamlar allaqachon «:» bilan ajratilgan — bu asoslarda son
    /// o'zgarishsiz qaytadi.
    /// </summary>
    /// <remarks>Bu faqat <b>ko'rsatish</b> uchun: nusxa olishda doim toza qiymat ishlatiladi.</remarks>
    public static string Group(string? value, int radix)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var size = radix switch
        {
            2 or 4 or 16 => 4,
            8 or 10 => 3,
            _ => 0
        };

        if (size == 0)
            return value;

        var sign = value[0] == '-' ? "-" : string.Empty;
        var body = value[sign.Length..];

        var dot = body.IndexOf('.');
        var integer = dot < 0 ? body : body[..dot];
        var fraction = dot < 0 ? string.Empty : body[(dot + 1)..];

        var builder = new StringBuilder(sign);
        builder.Append(GroupFromRight(integer, size));

        if (fraction.Length > 0)
        {
            builder.Append('.');
            builder.Append(GroupFromLeft(fraction, size));
        }

        return builder.ToString();
    }

    private static string GroupFromRight(string digits, int size)
    {
        if (digits.Length <= size)
            return digits;

        var builder = new StringBuilder(digits.Length + (digits.Length / size));
        var lead = digits.Length % size;

        if (lead > 0)
            builder.Append(digits[..lead]);

        for (var i = lead; i < digits.Length; i += size)
        {
            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(digits.AsSpan(i, size));
        }

        return builder.ToString();
    }

    private static string GroupFromLeft(string digits, int size)
    {
        if (digits.Length <= size)
            return digits;

        var builder = new StringBuilder(digits.Length + (digits.Length / size));

        for (var i = 0; i < digits.Length; i += size)
        {
            if (i > 0)
                builder.Append(' ');

            builder.Append(digits.AsSpan(i, Math.Min(size, digits.Length - i)));
        }

        return builder.ToString();
    }

    // =================================================================================
    //  Qadam-baqadam yechim
    // =================================================================================

    /// <summary>
    /// O'tkazishni bosqichma-bosqich tushuntiradi: manba → 10-lik (pozitsion yoyilma),
    /// so'ng 10-lik → maqsad (butun qism uchun ketma-ket bo'lish, kasr qism uchun ketma-ket
    /// ko'paytirish). Manba yoki maqsad allaqachon 10-lik bo'lsa, tegishli bosqich tushib qoladi.
    /// </summary>
    public static IReadOnlyList<ConversionExplanationSection> Explain(
        string? text,
        int fromBase,
        int toBase,
        int fractionDigits)
    {
        EnsureBase(fromBase);
        EnsureBase(toBase);

        if (!TryParse(text, fromBase, out var parsed, out _))
            return [];

        var digits = ClampDigits(fractionDigits);
        var source = Compose(parsed, fromBase);
        var (target, exact) = Render(parsed, toBase, digits);

        if (fromBase == toBase)
        {
            return
            [
                new ConversionExplanationSection(
                    "O'tkazish kerak emas",
                    [],
                    $"Manba ham, natija ham {fromBase}-lik sanoq sistemasida: {source}{Sub(fromBase)}")
            ];
        }

        var sections = new List<ConversionExplanationSection>(4);

        if (fromBase != 10)
            sections.Add(BuildExpansion(parsed, fromBase, digits));

        if (toBase != 10)
        {
            sections.Add(BuildDivision(parsed, toBase));

            if (!parsed.FractionNumerator.IsZero)
                sections.Add(BuildMultiplication(parsed, toBase, digits));
        }

        sections.Add(new ConversionExplanationSection(
            "Natija",
            [],
            $"{source}{Sub(fromBase)} {(exact ? "=" : "≈")} {target}{Sub(toBase)}"));

        return sections;
    }

    /// <summary>1-qadam: pozitsion yoyilma — har bir raqam o'z o'rni qiymatiga ko'paytiriladi.</summary>
    private static ConversionExplanationSection BuildExpansion(in ParsedNumber number, int fromBase, int fractionDigits)
    {
        const string title = "1-qadam — 10-lik sanoq sistemasiga o'tkazish";

        var decimalValue = FormatDecimal(number, fractionDigits, out var exact);
        var summary = $"{Compose(number, fromBase)}{Sub(fromBase)} {(exact ? "=" : "≈")} {decimalValue}{Sub(10)}";

        var total = number.IntegerDigits.Length + number.FractionDigits.Length;

        if (total > MaxExplainedDigits)
        {
            return new ConversionExplanationSection(
                title,
                [$"Sonda {total} ta raqam bor — yoyilma juda uzun bo'lgani uchun ko'rsatilmadi."],
                summary);
        }

        var lines = new List<string>(total);
        var radix = new BigInteger(fromBase);

        for (var i = 0; i < number.IntegerDigits.Length; i++)
        {
            var digit = number.IntegerDigits[i];
            var power = number.IntegerDigits.Length - 1 - i;
            var weight = BigInteger.Pow(radix, power);

            lines.Add(DescribeTerm(digit, fromBase, power, (weight * digit).ToString(Inv)));
        }

        for (var i = 0; i < number.FractionDigits.Length; i++)
        {
            var digit = number.FractionDigits[i];
            var power = -(i + 1);
            var weight = BigInteger.Pow(radix, i + 1);

            // digit / fromBase^(i+1) — o'nlikda o'zi ham cheksiz bo'lishi mumkin (masalan 1/3).
            var term = FormatRational(BigInteger.Zero, digit, weight, fractionDigits, out var termExact);

            lines.Add(DescribeTerm(digit, fromBase, power, (termExact ? string.Empty : "≈ ") + term));
        }

        return new ConversionExplanationSection(title, lines, summary);
    }

    private static string DescribeTerm(int digit, int fromBase, int power, string product)
    {
        var symbol = DigitSymbol(digit, fromBase);

        // Harf bilan yozilgan raqam uchun uning o'nlikdagi qiymati ham ko'rsatiladi. 64-likdan
        // boshlab raqamning o'zi allaqachon o'nlikda yozilgan — qavs ortiqcha bo'lardi.
        var head = symbol.Length == 1 && char.IsLetter(symbol[0])
            ? $"{symbol} ({digit.ToString(Inv)}) × {fromBase.ToString(Inv)}{Sup(power)}"
            : $"{symbol} × {fromBase.ToString(Inv)}{Sup(power)}";

        return $"{head} = {product}";
    }

    /// <summary>2-qadam: butun qismni yangi asosga ketma-ket bo'lish.</summary>
    private static ConversionExplanationSection BuildDivision(in ParsedNumber number, int toBase)
    {
        var title = $"2-qadam — butun qismni {toBase.ToString(Inv)} ga ketma-ket bo'lish";
        var radix = new BigInteger(toBase);
        var value = number.Integer;
        var lines = new List<string>();

        if (value.IsZero)
        {
            lines.Add($"0 ÷ {toBase.ToString(Inv)} = 0, qoldiq 0");
        }
        else
        {
            while (value > BigInteger.Zero)
            {
                var quotient = BigInteger.DivRem(value, radix, out var remainder);
                lines.Add($"{value.ToString(Inv)} ÷ {toBase.ToString(Inv)} = {quotient.ToString(Inv)}, qoldiq {DigitSymbol((int)remainder, toBase)}");
                value = quotient;
            }
        }

        return new ConversionExplanationSection(
            title,
            Shorten(lines),
            $"Qoldiqlarni oxiridan boshiga qarab o'qiymiz: {RenderInteger(number.Integer, toBase)}");
    }

    /// <summary>3-qadam: kasr qismini yangi asosga ketma-ket ko'paytirish.</summary>
    private static ConversionExplanationSection BuildMultiplication(in ParsedNumber number, int toBase, int fractionDigits)
    {
        var title = $"3-qadam — kasr qismini {toBase.ToString(Inv)} ga ketma-ket ko'paytirish";
        var radix = new BigInteger(toBase);
        var denominator = number.FractionDenominator;
        var value = number.FractionNumerator;

        var lines = new List<string>();
        var collected = new List<string>(fractionDigits);

        for (var i = 0; i < fractionDigits && !value.IsZero; i++)
        {
            var before = FormatRational(BigInteger.Zero, value, denominator, fractionDigits, out var beforeExact);

            value *= radix;
            var digit = BigInteger.Divide(value, denominator);
            value -= digit * denominator;

            var after = FormatRational(digit, value, denominator, fractionDigits, out var afterExact);
            var symbol = DigitSymbol((int)digit, toBase);

            collected.Add(symbol);
            lines.Add($"{Approx(beforeExact)}{before} × {toBase.ToString(Inv)} = {Approx(afterExact)}{after} → {symbol}");
        }

        var fraction = JoinDigits(collected, toBase);

        var summary = value.IsZero
            ? $"Butun qismlarni tartib bilan yozamiz: 0.{fraction}"
            : $"Kasr cheksiz davom etadi — {fractionDigits.ToString(Inv)} ta xonada kesildi: 0.{fraction}";

        return new ConversionExplanationSection(title, Shorten(lines), summary);
    }

    private static string Approx(bool exact) => exact ? string.Empty : "≈";

    /// <summary>Uzun ro'yxatni boshi va oxirini qoldirib qisqartiradi.</summary>
    private static IReadOnlyList<string> Shorten(List<string> lines)
    {
        if (lines.Count <= MaxExplanationLines)
            return lines;

        const int head = 8;
        const int tail = 3;

        var result = new List<string>(head + tail + 1);
        result.AddRange(lines.Take(head));
        result.Add($"… (yana {lines.Count - head - tail} ta qadam)");
        result.AddRange(lines.Skip(lines.Count - tail));

        return result;
    }

    // =================================================================================
    //  Tahlil (parsing)
    // =================================================================================

    /// <summary>Ajratib olingan son: raqamlari va aniq qiymati.</summary>
    private readonly struct ParsedNumber
    {
        public required bool IsNegative { get; init; }

        /// <summary>Butun qism raqamlari (boshidagi nollarsiz), chapdan o'ngga.</summary>
        public required int[] IntegerDigits { get; init; }

        /// <summary>Kasr qism raqamlari (oxiridagi nollarsiz), chapdan o'ngga.</summary>
        public required int[] FractionDigits { get; init; }

        public required BigInteger Integer { get; init; }

        /// <summary>Kasr qism = <see cref="FractionNumerator"/> / <see cref="FractionDenominator"/>.</summary>
        public required BigInteger FractionNumerator { get; init; }

        public required BigInteger FractionDenominator { get; init; }
    }

    private static bool TryParse(string? text, int fromBase, out ParsedNumber parsed, out string? error)
    {
        parsed = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        if (trimmed.Length > MaxInputLength)
        {
            error = $"Son juda uzun — {MaxInputLength.ToString(Inv)} tagacha belgi qabul qilinadi.";
            return false;
        }

        var negative = false;
        var start = 0;

        if (trimmed[0] is '-' or '−')
        {
            negative = true;
            start = 1;
        }
        else if (trimmed[0] == '+')
        {
            start = 1;
        }

        var integerDigits = new List<int>();
        var fractionDigits = new List<int>();

        // 64, 128 va 256-likda raqam bitta belgiga sig'maydi va yozuv butunlay boshqacha —
        // shuning uchun o'qish ham alohida yo'ldan boradi.
        if (UsesDigitGroups(fromBase))
        {
            return TryReadGroups(trimmed, start, fromBase, integerDigits, fractionDigits, out error)
                && Build(negative, integerDigits, fractionDigits, fromBase, out parsed, out error);
        }

        var separatorSeen = false;

        for (var i = start; i < trimmed.Length; i++)
        {
            var symbol = trimmed[i];

            if (symbol is '.' or ',')
            {
                if (separatorSeen)
                {
                    error = "Kasr belgisi faqat bitta bo'lishi mumkin.";
                    return false;
                }

                separatorSeen = true;
                continue;
            }

            // Guruhlash belgilari e'tiborsiz qoldiriladi — ko'rsatilgan natijani qaytarib
            // qo'yish (nusxa olib, yana kiritish) ishlashi uchun.
            if (IsSpacer(symbol))
                continue;

            var value = DigitAlphabet.IndexOf(char.ToUpperInvariant(symbol));

            if (value < 0 || value >= fromBase)
            {
                error = $"«{symbol}» — {fromBase.ToString(Inv)}-lik sanoq sistemasining raqami emas. "
                    + $"Ruxsat etilgan belgilar: {DigitsOf(fromBase)}.";
                return false;
            }

            (separatorSeen ? fractionDigits : integerDigits).Add(value);
        }

        return Build(negative, integerDigits, fractionDigits, fromBase, out parsed, out error);
    }

    /// <summary>
    /// 64, 128 va 256-lik uchun o'qish: har bir raqam o'nlikda yozilgan va «:» bilan
    /// ajratilgan — <c>188:97:78</c>. Bo'shliq ham ajratkich bo'lib ishlaydi, ya'ni ekrandagi
    /// natijani nusxa olib qaytadan kiritish mumkin. Ketma-ket kelgan ajratkichlar bittadek
    /// qaraladi va oxiridagi ajratkich xato emas: foydalanuvchi hali yozayotgan bo'lishi
    /// mumkin, har bosishda qizil xabar chiqarish xalaqit berardi.
    /// </summary>
    private static bool TryReadGroups(
        string text,
        int start,
        int fromBase,
        List<int> integerDigits,
        List<int> fractionDigits,
        out string? error)
    {
        error = null;

        var integerGroups = new List<string>();
        var fractionGroups = new List<string>();
        var target = integerGroups;
        var current = new StringBuilder(3);
        var separatorSeen = false;

        for (var i = start; i < text.Length; i++)
        {
            var symbol = text[i];

            if (symbol is '.' or ',')
            {
                if (separatorSeen)
                {
                    error = "Kasr belgisi faqat bitta bo'lishi mumkin.";
                    return false;
                }

                separatorSeen = true;
                Flush(current, target);
                target = fractionGroups;
                continue;
            }

            if (symbol == DigitSeparator || IsSpacer(symbol))
            {
                Flush(current, target);
                continue;
            }

            if (symbol is >= '0' and <= '9')
            {
                current.Append(symbol);
                continue;
            }

            error = $"«{symbol}» — {fromBase.ToString(Inv)}-lik sanoq sistemasida ishlatilmaydi. "
                + $"Ruxsat etilgan belgilar: {DigitsOf(fromBase)}.";
            return false;
        }

        Flush(current, target);

        return TryConvertGroups(integerGroups, fromBase, integerDigits, out error)
            && TryConvertGroups(fractionGroups, fromBase, fractionDigits, out error);

        static void Flush(StringBuilder current, List<string> groups)
        {
            if (current.Length == 0)
                return;

            groups.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>Guruhlarni raqamga aylantiradi; biror guruh asosga sig'masa — tushunarli xabar.</summary>
    private static bool TryConvertGroups(List<string> groups, int fromBase, List<int> digits, out string? error)
    {
        error = null;

        foreach (var group in groups)
        {
            // "007" — bu 7; "000" esa haqiqiy nol raqami, shuning uchun bo'sh qolgani ham 0.
            var body = group.TrimStart('0');

            if (body.Length == 0)
            {
                digits.Add(0);
                continue;
            }

            // Eng katta raqam 255, ya'ni uch xonadan uzuni albatta xato — bunday satr
            // int.Parse ga ham yetib bormasligi kerak.
            if (body.Length > 3 || !int.TryParse(body, NumberStyles.None, Inv, out var value) || value >= fromBase)
            {
                error = $"«{group}» — {fromBase.ToString(Inv)}-lik sanoq sistemasining raqami emas: "
                    + $"har bir raqam 0 dan {(fromBase - 1).ToString(Inv)} gacha bo'lishi va "
                    + $"«{DigitSeparator}» bilan ajratilishi kerak.";
                return false;
            }

            digits.Add(value);
        }

        return true;
    }

    /// <summary>
    /// O'qib olingan raqamlardan aniq qiymat yasaydi: butun qism <see cref="BigInteger"/>,
    /// kasr qism esa surat/maxraj juftligi. Ikkala yozuv uchun ham bir xil ishlaydi.
    /// </summary>
    private static bool Build(
        bool negative,
        List<int> integerDigits,
        List<int> fractionDigits,
        int fromBase,
        out ParsedNumber parsed,
        out string? error)
    {
        parsed = default;
        error = null;

        if (integerDigits.Count == 0 && fractionDigits.Count == 0)
        {
            error = "Son kiritilmagan.";
            return false;
        }

        // Boshidagi va oxiridagi nollar qiymatga ta'sir qilmaydi, lekin yoyilmani chalkashtiradi.
        while (integerDigits.Count > 0 && integerDigits[0] == 0)
            integerDigits.RemoveAt(0);

        while (fractionDigits.Count > 0 && fractionDigits[^1] == 0)
            fractionDigits.RemoveAt(fractionDigits.Count - 1);

        var radix = new BigInteger(fromBase);

        var integer = BigInteger.Zero;
        foreach (var digit in integerDigits)
            integer = (integer * radix) + digit;

        var numerator = BigInteger.Zero;
        var denominator = BigInteger.One;

        foreach (var digit in fractionDigits)
        {
            numerator = (numerator * radix) + digit;
            denominator *= radix;
        }

        parsed = new ParsedNumber
        {
            // "-0" degan son yo'q.
            IsNegative = negative && !(integer.IsZero && numerator.IsZero),
            IntegerDigits = [.. integerDigits],
            FractionDigits = [.. fractionDigits],
            Integer = integer,
            FractionNumerator = numerator,
            FractionDenominator = denominator
        };

        return true;
    }

    // =================================================================================
    //  Yozish (rendering)
    // =================================================================================

    private static (string Value, bool IsExact) Render(in ParsedNumber number, int toBase, int fractionDigits)
    {
        var builder = new StringBuilder();

        if (number.IsNegative)
            builder.Append('-');

        builder.Append(RenderInteger(number.Integer, toBase));

        if (number.FractionNumerator.IsZero)
            return (builder.ToString(), true);

        var fraction = RenderFraction(
            number.FractionNumerator, number.FractionDenominator, toBase, fractionDigits, out var exact);

        if (fraction.Length > 0)
        {
            builder.Append('.');
            builder.Append(fraction);
        }

        return (builder.ToString(), exact);
    }

    private static string RenderInteger(BigInteger value, int toBase)
    {
        if (value.IsZero)
            return "0";

        var radix = new BigInteger(toBase);
        var digits = new Stack<string>();
        var rest = BigInteger.Abs(value);

        while (rest > BigInteger.Zero)
        {
            rest = BigInteger.DivRem(rest, radix, out var remainder);
            digits.Push(DigitSymbol((int)remainder, toBase));
        }

        return JoinDigits(digits, toBase);
    }

    /// <summary>
    /// Kasr qismni yangi asosga o'tkazadi: qiymat asosga ko'paytiriladi, chiqqan butun qism
    /// navbatdagi raqam bo'ladi. Ratsional arifmetika — hech qanday yaxlitlash xatosi yo'q.
    /// </summary>
    private static string RenderFraction(
        BigInteger numerator,
        BigInteger denominator,
        int toBase,
        int maxDigits,
        out bool exact)
    {
        var radix = new BigInteger(toBase);
        var digits = new List<string>(maxDigits);
        var value = numerator;

        for (var i = 0; i < maxDigits && !value.IsZero; i++)
        {
            value *= radix;
            var digit = BigInteger.Divide(value, denominator);
            digits.Add(DigitSymbol((int)digit, toBase));
            value -= digit * denominator;
        }

        exact = value.IsZero;
        return JoinDigits(digits, toBase);
    }

    /// <summary>Sonning o'nlikdagi ko'rinishi — tushuntirish matnlari uchun.</summary>
    private static string FormatDecimal(in ParsedNumber number, int fractionDigits, out bool exact)
    {
        var text = FormatRational(number.Integer, number.FractionNumerator, number.FractionDenominator, fractionDigits, out exact);
        return number.IsNegative ? "-" + text : text;
    }

    private static string FormatRational(
        BigInteger integer,
        BigInteger numerator,
        BigInteger denominator,
        int fractionDigits,
        out bool exact)
    {
        var builder = new StringBuilder(integer.ToString(Inv));

        if (numerator.IsZero)
        {
            exact = true;
            return builder.ToString();
        }

        var fraction = RenderFraction(numerator, denominator, 10, fractionDigits, out exact);

        if (fraction.Length > 0)
        {
            builder.Append('.');
            builder.Append(fraction);
        }

        return builder.ToString();
    }

    /// <summary>Tahlildan keyingi tozalangan manba: ortiqcha nollarsiz va bitta nuqta bilan.</summary>
    private static string Compose(in ParsedNumber number, int fromBase)
    {
        var builder = new StringBuilder();

        if (number.IsNegative)
            builder.Append('-');

        builder.Append(number.IntegerDigits.Length == 0
            ? "0"
            : JoinDigits(number.IntegerDigits.Select(digit => DigitSymbol(digit, fromBase)), fromBase));

        if (number.FractionDigits.Length > 0)
        {
            builder.Append('.');
            builder.Append(JoinDigits(number.FractionDigits.Select(digit => DigitSymbol(digit, fromBase)), fromBase));
        }

        return builder.ToString();
    }

    // =================================================================================
    //  Kichik yordamchilar
    // =================================================================================

    /// <summary>Yuqori indeks: <c>16¹</c>, <c>16⁻¹</c>.</summary>
    private static string Sup(int power)
    {
        var builder = new StringBuilder();

        if (power < 0)
            builder.Append('⁻');

        foreach (var digit in Math.Abs(power).ToString(Inv))
            builder.Append(Superscripts[digit - '0']);

        return builder.ToString();
    }

    /// <summary>Quyi indeks: <c>1A₁₆</c>.</summary>
    private static string Sub(int radix)
    {
        var builder = new StringBuilder();

        foreach (var digit in radix.ToString(Inv))
            builder.Append(Subscripts[digit - '0']);

        return builder.ToString();
    }

    private static int ClampDigits(int fractionDigits)
        => Math.Clamp(fractionDigits, MinFractionDigits, MaxFractionDigits);

    private static void EnsureBase(int radix)
    {
        if (!IsSupportedBase(radix))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radix), radix,
                $"Sanoq sistemasi asosi quyidagilardan biri bo'lishi kerak: {string.Join(", ", SupportedBases)}.");
        }
    }
}
