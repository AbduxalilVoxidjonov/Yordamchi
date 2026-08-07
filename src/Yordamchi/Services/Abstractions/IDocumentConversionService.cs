using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Hujjat formatlari orasidagi konvertatsiya: PDF ↔ Word, PDF → Excel / PowerPoint / rasm.
/// <para>
/// Asosiy tamoyil — matn rasmga aylanmaydi. PDF dan matn <c>PdfPig</c> orqali shrift, o'lcham va
/// koordinatalari bilan o'qiladi, kerak bo'lganda OCR bilan to'ldiriladi, so'ng OpenXML orqali
/// haqiqiy abzas va jadval sifatida yoziladi. Natijadagi .docx to'liq tahrirlanadi.
/// </para>
/// </summary>
public interface IDocumentConversionService
{
    /// <summary>
    /// PDF dagi matn, sarlavha va jadvallarni tahrirlanadigan Word (.docx) hujjatiga o'tkazadi.
    /// Shrift, o'lcham, qalin/kursiv va joylashuv imkon qadar saqlanadi.
    /// </summary>
    /// <param name="pdfPath">Manba PDF.</param>
    /// <param name="docxPath">Natija .docx fayli.</param>
    /// <param name="options">Tanib olish rejimi, jadval/sarlavha aniqlash va h.k.</param>
    /// <exception cref="PdfServiceException"/>
    Task PdfToWordAsync(
        string pdfPath,
        string docxPath,
        PdfToWordOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Word hujjatini PDF ga o'tkazadi. Microsoft Word o'rnatilgan bo'lsa (COM) undan,
    /// aks holda dasturga o'rnatilgan OpenXML → PDF renderer'idan foydalaniladi.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task WordToPdfAsync(
        string docxPath,
        string pdfPath,
        WordToPdfOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Skaner qilingan (rasm) PDF ni Tesseract OCR orqali o'qib, matnni Word faylga yozadi.
    /// </summary>
    /// <param name="language">Tesseract tillari, masalan <c>uzb+eng+rus</c>.</param>
    /// <exception cref="PdfServiceException"/>
    Task OcrPdfToWordAsync(
        string scannedPdfPath,
        string docxPath,
        string language = OcrOptions.DefaultLanguage,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Yuqoridagi metodning to'liq sozlamali ko'rinishi.
    /// <para>
    /// Faqat til bilan chaqirish rezolyutsiya (<see cref="OcrOptions.Dpi"/>), rasmni oldindan
    /// tayyorlash (<see cref="OcrOptions.Preprocess"/>) va abzas ajratish
    /// (<see cref="OcrOptions.DetectParagraphs"/>) sozlamalarini yo'qotib yuboradi — ishchi oyna
    /// ularni foydalanuvchidan so'ragani uchun aynan shu ko'rinish ishlatiladi.
    /// </para>
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task OcrPdfToWordAsync(
        string scannedPdfPath,
        string docxPath,
        OcrOptions options,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>PDF dagi jadval va matnni .xlsx kitobiga chiqaradi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task PdfToExcelAsync(
        string pdfPath,
        string xlsxPath,
        PdfToExcelOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Har bir PDF sahifasidan matnli slayd yasab .pptx yozadi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task PdfToPowerPointAsync(
        string pdfPath,
        string pptxPath,
        PdfToPowerPointOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Har bir sahifani alohida rasm fayli sifatida <paramref name="outputFolder"/> ga yozadi.</summary>
    /// <returns>Yaratilgan rasm fayllari.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<IReadOnlyList<string>> PdfToImagesAsync(
        string pdfPath,
        string outputFolder,
        PdfToImageOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF mazmunini oraliq modelga o'qiydi. Konvertorlar shundan foydalanadi, lekin UI ham
    /// hujjatni oldindan ko'rish (preview) uchun chaqirishi mumkin.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task<DocumentContent> ExtractContentAsync(
        string pdfPath,
        PdfToWordOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ushbu kompyuterda Microsoft Word (COM) mavjudmi.</summary>
    bool IsMicrosoftWordAvailable { get; }
}
