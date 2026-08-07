using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// O'zbek kirill ↔ lotin o'girish bo'limining shartnomasi.
/// <para>
/// <see cref="IPdfEngineService"/> fasadiga <b>kirmaydi</b>: bu yerda PDF quvuri yo'q — kirish
/// oddiy matn yoki Word hujjati, chiqish ham shunday. Uni fasadga qo'shish "PDF dvigateli" ga
/// PDF ga umuman aloqasi bo'lmagan mas'uliyat qo'shgan bo'lardi.
/// </para>
/// <para>
/// Xatolarni dastur bo'ylab bir xil qilish uchun <see cref="PdfServiceException"/> tashlaydi —
/// <c>ViewModelBase.RunAsync</c> uni tushunarli xabarga aylantiradi.
/// </para>
/// </summary>
public interface ITransliterationService
{
    /// <summary>O'girib bo'ladigan fayl kengaytmalari (kichik harfda, nuqtasi bilan).</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Fayl tanlash oynasi uchun tayyor filtr satri.</summary>
    string OpenFilter { get; }

    /// <summary>Kengaytmasiga qarab tez tekshiruv (faylni ochmaydi).</summary>
    bool IsSupported(string? path);

    /// <summary>Matnni darhol o'giradi — faylsiz, sinxron: mingta belgi ham sezilmaydi.</summary>
    string ConvertText(string? text, TransliterationOptions options);

    /// <summary>Matn qaysi alifboda ekanini aniqlaydi; harf topilmasa <c>null</c>.</summary>
    TransliterationDirection? DetectDirection(string? text);

    /// <summary>
    /// Manba fayl nomidan natija yo'lini taklif qiladi: <c>hujjat.docx</c> →
    /// <c>hujjat-lotin.docx</c>. Bunday nom band bo'lsa raqam qo'shiladi, ya'ni oldingi
    /// natija ustiga jimgina yozilmaydi.
    /// </summary>
    string SuggestOutputPath(string sourcePath, string? outputFolder, TransliterationDirection direction);

    /// <summary>
    /// Faylni o'girib yangi faylga yozadi; manba faylga tegilmaydi. <c>.docx</c> da butun
    /// formatlash saqlanadi, <c>.txt</c> esa UTF-8 (BOM bilan) ko'rinishida yoziladi.
    /// <para>
    /// Natija nomi <b>servis tomonidan</b> tanlanadi (<see cref="SuggestOutputPath"/>), chunki
    /// avtomatik aniqlashda yo'nalish faqat fayl o'qilgach ma'lum bo'ladi. Yakuniy yo'l
    /// <see cref="TransliterationFileResult.OutputPath"/> da qaytadi.
    /// </para>
    /// </summary>
    /// <param name="outputFolder">Natija papkasi; <c>null</c> bo'lsa manba fayl papkasi.</param>
    /// <exception cref="PdfServiceException">Fayl topilmadi, format mos emas, yozib bo'lmadi.</exception>
    /// <exception cref="OperationCanceledException">Foydalanuvchi bekor qilganda.</exception>
    Task<TransliterationFileResult> ConvertFileAsync(
        string sourcePath,
        string? outputFolder,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
