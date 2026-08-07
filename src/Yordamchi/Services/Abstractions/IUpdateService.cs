using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Dastur ichidan yangilanishni tekshiradi, o'rnatgichni yuklab oladi va uni ishga tushiradi.
/// <para>
/// <see cref="IPdfEngineService"/> fasadiga <b>kirmaydi</b>: bu PDF quvuriga umuman aloqador
/// emas — kirish internetdagi reliz, chiqish esa dasturning o'zini almashtiradigan o'rnatgich
/// (ekran yozuvi va arxiv bilan bir xil sabab).
/// </para>
/// <para>
/// Xatolar dastur bo'ylab yagona <see cref="PdfServiceException"/> ko'rinishida chiqadi, shu
/// tufayli <c>ViewModelBase.RunAsync</c> ularni odatdagidek tushunarli xabarga aylantiradi.
/// </para>
/// </summary>
public interface IUpdateService
{
    /// <summary>Hozir ishlab turgan dastur versiyasi.</summary>
    Version CurrentVersion { get; }

    /// <summary>Brauzerda ochiladigan relizlar sahifasi ("Nima o'zgardi" tugmasi).</summary>
    string ReleasesPageUrl { get; }

    /// <summary>
    /// Eng so'nggi relizni so'raydi va u joriy versiyadan yangi bo'lsa ma'lumotini qaytaradi.
    /// <para>
    /// Muvaffaqiyatli natija jarayon davomida keshlanadi: dastur ochilishida va "Dastur haqida"
    /// sahifasida GitHub ikki marta so'ralmaydi (API so'rovlari soni cheklangan). Xato esa
    /// keshlanmaydi — internet tiklangach qayta urinib ko'rish ishlaydi.
    /// </para>
    /// </summary>
    /// <returns>Yangi versiya bo'lsa uning ma'lumoti, aks holda <c>null</c>.</returns>
    /// <exception cref="PdfServiceException">Tarmoq yoki server nosozligi.</exception>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// O'rnatgichni <c>%LOCALAPPDATA%\Yordamchi\Updates</c> papkasiga yuklab oladi.
    /// <para>
    /// Fayl avval <c>.part</c> nomiga yoziladi va faqat hajmi API aytgan qiymatga to'liq mos
    /// kelgandan keyin asl nomiga ko'chiriladi: chala yuklangan o'rnatgich hech qachon
    /// ishga tushirilmaydi.
    /// </para>
    /// </summary>
    /// <returns>Yuklab olingan o'rnatgichning to'liq yo'li.</returns>
    /// <exception cref="PdfServiceException"/>
    /// <exception cref="OperationCanceledException">Foydalanuvchi bekor qilganda.</exception>
    Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// O'rnatishni va qayta ishga tushirishni bajaradigan skriptni ajratilgan holda ishga tushiradi.
    /// <para>
    /// Bu metod dasturning o'zini <b>yopmaydi</b> — skript joriy jarayon chiqib ketishini kutadi,
    /// shuning uchun chaqiruvchi (UI qatlami) darhol yopilishi kerak.
    /// </para>
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    void LaunchInstaller(string installerPath);
}
