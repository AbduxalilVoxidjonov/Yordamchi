using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Arxivlarni o'qish, ochish va yaratish shartnomasi.
/// <para>
/// Ekran yozuvi kabi, bu servis ham <see cref="IPdfEngineService"/> fasadiga
/// <b>kirmaydi</b>: arxivlash PDF quvuriga umuman aloqador emas — u boshqa kutubxonalarga
/// tayanadi, boshqa xato holatlariga ega va o'z sahifasi bilan to'g'ridan-to'g'ri ishlaydi.
/// Fasadga qo'shish uni "hamma narsaning ro'yxati" ga aylantirib yuborardi.
/// </para>
/// <para>
/// Xatolar dastur bo'ylab yagona bo'lishi uchun <see cref="PdfServiceException"/> ko'rinishida
/// chiqadi (nomi "Pdf" bilan boshlansa ham, u dasturning umumiy xato turi:
/// <c>ViewModelBase</c> aynan shuni tushunarli xabarga aylantiradi).
/// </para>
/// </summary>
public interface IArchiveService
{
    /// <summary>O'qish uchun qo'llab-quvvatlanadigan kengaytmalar (<c>.zip</c>, <c>.rar</c>, …).</summary>
    IReadOnlyList<string> SupportedReadExtensions { get; }

    /// <summary>Fayl dialogi uchun tayyor filtr satri.</summary>
    string OpenFilter { get; }

    /// <summary>Kengaytmasiga qarab bu fayl arxivga o'xshaydimi.</summary>
    bool LooksLikeArchive(string path);

    /// <summary>
    /// Arxivni ochmasdan ichidagi ro'yxatni o'qiydi.
    /// </summary>
    /// <param name="archivePath">Arxiv fayli.</param>
    /// <param name="password">Shifrlangan arxivlar uchun parol; kerak bo'lmasa <c>null</c>.</param>
    /// <exception cref="PdfServiceException">
    /// Fayl topilmasa, format tanilmasa, shikastlangan bo'lsa yoki parol noto'g'ri bo'lsa.
    /// Faqat ro'yxat uchun parol talab qilinsa — <see cref="PdfErrorKind.PasswordProtected"/>.
    /// </exception>
    Task<ArchiveInfo> ReadAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Arxivni (yoki undagi tanlangan yozuvlarni) papkaga chiqaradi.
    /// </summary>
    /// <param name="entryPaths">
    /// Faqat shu yozuvlar chiqariladi. <c>null</c> yoki bo'sh bo'lsa — hammasi.
    /// </param>
    /// <returns>Chiqarilgan fayllar soni.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<int> ExtractAsync(
        string archivePath,
        string targetFolder,
        string? password = null,
        IReadOnlyCollection<string>? entryPaths = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fayl va papkalardan <c>.zip</c> arxiv yig'adi. Papkalar rekursiv o'tiladi.
    /// </summary>
    /// <returns>Arxivga yozilgan fayllar soni.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<int> CreateZipAsync(
        IReadOnlyList<string> sourcePaths,
        string archivePath,
        CreateArchiveOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
