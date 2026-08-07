namespace Yordamchi.Tests.TestSupport;

/// <summary>
/// GitHub API ning <c>releases/latest</c> uchqiga bergan <b>haqiqiy</b> javobi.
/// <para>
/// Fayl <c>TestData/github-latest-release.json</c> da yotadi va yig'ilmaga joylanadi
/// (Directory.Build.props). Nusxa qo'lda yozilgan namuna emas, aynan serverdan olingan
/// bayt-ma-bayt javob: <c>UpdateService</c> ning aktiv nomi naqshi yoki JSON maydonlari bir
/// harf bilan farq qilsa, yangilanish <b>jimgina</b> hech qachon ko'rinmaydi — buni faqat
/// haqiqiy javob ustidagi sinov tutadi.
/// </para>
/// <para>
/// Fayldan o'qiladi, tarmoqqa chiqilmaydi.
/// </para>
/// </summary>
public static class GitHubReleaseSample
{
    private const string ResourceName = "Yordamchi.Tests.TestData.github-latest-release.json";

    private static readonly Lazy<string> Cached = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Reliz e'lon qilingan versiya — javobdagi <c>tag_name</c>.</summary>
    public const string TagName = "v2.1.0";

    /// <summary>Javobdagi yagona mos aktiv (yonida <c>.msi</c> ham turadi).</summary>
    public const string SetupAssetName = "YordamchiSetup-2.1.0.exe";

    /// <summary>Mos kelmasligi kerak bo'lgan ikkinchi aktiv.</summary>
    public const string MsiAssetName = "Yordamchi-2.1.0-x64.msi";

    /// <summary>O'rnatgichning to'g'ridan-to'g'ri havolasi.</summary>
    public const string SetupDownloadUrl =
        "https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/v2.1.0/YordamchiSetup-2.1.0.exe";

    /// <summary>O'rnatgich hajmi (bayt) — API aytgan qiymat.</summary>
    public const long SetupSizeBytes = 108_142_493;

    /// <summary>Serverdan olingan javobning to'liq matni.</summary>
    public static string Json => Cached.Value;

    private static string Load()
    {
        using var stream = typeof(GitHubReleaseSample).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"'{ResourceName}' yig'ilmaga joylanmagan — Directory.Build.props tekshirilsin.");

        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}
