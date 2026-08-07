using System.Globalization;
using System.Net;
using System.Net.Http;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="UpdateService"/> ning tarmoqsiz qismlari: GitHub javobini o'qish, havolani
/// tekshirish va so'rovlar cheklovini tanish.
/// <para>
/// Bu metodlar ataylab statik va holatsiz qilingan. Dastur endi hech narsa yuklab olmaydi,
/// lekin qabul qilish qoidalari baribir qattiq: noto'g'ri aktiv yoki begona xostdagi havola
/// foydalanuvchiga ko'rsatilmasligi kerak. Shuning uchun bu yerda "yaxshi holat" emas, asosan
/// <b>rad etilishi shart</b> bo'lgan holatlar sinaladi. Hech bir sinov internetga chiqmaydi.
/// </para>
/// </summary>
public sealed class UpdateServiceTests
{
    private static readonly Version Current = new(2, 1, 0);

    // =================================================================================
    //  Versiyani taqqoslash
    // =================================================================================

    [Fact]
    public void A_newer_release_is_offered_with_its_asset()
    {
        var update = UpdateService.ParseRelease(Release(tag: "v2.2.0"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 2, 0), update.Version);
        Assert.Equal("v2.2.0", update.TagName);
        Assert.Equal("YordamchiSetup-2.2.0.exe", update.AssetName);
        Assert.Equal(123456789, update.SizeBytes);
        Assert.StartsWith("https://github.com/", update.DownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tag_without_the_v_prefix_is_accepted_too()
    {
        // Relizlar qo'lda ham yaratiladi — teg uslubi bir xil bo'lishiga tayanib bo'lmaydi.
        var update = UpdateService.ParseRelease(Release(tag: "2.2.0"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 2, 0), update.Version);
    }

    [Fact]
    public void The_same_version_is_not_offered()
    {
        // "Qayta o'rnatish" taklifi foydalanuvchini chalg'itadi va befoyda trafik sarflaydi.
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2.1.0", asset: "YordamchiSetup-2.1.0.exe"), Current));
    }

    [Fact]
    public void A_four_part_tag_equal_to_the_assembly_version_is_not_offered()
    {
        // Yig'ilma versiyasi to'rt qismli (2.1.0.0), teg esa uch qismli bo'ladi — bu ikkisi
        // bir xil sanalishi kerak, aks holda har ochilishda "yangilanish bor" deb ko'rinardi.
        Assert.Null(UpdateService.ParseRelease(
            Release(tag: "v2.1.0", asset: "YordamchiSetup-2.1.0.exe"),
            new Version(2, 1, 0, 0)));
    }

    [Fact]
    public void An_older_release_is_not_offered()
    {
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2.0.9", asset: "YordamchiSetup-2.0.9.exe"), Current));
    }

    [Fact]
    public void A_tag_that_is_not_a_version_is_rejected()
    {
        Assert.Null(UpdateService.ParseRelease(Release(tag: "nightly"), Current));
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2"), Current));
        Assert.Null(UpdateService.ParseRelease(Release(tag: string.Empty), Current));
    }

    // =================================================================================
    //  Reliz turi
    // =================================================================================

    [Fact]
    public void A_draft_release_is_ignored()
    {
        // Qoralama hali e'lon qilinmagan — uni faqat muallif ko'radi.
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2.2.0", draft: true), Current));
    }

    [Fact]
    public void A_prerelease_is_ignored()
    {
        // Sinov versiyasi barcha foydalanuvchilarga tarqatilmaydi.
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2.2.0", prerelease: true), Current));
    }

    // =================================================================================
    //  Aktiv (asset) nomi
    // =================================================================================

    [Fact]
    public void A_release_without_assets_is_ignored()
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseWithAssets("v2.2.0", "[]"), Current));
        Assert.Null(UpdateService.ParseRelease(
            """{"tag_name":"v2.2.0","draft":false,"prerelease":false}""",
            Current));
    }

    [Theory]
    [InlineData("YordamchiSetup-2.2.0.msi")]     // o'rnatgich .exe bo'lishi shart
    [InlineData("YordamchiSetup-2.2.0.zip")]
    [InlineData("Yordamchi-2.2.0.exe")]          // boshqa nom — bizning o'rnatgichimiz emas
    [InlineData("YordamchiSetup.exe")]           // versiyasiz
    [InlineData("YordamchiSetup-beta.exe")]      // raqamli versiya emas
    [InlineData("YordamchiSetup-2.2.0.exe.bak")]
    [InlineData("evil-YordamchiSetup-2.2.0.exe")]
    public void An_asset_with_an_unexpected_name_is_rejected(string assetName)
    {
        Assert.Null(UpdateService.ParseRelease(Release(tag: "v2.2.0", asset: assetName), Current));
    }

    [Fact]
    public void The_matching_asset_is_picked_from_a_mixed_list()
    {
        // Relizga odatda checksum va portativ arxiv ham qo'shiladi.
        const string assets = """
        [
          { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/a/b/releases/download/v2.2.0/SHA256SUMS.txt", "size": 120 },
          { "name": "Yordamchi-portable-2.2.0.zip", "browser_download_url": "https://github.com/a/b/releases/download/v2.2.0/Yordamchi-portable-2.2.0.zip", "size": 900 },
          { "name": "YordamchiSetup-2.2.0.exe", "browser_download_url": "https://github.com/a/b/releases/download/v2.2.0/YordamchiSetup-2.2.0.exe", "size": 4242 }
        ]
        """;

        var update = UpdateService.ParseRelease(ReleaseWithAssets("v2.2.0", assets), Current);

        Assert.NotNull(update);
        Assert.Equal("YordamchiSetup-2.2.0.exe", update.AssetName);
        Assert.Equal(4242, update.SizeBytes);
    }

    [Fact]
    public void An_asset_without_a_size_is_rejected()
    {
        // Hajmsiz aktiv — javob to'liq emas: kartochkada ko'rsatishga hajm qolmaydi.
        const string assets = """
        [ { "name": "YordamchiSetup-2.2.0.exe", "browser_download_url": "https://github.com/a/b/YordamchiSetup-2.2.0.exe", "size": 0 } ]
        """;

        Assert.Null(UpdateService.ParseRelease(ReleaseWithAssets("v2.2.0", assets), Current));
    }

    // =================================================================================
    //  Yuklab olish havolasi
    // =================================================================================

    [Theory]
    [InlineData("http://github.com/a/b/YordamchiSetup-2.2.0.exe")]          // shifrlanmagan
    [InlineData("https://evil.com/YordamchiSetup-2.2.0.exe")]               // begona xost
    [InlineData("https://github.com.evil.com/YordamchiSetup-2.2.0.exe")]    // o'xshatilgan xost
    [InlineData("https://raw.githubusercontent.com/YordamchiSetup-2.2.0.exe")]
    [InlineData("file:///C:/temp/YordamchiSetup-2.2.0.exe")]
    [InlineData("nimadir")]
    [InlineData("")]
    public void A_download_url_outside_github_is_rejected(string url)
    {
        var assets = $$"""
        [ { "name": "YordamchiSetup-2.2.0.exe", "browser_download_url": "{{url}}", "size": 4242 } ]
        """;

        Assert.False(UpdateService.IsTrustedDownloadUrl(url));
        Assert.Null(UpdateService.ParseRelease(ReleaseWithAssets("v2.2.0", assets), Current));
    }

    [Theory]
    [InlineData("https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/v2.2.0/YordamchiSetup-2.2.0.exe")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2?token=abc")]
    public void Github_release_hosts_are_trusted(string url) =>
        Assert.True(UpdateService.IsTrustedDownloadUrl(url));

    // =================================================================================
    //  Buzuq javob
    // =================================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("{}")]
    public void A_malformed_response_is_treated_as_no_update(string json)
    {
        // Proksi yoki server nosozligi HTML qaytarishi mumkin — bu dasturni yiqitmasligi kerak.
        Assert.Null(UpdateService.ParseRelease(json, Current));
    }

    [Fact]
    public void The_release_name_and_notes_are_carried_over()
    {
        const string json = """
        {
          "tag_name": "v2.2.0",
          "name": "Yordamchi 2.2.0",
          "body": "Yangi: ekran yozuvi.",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-08-01T10:20:30Z",
          "assets": [
            { "name": "YordamchiSetup-2.2.0.exe", "browser_download_url": "https://github.com/a/b/YordamchiSetup-2.2.0.exe", "size": 4242 }
          ]
        }
        """;

        var update = UpdateService.ParseRelease(json, Current);

        Assert.NotNull(update);
        Assert.Equal("Yordamchi 2.2.0", update.ReleaseName);
        Assert.Equal("Yangi: ekran yozuvi.", update.ReleaseNotes);
        Assert.Equal(2026, update.PublishedAt.Year);
        Assert.Equal("2.2.0", update.VersionText);
        Assert.Contains("MB", update.SizeText, StringComparison.Ordinal);
    }

    // =================================================================================
    //  So'rovlar cheklovi
    // =================================================================================

    [Fact]
    public void A_throttled_response_is_told_apart_from_a_plain_refusal()
    {
        // GitHub autentifikatsiyasiz so'rovlarni IP bo'yicha soatiga 60 taga cheklaydi va buni
        // 403 bilan bildiradi — ya'ni oddiy "ruxsat yo'q" bilan bir xil kod. Ularni ajratmasak,
        // umumiy tarmoqdagi (ofis, NAT) foydalanuvchi "server javob bermadi" degan noto'g'ri
        // va foydasiz xabar olardi.
        using var throttled = new HttpResponseMessage(HttpStatusCode.Forbidden);
        throttled.Headers.Add("X-RateLimit-Remaining", "0");

        Assert.True(UpdateService.IsRateLimited(throttled));
    }

    [Fact]
    public void A_plain_forbidden_response_is_not_treated_as_throttling()
    {
        using var forbidden = new HttpResponseMessage(HttpStatusCode.Forbidden);

        Assert.False(UpdateService.IsRateLimited(forbidden));
    }

    [Fact]
    public void A_response_with_requests_left_is_not_throttling()
    {
        using var allowed = new HttpResponseMessage(HttpStatusCode.Forbidden);
        allowed.Headers.Add("X-RateLimit-Remaining", "42");

        Assert.False(UpdateService.IsRateLimited(allowed));
    }

    [Fact]
    public void The_newer_too_many_requests_status_counts_as_throttling()
    {
        using var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.True(UpdateService.IsRateLimited(throttled));
    }

    [Fact]
    public void A_successful_response_is_never_throttling()
    {
        using var ok = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.False(UpdateService.IsRateLimited(ok));
    }

    // =================================================================================
    //  Haqiqiy GitHub javobi
    //
    //  Yuqoridagi sinovlar qo'lda yozilgan JSON ustida ishlaydi — ular naqshimizni o'zimiz
    //  o'ylab topgan ma'lumotga solishtiradi. Quyidagilar esa serverdan olingan asl javobni
    //  o'qiydi: maydon nomi yoki aktiv nomi bir harf bilan farq qilsa, yangilanish jimgina
    //  hech qachon ko'rinmaydi va buni boshqa hech qanday sinov tutmaydi.
    // =================================================================================

    [Fact]
    public void The_real_github_response_yields_the_setup_exe()
    {
        var update = UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 0, 0));

        Assert.NotNull(update);
        Assert.Equal(GitHubReleaseSample.TagName, update.TagName);
        Assert.Equal(new Version(2, 1, 0), update.Version);
        Assert.Equal("2.1.0", update.VersionText);
        Assert.Equal(GitHubReleaseSample.SetupAssetName, update.AssetName);
        Assert.Equal(GitHubReleaseSample.SetupDownloadUrl, update.DownloadUrl);
        Assert.Equal(GitHubReleaseSample.SetupSizeBytes, update.SizeBytes);
        Assert.True(UpdateService.IsTrustedDownloadUrl(update.DownloadUrl));

        // Reliz sarlavhasi va izohi kartochkada ko'rsatiladi — javobda ular bor.
        Assert.NotEmpty(update.DisplayName);
        Assert.NotEmpty(update.ReleaseNotes);
        Assert.Equal(2026, update.PublishedAt.Year);
    }

    [Fact]
    public void The_real_response_really_contains_a_second_asset_that_must_be_skipped()
    {
        // Relizda .msi ham bor. Agar naqsh kengroq bo'lsa, dastur foydalanuvchiga o'rnatuvchi
        // o'rniga MSI ni ko'rsatib, uni noto'g'ri faylga yo'naltirardi.
        Assert.Contains(GitHubReleaseSample.MsiAssetName, GitHubReleaseSample.Json, StringComparison.Ordinal);

        var update = UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 0, 0));

        Assert.NotNull(update);
        Assert.EndsWith(".exe", update.AssetName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_response_is_not_offered_to_a_newer_build()
    {
        // Yig'ilma relizdan yangi bo'lsa (masalan ishlab chiquvchi mashinasida) — taklif yo'q.
        Assert.Null(UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(99, 0, 0)));
    }

    [Fact]
    public void The_real_response_is_not_offered_to_the_same_build()
    {
        Assert.Null(UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 1, 0)));
        Assert.Null(UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 1, 0, 0)));
    }

    [Theory]
    [InlineData("uz-Latn-UZ", "103,1 MB")]
    [InlineData("en-US", "103.1 MB")]
    public void The_real_asset_size_is_shown_in_megabytes(string culture, string expected)
    {
        // Hajm tasdiqlash oynasida ko'rsatiladi: 108 142 493 bayt — bu 103,1 MB.
        // Alohida oqimda bajaramiz, aks holda til sozlamasi parallel sinovlarga ta'sir qilardi.
        var text = InCulture(culture, () =>
            UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 0, 0))!.SizeText);

        Assert.Equal(expected, text);
    }

    // =================================================================================
    //  Kesh va qo'lda tekshirish
    // =================================================================================

    [Fact]
    public async Task A_repeated_check_reuses_the_cached_answer()
    {
        // GitHub API so'rovlari soatiga cheklangan — qobiq va "Dastur haqida" sahifasi
        // birgalikda bitta so'rov yuborishi kerak.
        var calls = 0;
        var service = new UpdateService(_ =>
        {
            calls++;
            return Task.FromResult<string?>(Release("v9.9.0", "YordamchiSetup-9.9.0.exe"));
        });

        var first = await service.CheckForUpdateAsync();
        var second = await service.CheckForUpdateAsync();

        Assert.Equal(1, calls);
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task A_forced_check_asks_github_again()
    {
        // Dastur ochiq turganda yangi reliz chiqishi mumkin. Kesh chetlab o'tilmasa,
        // "Tekshirish" tugmasi foydalanuvchi uchun umuman ishlamas edi.
        var responses = new Queue<string>(
        [
            Release("v1.0.0", "YordamchiSetup-1.0.0.exe"),
            Release("v9.9.0", "YordamchiSetup-9.9.0.exe")
        ]);

        var service = new UpdateService(_ => Task.FromResult<string?>(responses.Dequeue()));

        Assert.Null(await service.CheckForUpdateAsync());

        var forced = await service.CheckForUpdateAsync(force: true);

        Assert.NotNull(forced);
        Assert.Equal("9.9.0", forced.VersionText);
        Assert.Empty(responses);
    }

    [Fact]
    public async Task A_forced_check_replaces_the_cached_answer()
    {
        // Qo'lda topilgan yangilanish keshga tushishi shart: aks holda yon paneldagi
        // bildirishnoma keyingi jimgina tekshiruvda yana yo'qolib qolardi.
        var responses = new Queue<string>(
        [
            Release("v1.0.0", "YordamchiSetup-1.0.0.exe"),
            Release("v9.9.0", "YordamchiSetup-9.9.0.exe")
        ]);

        var calls = 0;
        var service = new UpdateService(_ =>
        {
            calls++;
            return Task.FromResult<string?>(responses.Dequeue());
        });

        await service.CheckForUpdateAsync();
        var forced = await service.CheckForUpdateAsync(force: true);
        var afterwards = await service.CheckForUpdateAsync();

        Assert.Equal(2, calls);
        Assert.Same(forced, afterwards);
    }

    [Fact]
    public async Task A_failed_check_is_never_cached()
    {
        // Internet tiklangach keyingi urinish ishlashi kerak.
        var calls = 0;
        var service = new UpdateService(_ =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<string?>(
                    new PdfServiceException(PdfErrorKind.OperationFailed, "Internet yo'q"))
                : Task.FromResult<string?>(Release("v9.9.0", "YordamchiSetup-9.9.0.exe"));
        });

        await Assert.ThrowsAsync<PdfServiceException>(() => service.CheckForUpdateAsync());

        var update = await service.CheckForUpdateAsync();

        Assert.Equal(2, calls);
        Assert.NotNull(update);
    }

    [Fact]
    public async Task The_real_response_travels_through_the_service_unchanged()
    {
        // Butun zanjir: manba → ParseRelease → UpdateInfo. Tarmoqqa chiqilmaydi.
        var service = new UpdateService(_ => Task.FromResult<string?>(GitHubReleaseSample.Json));

        var update = await service.CheckForUpdateAsync();

        // Yig'ilma versiyasi relizga teng (2.1.0) bo'lsa taklif chiqmaydi — bu ham to'g'ri natija.
        if (service.CurrentVersion >= new Version(2, 1, 0))
        {
            Assert.Null(update);
            return;
        }

        Assert.NotNull(update);
        Assert.Equal(GitHubReleaseSample.SetupAssetName, update.AssetName);
    }

    [Fact]
    public async Task A_repository_without_releases_is_not_an_error()
    {
        // GitHub relizi bo'lmagan repozitoriyaga 404 qaytaradi. Bu nosozlik emas —
        // "yangilanish yo'q" degani, ya'ni foydalanuvchiga qizil xato chiqmasligi kerak.
        var service = new UpdateService(_ => Task.FromResult<string?>(null));

        var update = await service.CheckForUpdateAsync();

        Assert.Null(update);
    }

    [Fact]
    public async Task A_missing_release_answer_is_cached_like_any_other()
    {
        // 404 ham to'liq javob: uni har safar qayta so'rash API cheklovini behuda yeydi.
        var calls = 0;
        var service = new UpdateService(_ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });

        await service.CheckForUpdateAsync();
        await service.CheckForUpdateAsync();

        Assert.Equal(1, calls);
    }

    // =================================================================================
    //  Xizmatning oddiy xossalari
    // =================================================================================

    [Fact]
    public void The_service_reports_the_running_version_and_the_releases_page()
    {
        var service = new UpdateService();

        Assert.True(service.CurrentVersion.Major >= 2);
        Assert.Equal("https://github.com/AbduxalilVoxidjonov/Yordamchi/releases", service.ReleasesPageUrl);
    }

    [Fact]
    public void The_renamed_repository_url_is_still_trusted()
    {
        // Repozitoriy qayta nomlangandan keyin havolaning yo'l qismi o'zgardi. Xost tekshiruvi
        // yo'lga emas, faqat xostga qaraydi — shuning uchun yangi manzil ham o'tishi kerak.
        Assert.True(UpdateService.IsTrustedDownloadUrl(GitHubReleaseSample.SetupDownloadUrl));
        Assert.Contains("/AbduxalilVoxidjonov/Yordamchi/", GitHubReleaseSample.SetupDownloadUrl, StringComparison.Ordinal);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>
    /// Berilgan tilda bajaradi. Alohida oqim kerak: <see cref="CultureInfo.CurrentCulture"/>
    /// oqimga tegishli, sinovlar esa parallel ishlaydi — uni joyida almashtirish qo'shni
    /// sinovni tasodifan yiqitardi.
    /// </summary>
    private static T InCulture<T>(string culture, Func<T> action)
    {
        var result = default(T)!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
            throw failure;

        return result;
    }

    private static string Release(
        string tag,
        string asset = "YordamchiSetup-2.2.0.exe",
        bool draft = false,
        bool prerelease = false)
    {
        var assets = $$"""
        [ { "name": "{{asset}}", "browser_download_url": "https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/{{tag}}/{{asset}}", "size": 123456789 } ]
        """;

        return ReleaseWithAssets(tag, assets, draft, prerelease);
    }

    private static string ReleaseWithAssets(
        string tag,
        string assetsJson,
        bool draft = false,
        bool prerelease = false) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Yordamchi {{tag}}",
          "body": "Izohlar",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-08-01T10:20:30Z",
          "assets": {{assetsJson}}
        }
        """;
}
