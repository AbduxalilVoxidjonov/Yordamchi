using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.Services;

/// <summary>
/// GitHub relizlari ustidagi yangilanish xizmati.
/// <para>
/// Yuklab olingan fayl foydalanuvchining kompyuterida <b>administrator huquqi bilan</b>
/// ishga tushadi, shuning uchun bu yerdagi tekshiruvlar ataylab qattiq: aktiv nomi aniq
/// naqshga mos bo'lishi, havola faqat GitHub xostlarida va https bo'lishi, yuklab olingan
/// faylning hajmi API aytgan qiymatga baytma-bayt teng bo'lishi shart. Har qanday nomuvofiqlik
/// — jimgina rad etish yoki xato, hech qachon "ehtimol to'g'ridir" degan taxmin emas.
/// </para>
/// </summary>
public sealed partial class UpdateService : IUpdateService
{
    /// <summary>
    /// Eng so'nggi reliz uchun GitHub API manzili.
    /// <para>
    /// Repozitoriy qayta nomlangan va manzil to'g'ridan-to'g'ri yangi nomga qaratilgan.
    /// GitHub eski manzilni yo'naltiradi, lekin unga tayanmaymiz: yo'naltirish bir kun
    /// to'xtashi yoki bo'shab qolgan eski nom boshqa birov tomonidan band qilinishi mumkin —
    /// bunda dastur begona repozitoriyning relizini taklif qilardi.
    /// </para>
    /// </summary>
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/AbduxalilVoxidjonov/Yordamchi/releases/latest";

    private const string ReleasesPage =
        "https://github.com/AbduxalilVoxidjonov/Yordamchi/releases";

