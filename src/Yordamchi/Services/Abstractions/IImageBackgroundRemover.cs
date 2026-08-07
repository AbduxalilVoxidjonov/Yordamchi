using System.Windows.Media.Imaging;
using Yordamchi.Models;
using SkiaSharp;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Rasm fonini sun'iy intellekt (u2net segmentatsiya modeli) yordamida olib tashlaydi.
/// <para>
/// Model fayli (<c>u2net.onnx</c> yoki yengilroq <c>u2netp.onnx</c>) dastur papkasidagi
/// <c>Models</c> jildidan qidiriladi. Model topilmasa
/// <see cref="PdfErrorKind.MissingComponent"/> turidagi xato qaytadi va UI foydalanuvchiga
/// modelni yuklab olishni taklif qiladi.
/// </para>
/// </summary>
public interface IImageBackgroundRemover
{
    /// <summary>
    /// Rasm faylini o'qiydi, AI modeli orqali obyektni fondan ajratadi va fonni shaffof qiladi.
    /// </summary>
    /// <param name="inputImagePath">Manba rasm (JPG, PNG, BMP, WEBP…).</param>
    /// <param name="progress">0..100 bajarilish foizi.</param>
    /// <returns>UI ga bog'lash uchun tayyor, muzlatilgan (frozen) alfa kanalli tasvir.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<BitmapSource> RemoveBackgroundAsync(
        string inputImagePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Yuqoridagi metodning to'liq sozlanadigan varianti; natijani <see cref="SKBitmap"/> sifatida
    /// qaytaradi, shuning uchun uni PNG qilib saqlash yoki PDF ga joylash mumkin.
    /// Chaqiruvchi qaytgan tasvirni o'zi <c>Dispose</c> qilishi kerak.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task<SKBitmap> RemoveBackgroundToBitmapAsync(
        string inputImagePath,
        BackgroundRemovalOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Natijani alfa kanali saqlangan holda PNG faylga yozadi.</summary>
    /// <exception cref="PdfServiceException"/>
    Task SaveAsPngAsync(
        SKBitmap image,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>Model fayli mavjud va yuklashga tayyormi.</summary>
    bool IsModelAvailable { get; }

    /// <summary>Model fayli kutilayotgan to'liq yo'l — UI xato xabarida ko'rsatadi.</summary>
    string ModelPath { get; }

    /// <summary>Yuklab olinadigan model nomi, masalan <c>u2net.onnx</c>.</summary>
    string DownloadableModelName { get; }

    /// <summary>Yuklab olinadigan modelning taxminiy hajmi (tasdiqlash oynasida ko'rsatiladi).</summary>
    string DownloadableModelSizeText { get; }

    /// <summary>
    /// Modelni rasmiy manbadan yuklab olib, foydalanuvchi yozish huquqiga ega bo'lgan
    /// <c>%LOCALAPPDATA%\Yordamchi\Models</c> papkasiga joylaydi. Model allaqachon mavjud
    /// bo'lsa hech narsa yuklanmaydi.
    /// <para>
    /// Fayl avval vaqtinchalik nomga yoziladi: ulanish uzilsa yoki amal bekor qilinsa,
    /// buzuq <c>.onnx</c> qolib ketmaydi va keyingi urinishda "model bor" deb hisoblanmaydi.
    /// </para>
    /// </summary>
    /// <returns>Yuklab olingan (yoki allaqachon mavjud bo'lgan) model faylining to'liq yo'li.</returns>
    /// <exception cref="PdfServiceException"/>
    Task<string> DownloadModelAsync(
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
