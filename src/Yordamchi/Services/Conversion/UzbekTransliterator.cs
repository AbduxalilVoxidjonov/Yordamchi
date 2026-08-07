using System.Text;
using Yordamchi.Models;

namespace Yordamchi.Services.Conversion;

/// <summary>
/// O'zbek kirill va lotin alifbolari orasidagi o'girish qoidalari. Sof mantiq: fayl ham,
/// UI ham bilmaydi, shuning uchun to'liq sinovdan o'tkaziladi.
/// <para>
/// Ko'p harf bir-biriga bir xil tushmaydi, ular atrofdagi harflarga qarab hal qilinadi:
/// <c>е</c> so'z boshida <c>ye</c>, so'z ichida <c>e</c>; <c>ц</c> unlidan keyin <c>ts</c>,
/// aks holda <c>s</c>; <c>ъ</c> esa <c>е ё ю я</c> dan oldin butunlay tushib qoladi
/// (<c>объект</c> → <c>obyekt</c>).
/// </para>
/// <para>
/// <b>Bilib turib qilingan chekinish.</b> Lotindan kirillga o'girishda <c>-siya</c> bilan
/// tugaydigan o'zlashma so'zlarni qoida bilan ajratib bo'lmaydi: <c>funksiya</c> → <c>функция</c>,
/// lekin <c>pensiya</c> → <c>пенсия</c>. Ikkalasi ham bir xil ko'rinadi, shuning uchun bu yerda
/// faqat <c>ts</c> qatnashgan holat (<c>revolyutsiya</c> → <c>революция</c>) o'giriladi, qolgani
/// <c>с</c> bo'lib qolaveradi — noto'g'ri taxmin qilgandan ko'ra tegmagan ma'qul.
/// </para>
/// </summary>
public static class UzbekTransliterator
{
    /// <summary>Bitta manba bo'lagi va uning natijasi. Bo'lak uzunligi belgilarda o'lchanadi.</summary>
    /// <param name="sourceIndex">Manbadagi boshlanish o'rni.</param>
    /// <param name="sourceLength">Manbadan yeyilgan belgilar soni.</param>
    /// <param name="output">Natija; bo'sh bo'lishi ham mumkin (masalan <c>ь</c> tushib qoladi).</param>
    public delegate void SegmentWriter(int sourceIndex, int sourceLength, ReadOnlySpan<char> output);

    private const char TurnedComma = 'ʻ';        // ʻ — oʻ va gʻ uchun rasmiy belgi
    private const char ModifierApostrophe = 'ʼ'; // ʼ — tutuq belgisi uchun rasmiy belgi

    /// <summary>Foydalanuvchi kiritishi mumkin bo'lgan barcha apostrof ko'rinishlari.</summary>
    private static readonly char[] Apostrophes =
    [
        '\'', '`', '´', '‘', '’', 'ʹ', 'ʻ', 'ʼ', '′'
    ];

    private const string CyrillicVowels = "аеёиоуўэюяы";

    // Bir harf uchun uchta ko'rinish oldindan tayyorlanadi: ish vaqtida satr yasalmaydi.
    private readonly record struct Mapping(string Lower, string Title, string Upper)
    {
        public static Mapping Of(string lower) => new(
            lower,
            lower.Length == 0 ? string.Empty : char.ToUpperInvariant(lower[0]) + lower[1..],
            lower.ToUpperInvariant());

        public string For(bool isUpperSource, bool isUpperRun) =>
            !isUpperSource ? Lower : isUpperRun ? Upper : Title;
    }

    private static readonly Mapping Nothing = Mapping.Of(string.Empty);

    private static readonly Dictionary<char, Mapping> CyrillicAscii = BuildCyrillicTable('\'');
    private static readonly Dictionary<char, Mapping> CyrillicTypographic = BuildCyrillicTable(TurnedComma);

    private static readonly Mapping Ye = Mapping.Of("ye");
    private static readonly Mapping E = Mapping.Of("e");
    private static readonly Mapping Ts = Mapping.Of("ts");
    private static readonly Mapping S = Mapping.Of("s");
    private static readonly Mapping TutuqAscii = Mapping.Of("'");
    private static readonly Mapping TutuqTypographic = Mapping.Of(ModifierApostrophe.ToString());

