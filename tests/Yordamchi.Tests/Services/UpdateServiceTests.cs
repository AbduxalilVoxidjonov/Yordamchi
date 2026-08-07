using Yordamchi.Services;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="UpdateService"/> ning tarmoqsiz qismlari: GitHub javobini o'qish va qayta ishga
/// tushirish skriptini qurish.
/// <para>
/// Bu ikki metod ataylab statik va holatsiz qilingan, chunki aynan ular xavfsizlik chegarasida
/// turadi: yuklab olinadigan fayl keyinchalik <b>administrator huquqi bilan</b> ishga tushadi.
/// Shuning uchun bu yerda "yaxshi holat" emas, asosan <b>rad etilishi shart</b> bo'lgan holatlar
/// sinaladi. Hech bir sinov internetga chiqmaydi.
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
        // Hajmsiz aktivni yuklab olgandan keyin solishtirishga narsa qolmaydi,
        // ya'ni yaxlitlik tekshiruvi butunlay yo'qoladi.
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
    [InlineData("https://github.com/AbduxalilVoxidjonov/PdfEditor/releases/download/v2.2.0/YordamchiSetup-2.2.0.exe")]
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
    //  Qayta ishga tushirish skripti
    // =================================================================================

    [Fact]
    public void The_restart_script_waits_for_the_given_process()
    {
        var script = UpdateService.BuildRestartScript(4321, @"C:\Updates\YordamchiSetup-2.2.0.exe", @"C:\Program Files\Yordamchi\Yordamchi.exe");

        Assert.Contains("\"PID eq 4321\"", script, StringComparison.Ordinal);
        Assert.Contains(":wait", script, StringComparison.Ordinal);
        Assert.Contains("goto wait", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_restart_script_runs_the_installer_quietly_and_reopens_the_app()
    {
        var script = UpdateService.BuildRestartScript(
            10,
            @"C:\Users\Ali Vali\AppData\Local\Yordamchi\Updates\YordamchiSetup-2.2.0.exe",
            @"C:\Program Files\Yordamchi\Yordamchi.exe");

        // Bo'shliqli yo'llar qo'shtirnoqsiz qolsa cmd ularni ikkita argumentga bo'lib yuboradi.
        Assert.Contains(
            "start \"\" /wait \"C:\\Users\\Ali Vali\\AppData\\Local\\Yordamchi\\Updates\\YordamchiSetup-2.2.0.exe\" /passive /norestart",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "start \"\" \"C:\\Program Files\\Yordamchi\\Yordamchi.exe\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_restart_script_starts_the_installer_before_reopening_the_app()
    {
        var script = UpdateService.BuildRestartScript(10, @"C:\a\Setup.exe", @"C:\b\Yordamchi.exe");

        var installerAt = script.IndexOf("/passive", StringComparison.Ordinal);
        var restartAt = script.IndexOf(@"""C:\b\Yordamchi.exe""", StringComparison.Ordinal);

        Assert.True(installerAt > 0);
        Assert.True(restartAt > installerAt);
    }

    [Fact]
    public void The_restart_script_avoids_timeout_because_it_runs_without_a_console()
    {
        // "timeout" stdin ni talab qiladi va oynasiz ishga tushirilganda darhol xato beradi.
        var script = UpdateService.BuildRestartScript(10, @"C:\a\Setup.exe", @"C:\b\Yordamchi.exe");

        Assert.DoesNotContain("timeout ", script, StringComparison.Ordinal);
        Assert.Contains("ping -n", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", @"C:\b\Yordamchi.exe")]
    [InlineData(@"C:\a\Setup.exe", "   ")]
    public void The_restart_script_refuses_empty_paths(string installerPath, string applicationPath) =>
        Assert.Throws<ArgumentException>(
            () => UpdateService.BuildRestartScript(10, installerPath, applicationPath));

    // =================================================================================
    //  Xizmatning oddiy xossalari
    // =================================================================================

    [Fact]
    public void The_service_reports_the_running_version_and_the_releases_page()
    {
        var service = new UpdateService();

        Assert.True(service.CurrentVersion.Major >= 2);
        Assert.Equal("https://github.com/AbduxalilVoxidjonov/PdfEditor/releases", service.ReleasesPageUrl);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private static string Release(
        string tag,
        string asset = "YordamchiSetup-2.2.0.exe",
        bool draft = false,
        bool prerelease = false)
    {
        var assets = $$"""
        [ { "name": "{{asset}}", "browser_download_url": "https://github.com/AbduxalilVoxidjonov/PdfEditor/releases/download/{{tag}}/{{asset}}", "size": 123456789 } ]
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
