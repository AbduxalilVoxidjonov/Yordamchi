using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.Services;

/// <summary>
/// GitHub relizlari ustidagi yangilanish xizmati — faqat <b>tekshiradi</b>.
/// <para>
/// Dastur hech narsa yuklab olmaydi va hech narsani ishga tushirmaydi: topilgan reliz
/// foydalanuvchiga ko'rsatiladi, o'rnatgichni esa u brauzerda relizlar sahifasidan o'zi
/// oladi. Shunga qaramay bu yerdagi tekshiruvlar ataylab qattiq: aktiv nomi aniq naqshga
/// mos bo'lishi, havola faqat GitHub xostlarida va https bo'lishi shart — foydalanuvchiga
/// ko'rsatiladigan havola ham begona serverga olib bormasligi kerak. Har qanday nomuvofiqlik
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
    /// nomdagi fayl — bu bizning o'rnatgichimiz emas, demak yangilanish sifatida ko'rsatilmaydi.
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

                // Hajmsiz aktiv — javob to'liq emas: foydalanuvchi nechchi megabayt yuklashini
                // bilmay qoladi va bu ko'pincha aktiv hali yuklanib bo'lmaganini bildiradi.
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
        var client = new HttpClient { Timeout = CheckTimeout };

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
}
