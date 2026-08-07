using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// "Sanoq sistemasi" bo'limining shartnomasi: sonni 2 dan 32 gacha bo'lgan asoslar orasida
/// o'tkazish va yechimni bosqichma-bosqich tushuntirish.
/// <para>
/// <see cref="IPdfEngineService"/> fasadiga <b>kirmaydi</b>: bu yerda na fayl bor, na PDF —
/// kirish ham, chiqish ham oddiy satr. Shu sababli barcha metodlar <b>sinxron</b>: hisob
/// mikrosoniyalarda tugaydi va natija har bosishda darhol ko'rinishi kerak; <c>Task</c>
/// qaytarish bu yerda faqat keraksiz kontekst almashinuvi bo'lardi.
/// </para>
/// </summary>
public interface INumberSystemService
{
    /// <summary>Eng kichik asos (2).</summary>
    int MinBase { get; }

    /// <summary>Eng katta asos (32).</summary>
    int MaxBase { get; }

    /// <summary>Barcha qo'llab-quvvatlanadigan asoslar: 2, 3, …, 32.</summary>
    IReadOnlyList<int> SupportedBases { get; }

    /// <summary>Eng ko'p ishlatiladigan asoslar (2, 8, 10, 16) — UI ularni ajratib ko'rsatadi.</summary>
    IReadOnlyList<int> PopularBases { get; }

    /// <summary>Asosning o'zbekcha nomi: <c>16</c> → "o'n oltilik".</summary>
    string DescribeBase(int radix);

    /// <summary>Ro'yxatlar uchun to'liq yorliq: <c>16</c> → "16-lik — o'n oltilik".</summary>
    string LabelBase(int radix);

    /// <summary>Shu asosda ishlatiladigan belgilar tavsifi: <c>16</c> → "0–9 va A–F".</summary>
    string DigitsOf(int radix);

    /// <summary>Kiritilgan son shu asosga mos keladimi; xato bo'lsa tushunarli matn.</summary>
    /// <remarks>Bo'sh matn xato emas — foydalanuvchi hali yozishni boshlamagan.</remarks>
    string? Validate(string? text, int fromBase);

    /// <summary>Sonni bir asosdan boshqasiga o'tkazadi.</summary>
    NumberConversionResult Convert(string? text, int fromBase, int toBase, int fractionDigits);

    /// <summary>O'tkazishni bosqichma-bosqich tushuntiradi; son yaroqsiz bo'lsa — bo'sh ro'yxat.</summary>
    IReadOnlyList<ConversionExplanationSection> Explain(string? text, int fromBase, int toBase, int fractionDigits);

    /// <summary>Uzun natijani o'qishga qulay qilib ajratadi (faqat ko'rsatish uchun).</summary>
    string Group(string? value, int radix);
}