    /// <summary>O'rnatgich yuklanadigan papka — dastur papkasi emas, chunki u yozish uchun yopiq.</summary>
    private static readonly string UpdatesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yordamchi",
        "Updates");

    /// <summary>Skript nomi doimiy: har yangilanishda papkada yangi fayl to'planib qolmaydi.</summary>
    private const string RestartScriptName = "yordamchi-update.cmd";

    /// <summary>
    /// GitHub aktivlarni ikkita xostdan beradi: havolaning o'zi <c>github.com</c> da,
    /// yo'naltirilgan (redirect) manzil esa <c>objects.githubusercontent.com</c> da.
    /// Boshqa xost — biz nazorat qilmaydigan server, ya'ni rad etiladi.
    /// </summary>
    private static readonly HashSet<string> AllowedDownloadHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "objects.githubusercontent.com"
        };

    /// <summary>Tekshiruv qisqa amal: internet yo'q bo'lsa dastur ochilishida osilib qolmasin.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    /// <summary>O'rnatgich ~150 MB bo'lishi mumkin — model yuklashdagi kabi uzun muhlat.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Butun jarayon uchun bitta <see cref="HttpClient"/>: har safar yangisini yaratish
    /// soketlarni tugatib qo'yadi (socket exhaustion).
    /// </summary>
    private static readonly Lazy<HttpClient> SharedHttpClient =
        new(CreateHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Ikkita sahifa bir vaqtda tekshirsa ham GitHub ga bitta so'rov ketadi.</summary>
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    /// <summary>
    /// Reliz javobini keltiruvchi manba; odatda GitHub API. <c>null</c> qaytishi — reliz
    /// umuman yo'q (HTTP 404), ya'ni "yangilanish yo'q", nosozlik emas.
    /// </summary>
    private readonly Func<CancellationToken, Task<string?>> _releaseJsonSource;

    private UpdateInfo? _cachedUpdate;
    private bool _hasCheckedSuccessfully;

    /// <summary>Parametrsiz konstruktor — DI konteyneri shu tarzda yaratadi.</summary>
    public UpdateService()
        : this(FetchLatestReleaseJsonAsync)
    {
    }

    /// <summary>
    /// Reliz javobini boshqa manbadan oladigan konstruktor.
    /// <para>
    /// Kesh va <c>force</c> mantig'i aynan shu manba ustida ishlaydi, shuning uchun uni
    /// almashtira olish sinovlar uchun yagona yo'l: aks holda "kesh ishladimi" degan savolga
    /// faqat haqiqiy GitHub so'rovi javob berardi, ya'ni sinovlar tarmoqqa bog'lanib qolardi.
    /// </para>
    /// </summary>
    public UpdateService(Func<CancellationToken, Task<string?>> releaseJsonSource)
    {
        ArgumentNullException.ThrowIfNull(releaseJsonSource);
        _releaseJsonSource = releaseJsonSource;
    }

    /// <inheritdoc />
    public Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <inheritdoc />
    public string ReleasesPageUrl => ReleasesPage;

    /// <summary>
    /// Aktiv nomi uchun yagona qabul qilinadigan naqsh. <c>.msi</c>, <c>.zip</c> yoki boshqa
    /// nomdagi fayl — bu bizning o'rnatgichimiz emas, demak ishga tushirilmaydi.
    /// </summary>
    [GeneratedRegex(@"^YordamchiSetup-\d+(\.\d+){1,3}\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetNamePattern();

    // =====================================================================================
    //  Tekshirish
    // =====================================================================================

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckForUpdateAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && _hasCheckedSuccessfully)
            return _cachedUpdate;

        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Navbatda turganda boshqa chaqiruv allaqachon javob olgan bo'lishi mumkin.
            // Qo'lda tekshirishda bu qisqartma ishlamaydi: foydalanuvchi aynan yangi
            // javobni so'ragan, ya'ni GitHub qayta so'ralishi shart.
            if (!force && _hasCheckedSuccessfully)
                return _cachedUpdate;

            var json = await _releaseJsonSource(cancellationToken).ConfigureAwait(false);

            // json == null — repozitoriyada reliz yo'q (404). Bu "yangilanish yo'q" degani,
            // shuning uchun natija ham odatdagidek keshlanadi va foydalanuvchiga qizil
            // xato ko'rsatilmaydi.
            _cachedUpdate = json is null ? null : ParseRelease(json, CurrentVersion);
            _hasCheckedSuccessfully = true;
            return _cachedUpdate;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// GitHub javobini <see cref="UpdateInfo"/> ga aylantiradi. Tarmoqqa chiqmaydi va holatga
    /// tegmaydi — barcha qabul qilish qoidalari aynan shu yerda va shu sababli to'liq sinaladi.
    /// </summary>
    /// <returns>
    /// Yangilanish bo'lsa uning ma'lumoti; JSON buzuq, reliz qoralama (draft) yoki oldindan
    /// chiqarilgan (prerelease), teg versiyaga aylanmasa, versiya joriysidan katta bo'lmasa
    /// yoki mos aktiv topilmasa — <c>null</c>.
    /// </returns>
    public static UpdateInfo? ParseRelease(string json, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // Qoralama va sinov relizlari foydalanuvchilarga taklif qilinmaydi.
            if (ReadBoolean(root, "draft") || ReadBoolean(root, "prerelease"))
                return null;

            var tagName = ReadString(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName) || !TryParseTagVersion(tagName, out var version))
                return null;

            // Teng versiya ham rad etiladi: "qayta o'rnatish" taklifi foydalanuvchini chalg'itadi.
            if (version <= Normalize(currentVersion))
                return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object)
                    continue;

                var assetName = ReadString(asset, "name");
                if (assetName.Length == 0 || !AssetNamePattern().IsMatch(assetName))
                    continue;

                var downloadUrl = ReadString(asset, "browser_download_url");
                if (!IsTrustedDownloadUrl(downloadUrl))
                    continue;

                // Hajmsiz aktivni qabul qilib bo'lmaydi: yuklab olingandan keyin uni
                // solishtirishga narsa qolmaydi, ya'ni yaxlitlik tekshiruvi yo'qoladi.
                var size = ReadInt64(asset, "size");
                if (size <= 0)
                    continue;

                return new UpdateInfo(
                    version,
                    tagName,
                    ReadString(root, "name"),
                    ReadString(root, "body"),
                    downloadUrl,
                    assetName,
                    size,
                    ReadDate(root, "published_at"));
            }

            return null;
        }
        catch (JsonException)
        {
            // Buzuq JSON — server nosozligi yoki proksi qaytargan HTML sahifasi.
            return null;
        }
    }

    /// <summary>Havola https va faqat ruxsat etilgan GitHub xostlarida bo'lishi shart.</summary>
    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && AllowedDownloadHosts.Contains(uri.Host);
    }

    // =====================================================================================
    //  Yuklab olish
    // =====================================================================================

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // Ikkinchi bor tekshiramiz: bu obyekt ParseRelease dan tashqarida ham yasalishi mumkin,
        // ishga tushiriladigan fayl uchun esa bitta tekshiruv joyi kam.
        if (!AssetNamePattern().IsMatch(update.AssetName) || !IsTrustedDownloadUrl(update.DownloadUrl))
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                "Yangilanish havolasi ishonchsiz — yuklab olish to'xtatildi. "
                + "Relizni brauzerda ochib, o'rnatgichni qo'lda yuklab oling.");
        }

        try
        {
            Directory.CreateDirectory(UpdatesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Yangilanish uchun papka yaratilmadi: {UpdatesDirectory}. Papkaga yozish huquqini tekshiring.",
                UpdatesDirectory,
                ex);
        }

        var targetPath = Path.Combine(UpdatesDirectory, update.AssetName);

        // Avvalgi urinishdan to'liq fayl qolgan bo'lsa qaytadan tortib olishning ma'nosi yo'q:
        // hajmi mos kelgani uni allaqachon tekshirilgan qiladi.
        if (TryGetFileLength(targetPath) == update.SizeBytes)
        {
            progress?.Report(new PdfProgress(100, 100, "O'rnatgich allaqachon yuklangan"));
            return targetPath;
        }

        await DownloadCoreAsync(update, targetPath, progress, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    /// <summary>
    /// Oqim orqali yuklaydi va foizni xabar qiladi. Fayl katta bo'lgani uchun
    /// <c>CopyToAsync</c> emas, qo'lda o'qish halqasi ishlatiladi — aks holda progress-bar
    /// yuklash tugagunicha qimirlamasdi.
    /// </summary>
    private static async Task DownloadCoreAsync(
        UpdateInfo update,
        string targetPath,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = targetPath + ".part";

        try
        {
            progress?.Report(new PdfProgress(0, 100, "Yangilanish yuklanmoqda…"));

            using var response = await SharedHttpClient.Value
                .GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    $"O'rnatgich serverdan olinmadi (HTTP {(int)response.StatusCode}). "
                    + "Keyinroq qaytadan urinib ko'ring yoki relizlar sahifasidan qo'lda yuklab oling.",
                    targetPath);
            }

            var totalBytes = response.Content.Headers.ContentLength ?? update.SizeBytes;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
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

                    if (progress is null || totalBytes <= 0)
                        continue;

                    // Har baytda emas, faqat foiz o'zgarganda xabar beramiz: aks holda UI oqimi
                    // minglab yangilanish bilan ko'miladi.
                    var percent = (int)(received * 100 / totalBytes);
                    if (percent == lastReportedPercent)
                        continue;

                    lastReportedPercent = percent;
                    progress.Report(new PdfProgress(
                        percent,
                        100,
                        $"Yangilanish yuklanmoqda… {FormatMegabytes(received)} / {FormatMegabytes(totalBytes)}"));
                }
            }

            // Yaxlitlik tekshiruvi: chala yoki almashtirilgan fayl administrator huquqi bilan
            // ishga tushmasligi kerak.
            var actualSize = TryGetFileLength(partPath);
            if (actualSize != update.SizeBytes)
            {
                SafeDelete(partPath);
                throw new PdfServiceException(
                    PdfErrorKind.CorruptedDocument,
                    $"Yuklab olingan fayl to'liq emas ({actualSize} bayt, kutilgani {update.SizeBytes} bayt). "
                    + "Yuklash to'xtatildi — qaytadan urinib ko'ring.",
                    targetPath);
            }

            File.Move(partPath, targetPath, overwrite: true);
            progress?.Report(new PdfProgress(100, 100, "O'rnatgich tayyor"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(partPath);
            throw;
        }
        catch (PdfServiceException)
        {
            SafeDelete(partPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(partPath);
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Yangilanishni yuklab bo'lmadi — internetga ulanishni tekshiring va qaytadan urinib ko'ring.",
                targetPath,
                ex);
        }
    }

    // =====================================================================================
    //  O'rnatish va qayta ishga tushirish
    // =====================================================================================

    /// <inheritdoc />
    public void LaunchInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                "O'rnatgich fayli topilmadi. Yangilanishni qaytadan yuklab oling.",
                installerPath);
        }

        var applicationPath = Environment.ProcessPath
                              ?? Path.Combine(AppContext.BaseDirectory, "Yordamchi.exe");

        var script = BuildRestartScript(Environment.ProcessId, installerPath, applicationPath);
        var scriptPath = Path.Combine(UpdatesDirectory, RestartScriptName);

        try
        {
            Directory.CreateDirectory(UpdatesDirectory);

            // BOM siz UTF-8: BOM li .cmd faylining birinchi buyrug'ini cmd.exe tanimaydi.
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                "Yangilash skriptini yozib bo'lmadi. O'rnatgichni qo'lda ishga tushirishingiz mumkin: "
                + installerPath,
                scriptPath,
                ex);
        }

        try
        {
            // UseShellExecute — skript joriy jarayondan ajratilgan holda ishlaydi va biz
            // yopilganimizda u bilan birga o'lmaydi; oyna esa foydalanuvchiga ko'rinmaydi.
            //
            // Skriptning o'zi konsolga bog'liq emas (kutish PowerShell da bajariladi),
            // shuning uchun bu yerdagi tanlov faqat ajratilish va ko'rinmaslik uchun.
            Process.Start(new ProcessStartInfo(scriptPath)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = UpdatesDirectory
            });
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Yangilashni boshlab bo'lmadi. O'rnatgichni qo'lda ishga tushirishingiz mumkin: "
                + installerPath,
                installerPath,
                ex);
        }
    }

    /// <summary>
    /// Dastur yopilishini kutib, o'rnatgichni bajaradigan va so'ng dasturni qayta ochadigan
    /// <c>.cmd</c> skript matnini quradi. Fayl tizimiga tegmaydi — shuning uchun to'liq sinaladi.
    /// </summary>
    /// <param name="processId">Chiqib ketishi kutiladigan joriy jarayon identifikatori.</param>
    /// <param name="installerPath">Yuklab olingan o'rnatgich.</param>
    /// <param name="applicationPath">O'rnatishdan keyin qayta ochiladigan dastur (<c>Yordamchi.exe</c>).</param>
    /// <summary>
    /// Javob so'rovlar cheklovi sababli rad etilganmi.
    /// <para>
    /// GitHub buni ikki xil ko'rsatadi: eski yo'l — <c>403</c> va <c>X-RateLimit-Remaining: 0</c>
    /// sarlavhasi, yangisi — <c>429</c>. Oddiy <c>403</c> (masalan yopiq repozitoriy) bundan
    /// farq qiladi va u yerda sarlavha bo'lmaydi, shuning uchun ikkalasi aralashib ketmaydi.
    /// </para>
    /// </summary>
    public static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return true;

        if (response.StatusCode != HttpStatusCode.Forbidden)
            return false;

        return response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
               && values.FirstOrDefault() == "0";
    }

    public static string BuildRestartScript(int processId, string installerPath, string applicationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);

        var pid = processId.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();

        builder.AppendLine("@echo off");

        // Yo'llarda lotin bo'lmagan belgilar bo'lishi mumkin (foydalanuvchi nomi), skript esa
        // UTF-8 bo'lib yoziladi — kod sahifasini ochiqchasiga aytmasak, cmd ularni buzadi.
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("setlocal");
        builder.AppendLine();

        // Kutish ATAYLAB PowerShell ga topshirilgan. Avvalgi variant `tasklist` chiqishini
        // `find` bilan tekshirardi va bu jimgina buzilardi: skript konsolsiz ishga tushsa
        // quvur (pipe) hech narsa qaytarmay, halqa birinchi qadamdayoq o'tib ketardi — ya'ni
        // o'rnatgich dastur hali fayllarni ushlab turganda boshlanardi. `Wait-Process` esa
        // hech qanday matn chiqishiga tayanmaydi.
        //
        // -Timeout: dastur qandaydir sababdan yopilmay qolsa, skript abadiy osilib qolmaydi.
        // Jarayon allaqachon yo'q bo'lsa `Wait-Process` darhol qaytadi (SilentlyContinue).
        builder.AppendLine("rem 1) Dastur to'liq yopilishini kutamiz: band faylni o'rnatgich almashtira olmaydi.");
        builder.AppendLine(
            "powershell -NoProfile -ExecutionPolicy Bypass -Command "
            + $"\"Wait-Process -Id {pid} -Timeout 120 -ErrorAction SilentlyContinue\"");
        builder.AppendLine();

        // Ikkala bayroq ham WiX v5 manbasida tekshirilgan. /passive ni Burn dvigatelining o'zi
        // taniydi (core.cpp) — jarayon ko'rsatkichi ko'rinadi, savol berilmaydi. /norestart ni
        // esa bootstrapper qatlami o'qiydi (balutil/balinfo.cpp) va uni yozmasak, /passive
        // rejimida standart qiymat AUTOMATIC bo'ladi: Visual C++ ish vaqti 3010 qaytarganda
        // (Bundle.wxs da scheduleReboot) kompyuter so'ramasdan qayta yuklanardi.
        builder.AppendLine("rem 2) O'rnatgich foydalanuvchini savolga ko'mmasdan bajariladi va tugashini kutamiz.");
        builder.AppendLine($"start \"\" /wait \"{installerPath}\" /passive /norestart");
        builder.AppendLine();

        builder.AppendLine("rem 3) Yangi versiyani qayta ochamiz.");
        builder.AppendLine($"start \"\" \"{applicationPath}\"");
        builder.AppendLine();

        builder.AppendLine("endlocal");

        return builder.ToString();
    }

    // =====================================================================================
    //  Yordamchilar
    // =====================================================================================

    /// <summary>
    /// GitHub javobini matn sifatida oladi.
    /// </summary>
    /// <returns>
    /// JSON matni; repozitoriyada hali reliz bo'lmasa (HTTP 404) — <c>null</c>.
    /// </returns>
    private static async Task<string?> FetchLatestReleaseJsonAsync(CancellationToken cancellationToken)
    {
        // Tekshiruv uchun alohida, qisqa muhlat: yuklab olish muhlati (30 daqiqa) bu yerda
        // dastur ochilishini muzlatib qo'yardi.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            using var response = await SharedHttpClient.Value
                .GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);

            // 404 — repozitoriyada hali birorta reliz e'lon qilinmagan. Bu nosozlik emas:
            // foydalanuvchiga qizil xato ko'rsatish o'rniga "yangilanish yo'q" deb qaraymiz.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            // GitHub autentifikatsiyasiz so'rovlarni IP bo'yicha soatiga 60 taga cheklaydi.
            // Bitta foydalanuvchi bunga hech qachon yetmaydi, lekin umumiy tarmoqda (ofis,
            // NAT ortidagi o'nlab kompyuter) bu butunlay real holat. "Server javob bermadi"
            // deyish bu yerda noto'g'ri bo'lardi: server javob berdi, shunchaki bizni
            // vaqtincha to'xtatib qo'ydi — va bu o'zidan o'ziga o'tadi.
            if (IsRateLimited(response))
            {
                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    "GitHub so'rovlar sonini vaqtincha cheklab qo'ydi (bir necha kompyuter "
                    + "bitta internet manzilidan foydalanayotgan bo'lishi mumkin). Bir soatdan "
                    + "keyin o'zi tiklanadi; shu orada yangilanishni relizlar sahifasidan "
                    + "qo'lda yuklab olsa bo'ladi.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    $"Yangilanish serveri javob bermadi (HTTP {(int)response.StatusCode}). "
                    + "Keyinroq qaytadan urinib ko'ring.");
            }

            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Yangilanishni tekshirish uzoq davom etdi — internetga ulanishni tekshiring.");
        }
        catch (HttpRequestException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Yangilanishni tekshirib bo'lmadi — internetga ulanishni tekshiring.",
                null,
                ex);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = DownloadTimeout };

        // GitHub API User-Agent siz so'rovlarni rad etadi.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Yordamchi/{version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        return client;
    }

    /// <summary>
    /// Tegdan versiyani oladi: <c>v2.2.0</c> ham, <c>2.2.0</c> ham qabul qilinadi.
    /// </summary>
    private static bool TryParseTagVersion(string tagName, out Version version)
    {
        var text = tagName.Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>
    /// Versiyalarni uch qismga keltiradi. <see cref="Version"/> ko'rsatilmagan qismni <c>-1</c>
    /// deb sanaydi, shu sababli <c>2.1.0</c> va <c>2.1.0.0</c> teng bo'lmay qolardi va
    /// "teng versiya" holati yangilanish deb ko'rinardi.
    /// </summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0L;

    private static DateTimeOffset ReadDate(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : default;

    private static string FormatMegabytes(long bytes) =>
        $"{bytes / (1024d * 1024d):0.#} MB";

    private static long TryGetFileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Yarim yuklangan .part qolib ketsa ham zarari yo'q: u o'rnatgich sifatida
            // ishga tushirilmaydi va keyingi urinishda ustiga yoziladi.
        }
    }
}