    private static Dictionary<char, Mapping> BuildCyrillicTable(char letterApostrophe)
    {
        // ъ, ь, е va ц shu jadvalda yo'q — ular atrofdagi harflarga qarab hal qilinadi.
        var pairs = new (char Cyrillic, string Latin)[]
        {
            ('а', "a"), ('б', "b"), ('в', "v"), ('г', "g"), ('д', "d"),
            ('ё', "yo"), ('ж', "j"), ('з', "z"), ('и', "i"), ('й', "y"),
            ('к', "k"), ('л', "l"), ('м', "m"), ('н', "n"), ('о', "o"),
            ('п', "p"), ('р', "r"), ('с', "s"), ('т', "t"), ('у', "u"),
            ('ф', "f"), ('х', "x"), ('ч', "ch"), ('ш', "sh"), ('э', "e"),
            ('ю', "yu"), ('я', "ya"), ('қ', "q"), ('ҳ', "h"),

            // Rus alifbosidan kirib qolgan harflar — matn aralash bo'lsa ham natija o'qiladi.
            ('ы', "i"), ('щ', "shch")
        };

        var table = new Dictionary<char, Mapping>(pairs.Length + 2);

        foreach (var (cyrillic, latin) in pairs)
            table[cyrillic] = Mapping.Of(latin);

        table['ў'] = Mapping.Of("o" + letterApostrophe);
        table['ғ'] = Mapping.Of("g" + letterApostrophe);

        // Tutuq belgisi (ъ) bu jadvalda yo'q: u atrofdagi harflarga bog'liq, shuning uchun
        // TutuqAscii / TutuqTypographic alohida saqlanadi.
        return table;
    }

    // =================================================================================
    //  Ommaviy kirish nuqtalari
    // =================================================================================

    /// <summary>Matnni butunligicha o'giradi.</summary>
    public static string Convert(string? text, TransliterationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Kirill harflari lotinda ikki belgiga cho'zilishi mumkin — joyni oldindan olamiz.
        var builder = new StringBuilder(text.Length + (text.Length / 3));
        Convert(text, options, (_, _, piece) => builder.Append(piece));
        return builder.ToString();
    }

    /// <summary>
    /// Matnni bo'lak-bo'lak o'giradi va har bir bo'lak uchun <paramref name="write"/> ni chaqiradi.
    /// <para>
    /// Bu Word hujjati uchun kerak: bitta abzas matni bir nechta <c>w:t</c> tuguniga bo'linib
    /// ketgan bo'lishi mumkin (masalan <c>Ўз</c> + <c>бекистон</c>). Tugunlarni alohida o'girish
    /// so'z boshini ham, <c>o'</c> kabi juft belgilarni ham buzardi; shuning uchun abzas
    /// butunligicha o'giriladi, natija esa manba o'rniga qarab tugunlarga qaytariladi.
    /// </para>
    /// </summary>
    public static void Convert(string? text, TransliterationOptions options, SegmentWriter write)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(write);

        if (string.IsNullOrEmpty(text))
            return;

