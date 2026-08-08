using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// "Kompyuterlarni boshqarish" bo'limining shartnomasi: boshqa kompyuterlarga o'rnatiladigan
/// <b>agent</b> (server) faylini GitHub'dan yuklab olish va uni qayerga saqlashni boshqarish.
/// <para>
/// Bu bo'lim — <b>tarqatish markazi</b>: agentning o'zi (ekran uzatish, boshqaruv, xizmat)
/// alohida, katta loyiha bo'lib, u GitHub relizlariga qo'yiladi va shu yerdan yuklab olinadi.
/// Dastur faylni faqat <b>yuklab oladi</b>, uni o'zi ishga tushirmaydi: o'rnatishni
/// foydalanuvchi maqsadli kompyuterda administrator huquqida o'zi bajaradi.
/// </para>
/// <para>
/// <see cref="IPdfEngineService"/> fasadiga <b>kirmaydi</b>: bu PDF quvuriga umuman aloqador
/// emas — arxiv, ekran yozuvi va sanoq sistemalari kabi mustaqil bo'lim.
/// </para>
/// </summary>
public interface IRemoteControlService
{
    /// <summary>
    /// Agentning oldindan sozlangan yuklab olish manzili. Hozircha bo'sh (placeholder): real
    /// reliz aktivi chiqqach, u shu yerga yoki UI dagi maydonga qo'yiladi.
    /// </summary>
    string DefaultDownloadUrl { get; }

    /// <summary>UI da namuna sifatida ko'rsatiladigan, kutilayotgan manzil shakli.</summary>
    string ExampleDownloadUrl { get; }

    /// <summary>Yuklab olingan agent fayli saqlanadigan papka.</summary>
    string DownloadFolder { get; }

    /// <summary>Agent fayli mahalliy diskda saqlanadigan nom.</summary>
    string AgentFileName { get; }

    /// <summary>Yuklab olingan agent faylining to'liq yo'li.</summary>
    string AgentFilePath { get; }

    /// <summary>Agent fayli allaqachon yuklab olinganmi.</summary>
    bool IsAgentDownloaded { get; }

    /// <summary>
    /// Berilgan manzil yuklab olishga yaroqlimi: bo'sh emas, <c>https</c> va faqat GitHub
    /// xostlarida. Boshqa server — biz nazorat qilmaydigan manba, ya'ni rad etiladi.
    /// </summary>
    bool IsDownloadUrlReady(string? url);

    /// <summary>
    /// Agentni berilgan manzildan yuklab oladi va <see cref="AgentFilePath"/> ga saqlaydi.
    /// Faylni ishga <b>tushirmaydi</b>.
    /// </summary>
    /// <returns>Saqlangan faylning to'liq yo'li.</returns>
    /// <exception cref="PdfServiceException">Manzil yaroqsiz, papka yozilmaydi yoki tarmoq nosozligi.</exception>
    Task<string> DownloadAgentAsync(
        string url,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
