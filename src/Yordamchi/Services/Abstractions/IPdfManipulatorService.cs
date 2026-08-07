using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// PDF fayl ustidagi tezkor operatsiyalar: birlashtirish, bo'lish, siqish, himoyalash,
/// qulfni ochish, suv belgisi va sahifa raqamlari.
/// <para>
/// Barcha metodlar to'liq asinxron — CPU ni band qiladigan PDFsharp/SkiaSharp ishi thread pool da
/// bajariladi, shuning uchun UI hech qachon qotmaydi. Har bir metod <see cref="IProgress{T}"/>
/// orqali bajarilish foizini xabar qiladi (0..100), bu esa progress-bar uchun yetarli.
/// Har qanday nosozlik <see cref="PdfServiceException"/> ko'rinishida qaytadi.
/// </para>
/// </summary>
public interface IPdfManipulatorService
{
    /// <summary>
    /// <paramref name="pdfPaths"/> dagi PDF fayllarni berilgan tartibda ketma-ket qo'shib,
    /// <paramref name="outputPath"/> ga bitta hujjat yozadi.
    /// </summary>
    /// <param name="pdfPaths">Manba fayllar; kamida bittasi bo'lishi kerak.</param>
    /// <param name="outputPath">Natija fayli. Manba fayllardan biri bilan bir xil bo'lishi mumkin.</param>
    /// <param name="progress">0..100 oralig'idagi bajarilish foizi.</param>
    /// <exception cref="PdfServiceException"/>
    Task MergePdfsAsync(
        List<string> pdfPaths,
        string outputPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PDF ni <paramref name="options"/> ga muvofiq bo'laklarga ajratib,
    /// <paramref name="outputFolder"/> ichiga bir nechta fayl yozadi.
    /// </summary>
    /// <returns>Yaratilgan fayllarning to'liq yo'llari.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<IReadOnlyList<string>> SplitPdfAsync(
        string pdfPath,
        string outputFolder,
        SplitOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sodda ko'rinish: sahifa oraliqlari to'g'ridan-to'g'ri ro'yxat sifatida beriladi.
    /// Har bir juftlik — bir fayl bo'lib chiqadigan (birinchi, oxirgi) sahifa raqamlari (1 dan).
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task<IReadOnlyList<string>> SplitPdfAsync(
        string pdfPath,
        string outputFolder,
        List<(int First, int Last)> pageRanges,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hujjat ichidagi rasmlarni pastroq rezolyutsiyada qayta kodlab, keraksiz metama'lumot va
    /// takrorlanuvchi obyektlarni tashlab, fayl hajmini kichraytiradi.
    /// </summary>
    /// <returns>Siqish natijasi: eski/yangi hajm va yutuq foizi.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<CompressionResult> CompressPdfAsync(
        string inputPath,
        string outputPath,
        CompressionLevel level,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ochish uchun parol qo'yadi va nusxa ko'chirish/chop etishni cheklaydi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task ProtectPdfAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>To'liq sozlamalar bilan himoyalash (egalik paroli, ruxsatlar, shifrlash darajasi).</summary>
    /// <exception cref="PdfServiceException"/>
    Task ProtectPdfAsync(
        string inputPath,
        string outputPath,
        ProtectOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paroli ma'lum bo'lgan hujjatdan himoyani olib tashlaydi.
    /// Parol noto'g'ri bo'lsa <see cref="PdfErrorKind.InvalidPassword"/> qaytadi.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task UnlockPdfAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Har bir sahifaga matnli suv belgisi chizadi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task AddWatermarkAsync(
        string inputPath,
        string outputPath,
        WatermarkOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sahifalarni tanlangan joyda raqamlaydi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task AddPageNumbersAsync(
        string inputPath,
        string outputPath,
        PageNumberOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sahifalarni buradi. <paramref name="pageIndices"/> <c>null</c> bo'lsa barcha sahifalar buriladi.
    /// </summary>
    /// <param name="degrees">90 ga karrali burchak; manfiy bo'lishi mumkin.</param>
    /// <exception cref="PdfServiceException"/>
    Task RotatePagesAsync(
        string inputPath,
        string outputPath,
        int degrees,
        IReadOnlyList<int>? pageIndices = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Hujjat parol bilan himoyalanganmi (ochish uchun parol talab qiladimi).</summary>
    Task<bool> IsPasswordProtectedAsync(string pdfPath, CancellationToken cancellationToken = default);
}

/// <summary>Siqish natijasi.</summary>
/// <param name="OriginalBytes">Boshlang'ich hajm.</param>
/// <param name="CompressedBytes">Natijaviy hajm.</param>
/// <param name="ImagesProcessed">Qayta kodlangan rasmlar soni.</param>
public readonly record struct CompressionResult(long OriginalBytes, long CompressedBytes, int ImagesProcessed)
{
    /// <summary>Qancha foizga kichraydi (manfiy bo'lsa — hajm oshgan).</summary>
    public double SavedPercent => OriginalBytes <= 0
        ? 0d
        : Math.Round((OriginalBytes - CompressedBytes) * 100d / OriginalBytes, 1);

    public long SavedBytes => OriginalBytes - CompressedBytes;
}
