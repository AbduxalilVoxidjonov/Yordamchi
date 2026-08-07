namespace Yordamchi.Models;

/// <summary>O'girish yo'nalishi.</summary>
public enum TransliterationDirection
{
    /// <summary>Kirilldan lotinga: <c>Ўзбекистон</c> → <c>O'zbekiston</c>.</summary>
    CyrillicToLatin,

    /// <summary>Lotindan kirillga: <c>O'zbekiston</c> → <c>Ўзбекистон</c>.</summary>
    LatinToCyrillic
}

/// <summary>Lotin yozuvida <c>o'</c>, <c>g'</c> va tutuq belgisi qanday yoziladi.</summary>
public enum ApostropheStyle
{
    /// <summary>Oddiy klaviatura apostrofi (<c>'</c>) — hamma joyda bir xil ko'chiriladi.</summary>
    Ascii,

    /// <summary>
    /// Rasmiy belgilar: <c>oʻ</c>/<c>gʻ</c> uchun U+02BB, tutuq belgisi uchun U+02BC.
    /// Nashrga tayyorlanadigan matn uchun to'g'ri, lekin ba'zi eski dasturlar buni qo'llab-quvvatlamaydi.
    /// </summary>
    Typographic
}

/// <summary>
/// O'girish sozlamalari. <see cref="AutoDetectDirection"/> yoqilganda <see cref="Direction"/>
/// boshlang'ich taxmin bo'lib qoladi: haqiqiy yo'nalish matnning o'zidan aniqlanadi
/// (<c>UzbekTransliterator.Resolve</c>).
/// </summary>
public sealed record TransliterationOptions
{
    public TransliterationDirection Direction { get; init; } = TransliterationDirection.CyrillicToLatin;

    public ApostropheStyle Apostrophe { get; init; } = ApostropheStyle.Ascii;

    /// <summary>Yo'nalish matnning o'zidan aniqlansinmi.</summary>
    public bool AutoDetectDirection { get; init; }
}

/// <summary>Bitta fayl o'girilgandan keyingi natija.</summary>
/// <param name="SourcePath">Manba fayl.</param>
/// <param name="OutputPath">Yozilgan natija fayli.</param>
/// <param name="Direction">Amalda qo'llangan yo'nalish (avtomatik aniqlangan bo'lishi mumkin).</param>
/// <param name="CharacterCount">O'girilgan belgilar soni — natija haqiqatan bo'sh emasligini ko'rsatadi.</param>
public sealed record TransliterationFileResult(
    string SourcePath,
    string OutputPath,
    TransliterationDirection Direction,
    int CharacterCount);