        if (options.Direction == TransliterationDirection.CyrillicToLatin)
            CyrillicToLatin(text, options, write);
        else
            LatinToCyrillic(text, write);
    }

    /// <summary>
    /// Matn qaysi alifboda yozilganini aniqlaydi va mos yo'nalishni qaytaradi.
    /// Harf umuman topilmasa (raqam, tinish belgisi) — <c>null</c>.
    /// </summary>
    public static TransliterationDirection? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var cyrillic = 0;
        var latin = 0;

        foreach (var character in text)
        {
            if (IsCyrillic(character))
                cyrillic++;
            else if (IsLatin(character))
                latin++;
        }

        if (cyrillic == 0 && latin == 0)
            return null;

        return cyrillic > latin
            ? TransliterationDirection.CyrillicToLatin
            : TransliterationDirection.LatinToCyrillic;
    }

    /// <summary>
    /// Avtomatik aniqlash yoqilgan bo'lsa yo'nalishni namunadan hal qiladi va aniq sozlama qaytaradi.
    /// Aniqlab bo'lmasa mavjud <see cref="TransliterationOptions.Direction"/> qoladi.
    /// </summary>
    public static TransliterationOptions Resolve(TransliterationOptions options, string? sample)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.AutoDetectDirection)
            return options;

        var detected = Detect(sample);

        return detected is null
            ? options with { AutoDetectDirection = false }
            : options with { Direction = detected.Value, AutoDetectDirection = false };
    }

    // =================================================================================
    //  Kirill → Lotin
    // =================================================================================

    private static void CyrillicToLatin(string text, TransliterationOptions options, SegmentWriter write)
    {
        var table = options.Apostrophe == ApostropheStyle.Typographic ? CyrillicTypographic : CyrillicAscii;
        var tutuq = options.Apostrophe == ApostropheStyle.Typographic ? TutuqTypographic : TutuqAscii;

        for (var index = 0; index < text.Length;)
        {
            if (TryTakeVerbatimToken(text, index, out var tokenLength))
            {
                write(index, tokenLength, text.AsSpan(index, tokenLength));
                index += tokenLength;
                continue;
            }

            var character = text[index];
            var lower = char.ToLowerInvariant(character);

            Mapping mapping;

            switch (lower)
            {
                case 'е':
                    // So'z boshida, unlidan keyin va ayirish belgisidan keyin — "ye".
                    mapping = IsWordStart(text, index)
                        || IsCyrillicVowel(PreviousLetter(text, index))
                        || PreviousLetter(text, index) is 'ъ' or 'ь'
                        ? Ye
                        : E;
                    break;

                case 'ц':
                    // "революция" → revolyutsiya, lekin "лекция" → leksiya.
                    mapping = IsCyrillicVowel(PreviousLetter(text, index)) ? Ts : S;
                    break;

                case 'ъ':
                    // "объект" → obyekt: keyingi harfning o'zi "y" ni olib keladi.
                    mapping = NextLetter(text, index) is 'е' or 'ё' or 'ю' or 'я' || !char.IsLetter(PreviousLetter(text, index))
                        ? Nothing
                        : tutuq;
                    break;

                case 'ь':
                    mapping = Nothing;
                    break;

                default:
                    if (!table.TryGetValue(lower, out mapping))
                    {
                        // Lotin harfi, raqam, tinish belgisi — tegmaymiz.
                        write(index, 1, text.AsSpan(index, 1));
                        index++;
                        continue;
                    }

                    break;
            }

            var isUpper = char.IsUpper(character);
            write(index, 1, mapping.For(isUpper, isUpper && IsInsideUpperRun(text, index)));
            index++;
        }
    }

    // =================================================================================
    //  Lotin → Kirill
    // =================================================================================

    private static void LatinToCyrillic(string text, SegmentWriter write)
    {
        Span<char> single = stackalloc char[1];

        for (var index = 0; index < text.Length;)
        {
            if (TryTakeVerbatimToken(text, index, out var tokenLength))
            {
                write(index, tokenLength, text.AsSpan(index, tokenLength));
                index += tokenLength;
                continue;
            }

            if (!TryMapLatin(text, index, out var length, out var lower))
            {
                write(index, 1, text.AsSpan(index, 1));
                index++;
                continue;
            }

            single[0] = char.IsUpper(text[index]) ? char.ToUpperInvariant(lower) : lower;
            write(index, length, single);
            index += length;
        }
    }

    /// <summary>
    /// <paramref name="index"/> dagi lotin bo'lagini bitta kirill harfiga o'giradi.
    /// Har bir qoida bittagina harf qaytaradi, shuning uchun katta-kichiklik manbaning
    /// birinchi belgisidan olinadi: <c>Sh</c> ham, <c>SH</c> ham — <c>Ш</c>.
    /// </summary>
    private static bool TryMapLatin(string text, int index, out int length, out char lower)
    {
        length = 1;
        var current = char.ToLowerInvariant(text[index]);
        var next = LowerAt(text, index + 1);

        switch (current)
        {
            case 'y':
                // "yo'l" — bu "ё" emas: "o'" keyingi qadamda "ў" bo'lib chiqadi, ya'ni "йўл".
                if (next is 'a' or 'o' or 'u' or 'e' && !(next is 'o' && IsApostrophe(CharAt(text, index + 2))))
                {
                    length = 2;
                    lower = next switch { 'a' => 'я', 'o' => 'ё', 'u' => 'ю', _ => 'е' };
                    return true;
                }

                lower = 'й';
                return true;

            case 'o' when IsApostrophe(CharAt(text, index + 1)):
                length = 2;
                lower = 'ў';
                return true;

            case 'g' when IsApostrophe(CharAt(text, index + 1)):
                length = 2;
                lower = 'ғ';
                return true;

            case 's' when next == 'h':
                length = 2;
                lower = 'ш';
                return true;

            case 'c' when next == 'h':
                length = 2;
                lower = 'ч';
                return true;

            case 't' when next == 's' && IsTse(text, index):
                length = 2;
                lower = 'ц';
                return true;

            case 'e':
                lower = IsWordStart(text, index) ? 'э' : 'е';
                return true;

            case 'a': lower = 'а'; return true;
            case 'b': lower = 'б'; return true;
            case 'c': lower = 'с'; return true;
            case 'd': lower = 'д'; return true;
            case 'f': lower = 'ф'; return true;
            case 'g': lower = 'г'; return true;
            case 'h': lower = 'ҳ'; return true;
            case 'i': lower = 'и'; return true;
            case 'j': lower = 'ж'; return true;
            case 'k': lower = 'к'; return true;
            case 'l': lower = 'л'; return true;
            case 'm': lower = 'м'; return true;
            case 'n': lower = 'н'; return true;
            case 'o': lower = 'о'; return true;
            case 'p': lower = 'п'; return true;
            case 'q': lower = 'қ'; return true;
            case 'r': lower = 'р'; return true;
            case 's': lower = 'с'; return true;
            case 't': lower = 'т'; return true;
            case 'u': lower = 'у'; return true;
            case 'v': lower = 'в'; return true;
            case 'w': lower = 'в'; return true;
            case 'x': lower = 'х'; return true;
            case 'z': lower = 'з'; return true;
        }

        // Harfdan keyingi apostrof — tutuq belgisi: "ma'no" → "маъно".
        // So'z boshidagi apostrof esa qo'shtirnoq bo'lishi mumkin, unga tegmaymiz.
        if (IsApostrophe(text[index]) && index > 0 && char.IsLetter(text[index - 1]))
        {
            lower = 'ъ';
            return true;
        }

        lower = '\0';
        return false;
    }

    /// <summary>
    /// <c>ts</c> aynan <c>ц</c> mi. Faqat ishonchli holatlar: so'z boshi (<c>tsex</c>) va
    /// o'zlashma qo'shimchalar (<c>-tsiya</c>, <c>-tsion</c>). Aks holda <c>ketsa</c> kabi
    /// o'zbekcha so'zlar <c>кеца</c> bo'lib buzilardi.
    /// </summary>
    private static bool IsTse(string text, int index) =>
        IsWordStart(text, index)
        || HasPrefix(text, index, "tsiya")
        || HasPrefix(text, index, "tsion");

    private static bool HasPrefix(string text, int index, string prefix) =>
        index + prefix.Length <= text.Length
        && text.AsSpan(index, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase);

    // =================================================================================
    //  Kontekst yordamchilari
    // =================================================================================

    /// <summary>
    /// Havola va elektron pochta manzillari o'girilmaydi: <c>www.google.com</c> lotinda
    /// qolishi kerak, aks holda havola ishlamay qoladi.
    /// </summary>
    private static bool TryTakeVerbatimToken(string text, int index, out int length)
    {
        length = 0;

        if (!IsWordStart(text, index) || char.IsWhiteSpace(text[index]))
            return false;

        var end = index;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
            end++;

        var token = text.AsSpan(index, end - index);

        var looksLikeLink =
            token.Contains("://", StringComparison.Ordinal)
            || token.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || (token.Contains('@') && token.Contains('.'));

        if (!looksLikeLink)
            return false;

        length = end - index;
        return true;
    }

    /// <summary>Apostrof so'zning bo'lagi, shuning uchun u so'zni tugatmaydi.</summary>
    private static bool IsWordStart(string text, int index)
    {
        if (index == 0)
            return true;

        var previous = text[index - 1];
        return !char.IsLetter(previous) && !char.IsDigit(previous) && !IsApostrophe(previous);
    }

    /// <summary>
    /// Bosh harf butun so'z katta yozilganidanmi. Yonidagi harfga qaraladi: keyingisi kichik
    /// bo'lsa — faqat bosh harf (<c>Ша</c> → <c>Sha</c>), katta bo'lsa — hammasi katta
    /// (<c>ША</c> → <c>SHA</c>). Yolg'iz harf bosh harf deb qabul qilinadi.
    /// </summary>
    private static bool IsInsideUpperRun(string text, int index)
    {
        if (index + 1 < text.Length && char.IsLetter(text[index + 1]))
            return char.IsUpper(text[index + 1]);

        if (index > 0 && char.IsLetter(text[index - 1]))
            return char.IsUpper(text[index - 1]);

        return false;
    }

    private static char PreviousLetter(string text, int index) =>
        index > 0 ? char.ToLowerInvariant(text[index - 1]) : '\0';

    private static char NextLetter(string text, int index) =>
        index + 1 < text.Length ? char.ToLowerInvariant(text[index + 1]) : '\0';

    private static char LowerAt(string text, int index) =>
        index < text.Length ? char.ToLowerInvariant(text[index]) : '\0';

    private static char CharAt(string text, int index) =>
        index < text.Length ? text[index] : '\0';

    private static bool IsApostrophe(char character) => Array.IndexOf(Apostrophes, character) >= 0;

    private static bool IsCyrillicVowel(char lower) => CyrillicVowels.Contains(lower);

    private static bool IsCyrillic(char character) => character is >= 'Ѐ' and <= 'ӿ';

    private static bool IsLatin(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
