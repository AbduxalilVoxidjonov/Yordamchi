using System.IO;
using System.Net.Http;
using System.Reflection;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.Services;

/// <summary>
/// <inheritdoc cref="IRemoteControlService"/>
/// <para>
/// <b>Xavfsizlik.</b> Yuklab olish faqat GitHub xostlaridan (<c>https</c>) ruxsat etiladi —
/// manzilni tekshirish <see cref="UpdateService.IsTrustedDownloadUrl"/> orqali, ya'ni butun
/// dasturda bitta ishonch ro'yxati ishlaydi. Agent boshqa kompyuterlarga o'rnatiladigan
/// dastur bo'lgani uchun uni begona serverdan tortib olishga yo'l qo'yilmaydi.
/// </para>
/// </summary>
public sealed class RemoteControlService : IRemoteControlService
{
    /// <summary>
    /// Agentning oldindan sozlangan manzili — GitHub relizidagi <c>agent-v1</c> aktivi.
    /// <para>
    /// Havola <b>versiyasiz</b> nomga bog'langan (<c>YordamchiAgentSetup.exe</c>): agentning yangi
    /// nusxasi chiqqanda shu tegdagi aktiv almashtiriladi va dasturni qayta yig'ish kerak
    /// bo'lmaydi. Manzil <see cref="UpdateService.IsTrustedDownloadUrl"/> tekshiruvidan o'tadi,
    /// ya'ni bu doimiy o'zgartirilsa ham faqat GitHub xostiga yo'l qoladi.
    /// </para>
    /// </summary>
    private const string ConfiguredDownloadUrl =
        "https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/agent-v1/YordamchiAgentSetup.exe";

    /// <summary>UI da namuna sifatida ko'rsatiladigan manzil (sozlangani bilan bir xil shakl).</summary>
    private const string ExampleUrl = ConfiguredDownloadUrl;

    /// <summary>Agent o'rnatgichi bir necha o'nlab megabayt bo'lishi mumkin, shuning uchun uzoqroq muhlat.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Butun jarayon uchun bitta <see cref="HttpClient"/>: har safar yangisini yaratish
    /// soketlarni tugatib qo'yadi (socket exhaustion).
    /// </summary>
    private static readonly Lazy<HttpClient> SharedHttpClient =
        new(CreateHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Foydalanuvchi yozish huquqiga ega bo'lgan, dasturga tegishli papka.</summary>
    private static readonly string BaseFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yordamchi",
        "RemoteControl");

    /// <inheritdoc />
    public string DefaultDownloadUrl => ConfiguredDownloadUrl;

    /// <inheritdoc />
    public string ExampleDownloadUrl => ExampleUrl;

    /// <inheritdoc />
    public string DownloadFolder => BaseFolder;

    /// <inheritdoc />
    public string AgentFileName => "YordamchiAgentSetup.exe";

    /// <inheritdoc />
    public string AgentFilePath => Path.Combine(BaseFolder, AgentFileName);

    /// <inheritdoc />
    public bool IsAgentDownloaded
    {
        get
        {
            try
            {
                return File.Exists(AgentFilePath);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool IsDownloadUrlReady(string? url) =>
        !string.IsNullOrWhiteSpace(url) && UpdateService.IsTrustedDownloadUrl(url.Trim());

    /// <inheritdoc />
    public async Task<string> DownloadAgentAsync(
        string url,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsDownloadUrlReady(url))
        {
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                "Agent faylining manzili ko'rsatilmagan yoki noto'g'ri. Manzil https bo'lishi va "
                + "GitHub'da joylashishi kerak, masalan:\n" + ExampleUrl);
        }

        try
        {
            Directory.CreateDirectory(BaseFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Yuklab olish uchun papka yaratilmadi: {BaseFolder}. Papkaga yozish huquqini tekshiring.",
                BaseFolder,
                ex);
        }

        await DownloadCoreAsync(url.Trim(), AgentFilePath, progress, cancellationToken).ConfigureAwait(false);
        return AgentFilePath;
    }

    /// <summary>
    /// Faylni oqim orqali yuklaydi va foizni xabar qiladi. Fayl katta bo'lishi mumkin, shuning
    /// uchun <c>CopyToAsync</c> emas, qo'lda o'qish halqasi — aks holda progress-bar yuklash
    /// tugagunicha qimirlamasdi. Avval <c>.tmp</c> ga yoziladi, so'ng ko'chiriladi: yarim
    /// yuklangan fayl "tayyor" ko'rinib qolmasin.
    /// </summary>
    private static async Task DownloadCoreAsync(
        string url,
        string targetPath,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".tmp";

        try
        {
            progress?.Report(new PdfProgress(0, 100, "Agent yuklanmoqda…"));

            using var response = await SharedHttpClient.Value
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    $"Agent serverdan olinmadi (HTTP {(int)response.StatusCode}). Manzil to'g'riligini "
                    + "tekshiring yoki keyinroq qaytadan urinib ko'ring.",
                    targetPath);
            }

            // Server hajmni bermasligi mumkin (chunked) — u holda foiz o'rniga aylanma ko'rsatkich qoladi.
            var totalBytes = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastReportedPercent = -1;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;

                    if (progress is null || totalBytes is not > 0)
                        continue;

                    // Har baytda emas, faqat foiz o'zgarganda xabar beramiz: aks holda UI oqimi
                    // minglab yangilanish bilan ko'miladi.
                    var percent = (int)(received * 100 / totalBytes.Value);
                    if (percent == lastReportedPercent)
                        continue;

                    lastReportedPercent = percent;
                    progress.Report(new PdfProgress(
                        percent, 100,
                        $"Agent yuklanmoqda… {FormatMegabytes(received)} / {FormatMegabytes(totalBytes.Value)}"));
                }
            }

            File.Move(tempPath, targetPath, overwrite: true);
            progress?.Report(new PdfProgress(100, 100, "Agent tayyor"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (PdfServiceException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Agentni yuklab bo'lmadi — internetga ulanishni tekshiring va qaytadan urinib ko'ring.",
                targetPath,
                ex);
        }
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / (1024d * 1024d):0.#} MB";

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Vaqtinchalik faylni o'chira olmasak ham, foydalanuvchiga bu haqda xabar berishning ma'nosi yo'q.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = DownloadTimeout };

        // GitHub User-Agent siz so'rovlarni rad etadi.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.3";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Yordamchi/{version}");

        return client;
    }
}
