namespace Yordamchi.Models;

/// <summary>
/// Sonni bir sanoq sistemasidan boshqasiga o'tkazish natijasi.
/// </summary>
/// <param name="Value">Natija; kiritish bo'sh yoki xato bo'lsa — bo'sh satr.</param>
/// <param name="IsExact">
/// Natija aniqmi. Kasr qismi yangi asosda cheksiz davom etsa (masalan <c>0.1₁₀</c> ikkilikda)
/// u belgilangan xonada kesiladi va bu bayroq <c>false</c> bo'ladi.
/// </param>
/// <param name="Error">Kiritilgan son sanoq sistemasiga mos kelmasa — tushunarli xabar.</param>
public sealed record NumberConversionResult(string Value, bool IsExact, string? Error)
{
    public bool HasValue => Value.Length > 0;
}

/// <summary>
/// Qadam-baqadam yechimning bitta bo'limi: sarlavha, hisob qatorlari va xulosa.
/// </summary>
/// <param name="Title">Bo'lim sarlavhasi, masalan "1-qadam — 10-lik sanoq sistemasiga o'tkazish".</param>
/// <param name="Lines">Hisob qatorlari; bo'sh bo'lishi ham mumkin.</param>
/// <param name="Summary">Bo'lim xulosasi — nima kelib chiqqani.</param>
public sealed record ConversionExplanationSection(
    string Title,
    IReadOnlyList<string> Lines,
    string? Summary)
{
    public bool HasLines => Lines.Count > 0;

    public bool HasSummary => !string.IsNullOrEmpty(Summary);
}
