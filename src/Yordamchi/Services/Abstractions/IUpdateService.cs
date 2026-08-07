using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Yangi versiya chiqqanini tekshiradi va uni foydalanuvchiga ko'rsatish uchun qaytaradi.
/// <para>
/// Dastur hech narsa yuklab olmaydi va o'rnatmaydi: topilgan reliz "Dastur haqida" sahifasida
/// e'lon qilinadi, o'rnatgichni foydalanuvchi <see cref="ReleasesPageUrl"/> sahifasidan
/// brauzerda o'zi oladi.
/// </para>
/// <para>
/// <see cref="IPdfEngineService"/> fasadiga <b>kirmaydi</b>: bu PDF quvuriga umuman aloqador
/// emas — kirish internetdagi reliz, chiqish esa dasturning versiyasi haqidagi xabar
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

    /// <summary>Brauzerda ochiladigan relizlar sahifasi — o'rnatgich shu yerdan qo'lda olinadi.</summary>
    string ReleasesPageUrl { get; }

    /// <summary>
    /// Eng so'nggi relizni so'raydi va u joriy versiyadan yangi bo'lsa ma'lumotini qaytaradi.
    /// <para>
    /// Muvaffaqiyatli natija jarayon davomida keshlanadi: dastur ochilishida va "Dastur haqida"
    /// sahifasida GitHub ikki marta so'ralmaydi (API so'rovlari soni cheklangan). Xato esa
    /// keshlanmaydi — internet tiklangach qayta urinib ko'rish ishlaydi.
    /// </para>
    /// </summary>
    /// <param name="force">
    /// <c>true</c> bo'lsa kesh chetlab o'tiladi va GitHub qaytadan so'raladi. Bu foydalanuvchi
    /// "Tekshirish" tugmasini bosgan holat uchun: dastur ochiq turganda yangi reliz chiqsa,
    /// keshlangan "yangilanish yo'q" javobi tugmani <b>hech qachon ishlamaydigan</b> qilib
    /// qo'yardi. Jimgina fon tekshiruvi esa <c>false</c> bilan keshdan foydalanaveradi.
    /// </param>
    /// <returns>
    /// Yangi versiya bo'lsa uning ma'lumoti, aks holda <c>null</c>. Repozitoriyada birorta
    /// reliz bo'lmasa (HTTP 404) ham <c>null</c> qaytadi: bu nosozlik emas, shunchaki
    /// yangilanish yo'q.
    /// </returns>
    /// <exception cref="PdfServiceException">Tarmoq yoki server nosozligi.</exception>
    Task<UpdateInfo?> CheckForUpdateAsync(bool force = false, CancellationToken cancellationToken = default);
}
