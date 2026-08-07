using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Dasturning yagona "dvigateli" — barcha modullarni bitta joyda birlashtiruvchi fasad.
/// <para>
/// UI qatlami ayrim kutubxonalarni (PDFsharp, PdfPig, OpenXML, Tesseract, ONNX Runtime) umuman
/// bilmaydi: u yoki mos sub-servisni oladi, yoki universal <see cref="ExecuteAsync"/> ni chaqiradi.
/// Shu tufayli yangi modul qo'shish UI ni o'zgartirishni talab qilmaydi — faqat
/// <see cref="ToolCatalog"/> ga yozuv va shu fasadga bitta <c>case</c> qo'shiladi.
/// </para>
/// <para>
/// Qatlamlar: <c>Views</c> → <c>ViewModels</c> → <c>IPdfEngineService</c> → modul servislari →
/// kutubxonalar. Har bir nosozlik <see cref="PdfServiceException"/> ko'rinishida yuqoriga chiqadi.
/// </para>
/// </summary>
public interface IPdfEngineService
{
    // ------------------------------------------------------------------
    //  Modul servislari
    // ------------------------------------------------------------------

    /// <summary>Sahifalarni rasterizatsiya qilish, eskiz chizish va sahifa rejasini yozish.</summary>
    IPdfService Pages { get; }

    /// <summary>Birlashtirish, bo'lish, siqish, himoyalash, suv belgisi, raqamlash.</summary>
    IPdfManipulatorService Documents { get; }

    /// <summary>PDF ↔ Word/Excel/PowerPoint/rasm konvertatsiyasi.</summary>
    IDocumentConversionService Conversion { get; }

    /// <summary>Tesseract OCR.</summary>
    IOcrService Ocr { get; }

    /// <summary>u2net (ONNX) bilan rasm fonini olib tashlash.</summary>
    IImageBackgroundRemover BackgroundRemover { get; }

    // ------------------------------------------------------------------
    //  Universal bajarish
    // ------------------------------------------------------------------

    /// <summary>
    /// Tanlangan vositani berilgan so'rov bilan bajaradi va natijani qaytaradi.
    /// </summary>
    /// <exception cref="PdfServiceException">Har qanday kutilgan nosozlik.</exception>
    /// <exception cref="OperationCanceledException">Foydalanuvchi bekor qilganda.</exception>
    Task<ToolRunResult> ExecuteAsync(
        ToolRequest request,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vosita ishga tushishidan oldin so'rovni tekshiradi (fayllar bormi, parol kiritilganmi,
    /// OCR tili o'rnatilganmi va h.k.). Muammo bo'lsa — foydalanuvchiga tushunarli matn,
    /// aks holda <c>null</c>.
    /// </summary>
    string? Validate(ToolRequest request);

    /// <summary>
    /// Vosita ishlashi uchun tashqi komponent kerakmi va u mavjudmi — masalan OCR til fayllari
    /// yoki u2net modeli. UI shu asosda ogohlantirish paneli ko'rsatadi.
    /// </summary>
    /// <param name="tool">Tekshirilayotgan vosita.</param>
    /// <param name="options">Vosita sozlamalari (til tanlovi shu yerdan olinadi); ixtiyoriy.</param>
    /// <returns>Muammo tavsifi yoki <c>null</c>.</returns>
    string? CheckPrerequisites(ToolId tool, object? options = null);

    /// <summary>
    /// <see cref="CheckPrerequisites"/> ning mashina o'qiy oladigan varianti: yetishmayotgan
    /// komponentni dastur o'zi yuklab olib bera oladimi va qaysi birini. Ishchi oyna shu asosda
    /// ogohlantirish yoniga "Yuklab olish" tugmasini qo'yadi.
    /// </summary>
    DownloadableComponent GetMissingComponent(ToolId tool, object? options = null);

    /// <summary>
    /// Yetishmayotgan komponentni yuklab oladi. <see cref="DownloadableComponent.None"/> uchun
    /// hech narsa qilmaydi.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task DownloadComponentAsync(
        DownloadableComponent component,
        object? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
