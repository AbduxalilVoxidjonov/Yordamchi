using System.IO;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="OcrService"/> ning <b>til fayllarini boshqaradigan</b> qismi: tessdata papkasini
/// topish, o'rnatilgan tillarni sanash va yuklab olishdan oldingi tekshiruvlar.
/// <para>
/// Bu yerda Tesseract dvigateli umuman ishga tushmaydi va internetga chiqilmaydi — sinovlar
/// faqat qaror qabul qilish mantiqini tekshiradi. Papka <c>TESSDATA_PREFIX</c> orqali
/// vaqtinchalik jildga yo'naltiriladi, shuning uchun foydalanuvchining haqiqiy
/// <c>%LOCALAPPDATA%\Yordamchi\tessdata</c> papkasiga hech narsa yozilmaydi.
/// </para>
/// </summary>
[Collection(ExternalComponentCollection.Name)]
public sealed class OcrServiceTests : IDisposable
{
    private const string PrefixVariable = "TESSDATA_PREFIX";

    private readonly TempWorkspace _temp = new();

    /// <summary>Sinovdan oldingi qiymat — sinov muhitni o'zgartirilgan holda qoldirmasligi kerak.</summary>
    private readonly string? _previousPrefix = Environment.GetEnvironmentVariable(PrefixVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PrefixVariable, _previousPrefix);
        _temp.Dispose();
    }

    // =================================================================================
    //  Til fayllari papkasini topish
    // =================================================================================

    [Fact]
    public void TessDataPath_uses_the_folder_from_the_environment_variable()
    {
        // Tesseract o'rnatilgan kompyuterda til fayllari dastur papkasida emas, tizimdagi
        // umumiy joyda turadi — o'sha nusxani qayta yuklab olish shart emas.
        var folder = TessDataFolder("tizim", "eng");

        using var ocr = ServiceFor(folder);

        Assert.Equal(folder, ocr.TessDataPath);
    }

    [Fact]
    public void TessDataPath_looks_inside_the_tessdata_subfolder_of_the_variable()
    {
        // TESSDATA_PREFIX ba'zi o'rnatuvchilarda tessdata ning o'ziga, ba'zilarida uning
        // otasiga ishora qiladi. Ikkala yozuv ham qabul qilinishi kerak.
        var parent = _temp.CreateFolder("ota");
        var nested = TessDataFolder(Path.Combine("ota", "tessdata"), "uzb");

        using var ocr = ServiceFor(parent);

        Assert.Equal(nested, ocr.TessDataPath);
    }

    [Fact]
    public void TessDataPath_ignores_quotes_and_spaces_around_the_variable()
    {
        // Muhit o'zgaruvchisi ko'pincha qo'lda yoziladi: qo'shtirnoq yoki ortiqcha bo'shliq
        // tufayli papka topilmay qolsa, foydalanuvchi sababini tushunmaydi.
        var folder = TessDataFolder("qo-shtirnoqli", "eng");

        using var ocr = ServiceFor($"  \"{folder}\"  ");

        Assert.Equal(folder, ocr.TessDataPath);
    }

    // =================================================================================
    //  O'rnatilgan tillar ro'yxati
    // =================================================================================

    [Fact]
    public void GetInstalledLanguages_lists_the_codes_of_the_files_in_the_folder()
    {
        var folder = TessDataFolder("tessdata", "uzb", "eng", "rus");
        _temp.WriteFile(Path.Combine("tessdata", "o-qilmasin.txt"), "begona fayl");

        using var ocr = ServiceFor(folder);

        // Kengaytmasiz, alifbo tartibida — ro'yxat foydalanuvchiga shu holda ko'rsatiladi.
        Assert.Equal(["eng", "rus", "uzb"], ocr.GetInstalledLanguages());
    }

    [Fact]
    public void GetInstalledLanguages_returns_an_empty_list_when_the_folder_is_gone()
    {
        // Papka o'chirilgan bo'lsa dastur yiqilmasligi kerak: "hech qanday til yo'q" —
        // to'liq qonuniy holat va foydalanuvchiga yuklab olish taklif qilinadi.
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);
        Assert.NotEmpty(ocr.GetInstalledLanguages());

        Directory.Delete(folder, recursive: true);

        Assert.Empty(ocr.GetInstalledLanguages());
    }

    // =================================================================================
    //  Til ifodasini tekshirish
    // =================================================================================

    [Fact]
    public void AreLanguagesInstalled_splits_a_combined_expression_and_names_what_is_missing()
    {
        // "uzb+eng+rus" — standart tanlov. Yetishmayotgan kod aynan nomi bilan qaytishi kerak,
        // chunki ogohlantirish matnida va yuklab olish so'rovida shu ro'yxat ishlatiladi.
        var folder = TessDataFolder("tessdata", "uzb", "eng");

        using var ocr = ServiceFor(folder);

        Assert.False(ocr.AreLanguagesInstalled("uzb+eng+rus", out var missing));
        Assert.Equal(["rus"], missing);
    }

    [Fact]
    public void AreLanguagesInstalled_tolerates_spaces_around_the_codes()
    {
        // Til ifodasi sozlamalardan yoki qo'lda kelishi mumkin — " uzb + eng " ham amal qiladi.
        var folder = TessDataFolder("tessdata", "uzb", "eng");

        using var ocr = ServiceFor(folder);

        Assert.True(ocr.AreLanguagesInstalled(" uzb + eng ", out var missing));
        Assert.Empty(missing);
    }

    [Fact]
    public void AreLanguagesInstalled_ignores_the_letter_case()
    {
        // Fayl nomlari kichik harfda, lekin ifoda katta harf bilan kelsa ham til "yo'q"
        // deb hisoblanmasligi kerak — aks holda mavjud fayl ustiga qayta yuklab olinardi.
        var folder = TessDataFolder("tessdata", "uzb", "eng");

        using var ocr = ServiceFor(folder);

        Assert.True(ocr.AreLanguagesInstalled("UZB+Eng", out var missing));
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("++")]
    public void AreLanguagesInstalled_checks_the_default_languages_when_the_request_is_empty(string? language)
    {
        // Bo'sh ifoda bajarish paytida DefaultLanguage (uzb+eng+rus) ga aylanadi. Agar
        // oldindan tekshiruv bunda "hammasi tayyor" desa, ogohlantirish paneli jim turib,
        // amal esa "komponent topilmadi" bilan yiqilardi — tekshiruv va bajarish bir xil
        // savolga bir xil javob berishi shart.
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);

        Assert.False(ocr.AreLanguagesInstalled(language!, out var missing));
        Assert.Equal(["rus", "uzb"], missing.Order().ToArray());
    }

    [Fact]
    public void AreLanguagesInstalled_is_satisfied_when_every_default_language_is_present()
    {
        var folder = TessDataFolder("tessdata", "uzb", "eng", "rus");

        using var ocr = ServiceFor(folder);

        Assert.True(ocr.AreLanguagesInstalled(string.Empty, out var missing));
        Assert.Empty(missing);
    }

    [Fact]
    public void AreLanguagesInstalled_reports_a_repeated_code_only_once()
    {
        // "rus+rus" ifodasi xato xabarida rus tilini ikki marta ko'rsatmasligi kerak.
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);

        Assert.False(ocr.AreLanguagesInstalled("rus+RUS", out var missing));
        Assert.Equal(["rus"], missing);
    }

    // =================================================================================
    //  Yuklab olishdan oldingi tekshiruv (internetga chiqmaydigan holatlar)
    // =================================================================================

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("eng/rus")]
    [InlineData("uz b")]
    [InlineData("eng.traineddata")]
    public async Task DownloadLanguagesAsync_rejects_a_code_that_could_escape_the_folder(string code)
    {
        // Til kodi to'g'ridan-to'g'ri fayl nomiga va URL ga qo'shiladi. Tekshiruvsiz bu
        // papkadan chiqib ketadigan yo'l yozishga imkon berardi — shuning uchun tarmoqqa
        // chiqishdan oldin rad etiladi.
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => ocr.DownloadLanguagesAsync([code]));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
        Assert.Contains("Til kodi noto'g'ri", error.Message);

        // Papkaga hech narsa yozilmadi — ya'ni yuklab olish umuman boshlanmadi.
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task DownloadLanguagesAsync_rejects_a_code_that_is_too_long()
    {
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => ocr.DownloadLanguagesAsync([new string('a', 33)]));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task DownloadLanguagesAsync_does_nothing_when_no_language_was_asked_for()
    {
        // Bo'sh so'rov xato emas: chaqiruvchi "hammasi tayyor" javobini olishi kerak.
        var folder = TessDataFolder("tessdata", "eng");

        using var ocr = ServiceFor(folder);

        Assert.Empty(await ocr.DownloadLanguagesAsync([]));
        Assert.Empty(await ocr.DownloadLanguagesAsync(["", "   "]));
    }

    [Fact]
    public async Task DownloadLanguagesAsync_keeps_an_already_installed_file_untouched()
    {
        // Til fayli bor bo'lsa qayta yuklab olinmaydi (aks holda har chaqiruvda megabaytlar
        // behuda sarflanardi), lekin natijaga baribir qo'shiladi — chaqiruvchi uchun bu
        // "shu til endi tayyor" degani.
        var folder = TessDataFolder("tessdata", "eng");
        var file = Path.Combine(folder, "eng.traineddata");
        var before = File.ReadAllText(file);

        using var ocr = ServiceFor(folder);
        var reports = new List<PdfProgress>();

        var result = await ocr.DownloadLanguagesAsync(
            ["eng"],
            new Progress<PdfProgress>(reports.Add));

        Assert.Equal(["eng"], result);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task DownloadLanguagesAsync_collapses_duplicate_entries()
    {
        // Ro'yxat "uzb+eng" ifodasi va alohida "eng" dan yig'ilgan bo'lishi mumkin —
        // bitta til ikki marta qayta ishlanmasligi kerak.
        var folder = TessDataFolder("tessdata", "uzb", "eng");

        using var ocr = ServiceFor(folder);

        Assert.Equal(["uzb", "eng"], await ocr.DownloadLanguagesAsync(["uzb+eng", "ENG"]));
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>Berilgan til fayllari yotgan vaqtinchalik tessdata papkasini yaratadi.</summary>
    private string TessDataFolder(string relative, params string[] languages)
    {
        var folder = _temp.CreateFolder(relative);

        foreach (var code in languages)
            _temp.WriteFile(Path.Combine(relative, code + ".traineddata"), "soxta til fayli");

        return folder;
    }

    /// <summary>Papkani <c>TESSDATA_PREFIX</c> orqali ko'rsatgan holda servis yaratadi.</summary>
    private static OcrService ServiceFor(string prefix)
    {
        Environment.SetEnvironmentVariable(PrefixVariable, prefix);
        return new OcrService();
    }
}
