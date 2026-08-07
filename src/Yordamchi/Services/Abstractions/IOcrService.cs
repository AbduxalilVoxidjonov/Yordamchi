using Yordamchi.Models;
using SkiaSharp;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Tesseract OCR ustidagi yupqa qobiq. Rasmdan matnni bloklar va qatorlar ko'rinishida
/// qaytaradi, shuning uchun natijani Word'da abzas sifatida yozish mumkin.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Bitta rasmni tanib oladi va natijani <see cref="ContentPage"/> bloklar ro'yxati sifatida qaytaradi.
    /// </summary>
    /// <param name="image">Tanilishi kerak bo'lgan rasm.</param>
    /// <param name="options">Til, dpi va oldindan ishlov sozlamalari.</param>
    /// <exception cref="PdfServiceException">Til fayli topilmasa <see cref="PdfErrorKind.MissingComponent"/>.</exception>
    Task<ContentPage> RecognizeAsync(
        SKBitmap image,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rasm faylidan oddiy matn ajratadi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task<string> RecognizeTextAsync(
        string imagePath,
        string language = OcrOptions.DefaultLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Skaner qilingan PDF ning barcha sahifalarini tanib, to'liq hujjat modelini qaytaradi.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task<DocumentContent> RecognizePdfAsync(
        string pdfPath,
        OcrOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Tesseract til fayllari (tessdata) joylashgan papka.</summary>
    string TessDataPath { get; }

    /// <summary>Kompyuterda mavjud til kodlari, masalan <c>["eng", "rus", "uzb"]</c>.</summary>
    IReadOnlyList<string> GetInstalledLanguages();

    /// <summary>
    /// <paramref name="language"/> ifodasidagi (<c>uzb+eng+rus</c>) barcha tillar o'rnatilganmi.
    /// </summary>
    /// <param name="missing">Yetishmayotgan til kodlari.</param>
    bool AreLanguagesInstalled(string language, out IReadOnlyList<string> missing);

    /// <summary>
    /// Yetishmayotgan til fayllarini rasmiy <c>tessdata_fast</c> ombordan yuklab oladi.
    /// Internet talab qiladi va faqat foydalanuvchi roziligi bilan chaqirilishi kerak.
    /// </summary>
    /// <returns>Muvaffaqiyatli yuklab olingan tillar.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<IReadOnlyList<string>> DownloadLanguagesAsync(
        IEnumerable<string> languages,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
