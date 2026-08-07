using System.IO;
using SkiaSharp;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="OnnxBackgroundRemover"/> ning <b>model faylini topish</b> mantiqi.
/// <para>
/// Hech qanday ONNX sessiyasi ochilmaydi va 168 MB lik model yuklab olinmaydi: sinovlar
/// modelning "bor/yo'q" holatini va yuklab olishdan oldingi qarorlarni tekshiradi.
/// Soxta model fayli faqat dastur papkasidagi <c>Models</c> jildiga qo'yiladi va sinov
/// tugagach o'chiriladi — foydalanuvchining <c>%LOCALAPPDATA%</c> papkasiga tegilmaydi.
/// </para>
/// </summary>
[Collection(ExternalComponentCollection.Name)]
public sealed class OnnxBackgroundRemoverTests : IDisposable
{
    /// <summary>Model birinchi navbatda shu papkadan qidiriladi (sinovda — test bin papkasi).</summary>
    private static readonly string AppModelsFolder = Path.Combine(AppContext.BaseDirectory, "Models");

    /// <summary>Model topilmasa ko'rsatiladigan, foydalanuvchi yozish huquqiga ega papka.</summary>
    private static readonly string UserModelsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yordamchi",
        "Models");

    private readonly TempWorkspace _temp = new();
    private readonly List<string> _placedFiles = [];
    private bool _createdModelsFolder;

    public void Dispose()
    {
        foreach (var file in _placedFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fayl ushlab turilgan bo'lsa ham sinov natijasiga ta'sir qilmasin.
            }
        }

        if (_createdModelsFolder)
        {
            try
            {
                if (Directory.Exists(AppModelsFolder) && Directory.GetFileSystemEntries(AppModelsFolder).Length == 0)
                    Directory.Delete(AppModelsFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _temp.Dispose();
    }

    // =================================================================================
    //  Model faylini topish
    // =================================================================================

    [Fact]
    public void ModelPath_prefers_the_model_next_to_the_application()
    {
        // Dastur bilan birga tarqatilgan model foydalanuvchi papkasidagisidan ustun turadi.
        var placed = PlaceModel("u2net.onnx");

        using var remover = new OnnxBackgroundRemover();

        Assert.Equal(placed, remover.ModelPath);
        Assert.True(remover.IsModelAvailable);
    }

    [Fact]
    public void ModelPath_accepts_the_lighter_u2netp_model()
    {
        // u2netp — sekin internetli foydalanuvchi qo'lda joylashtiradigan yengil variant.
        var placed = PlaceModel("u2netp.onnx");

        using var remover = new OnnxBackgroundRemover();

        Assert.Equal(placed, remover.ModelPath);
    }

    [Fact]
    public void ModelPath_prefers_the_accurate_model_over_the_light_one()
    {
        // Ikkalasi ham bo'lsa aniqrog'i tanlanadi — sochlar kabi nozik chekkalar uchun muhim.
        var accurate = PlaceModel("u2net.onnx");
        PlaceModel("u2netp.onnx");

        using var remover = new OnnxBackgroundRemover();

        Assert.Equal(accurate, remover.ModelPath);
    }

    [Fact]
    public void A_model_that_appears_later_is_found_without_a_new_service()
    {
        // Foydalanuvchi modelni dastur ishlab turganda joylashtirishi mumkin: yo'l har
        // chaqiruvda qaytadan qidiriladi, shuning uchun dasturni qayta ishga tushirish shart emas.
        using var remover = new OnnxBackgroundRemover();
        var before = remover.ModelPath;

        var placed = PlaceModel("u2net.onnx");

        Assert.NotEqual(before, remover.ModelPath);
        Assert.Equal(placed, remover.ModelPath);
        Assert.True(remover.IsModelAvailable);
    }

    [Fact]
    public void IsModelAvailable_always_matches_the_file_at_ModelPath()
    {
        // UI "model bor" deb hisoblab tugmani ochsa-yu, ModelPath boshqa faylni ko'rsatsa —
        // foydalanuvchi tushunarsiz xatoga duch kelardi.
        using var remover = new OnnxBackgroundRemover();

        Assert.Equal(File.Exists(remover.ModelPath), remover.IsModelAvailable);
    }

    [Fact]
    public void ModelPath_points_at_the_user_folder_when_no_model_is_installed()
    {
        // Model topilmasa xato xabarida aynan yozish huquqi bor papka ko'rsatilishi kerak,
        // aks holda foydalanuvchi faylni Program Files ichiga qo'yishga urinardi.
        using var remover = new OnnxBackgroundRemover();
        var path = remover.ModelPath;
        var expected = Path.Combine(UserModelsFolder, "u2net.onnx");

        Assert.True(
            File.Exists(path) || string.Equals(path, expected, StringComparison.OrdinalIgnoreCase),
            $"Model yo'q bo'lsa ModelPath '{expected}' bo'lishi kerak edi, lekin '{path}' qaytdi.");
    }

    // =================================================================================
    //  Yuklab olishdan oldingi qarorlar (internetga chiqmaydigan holatlar)
    // =================================================================================

    [Fact]
    public async Task DownloadModelAsync_returns_the_existing_model_without_rewriting_it()
    {
        // 168 MB ni qayta tortib olish — foydalanuvchi trafigini behuda sarflash. Mavjud
        // fayl esa hech qanday holatda ustidan yozilmasligi kerak.
        var placed = PlaceModel("u2net.onnx");
        var content = File.ReadAllText(placed);

        using var remover = new OnnxBackgroundRemover();

        Assert.Equal(placed, await remover.DownloadModelAsync());
        Assert.Equal(content, File.ReadAllText(placed));
        Assert.Single(Directory.GetFiles(AppModelsFolder, "*.onnx"));
    }

    [Fact]
    public void The_download_offer_names_the_model_and_its_size()
    {
        // Tasdiqlash oynasida "nima yuklanadi va qancha joy oladi" ko'rinishi shart.
        using var remover = new OnnxBackgroundRemover();

        Assert.Equal("u2net.onnx", remover.DownloadableModelName);
        Assert.False(string.IsNullOrWhiteSpace(remover.DownloadableModelSizeText));
        Assert.Contains("MB", remover.DownloadableModelSizeText);
    }

    [Fact]
    public async Task A_disposed_remover_refuses_to_download()
    {
        var remover = new OnnxBackgroundRemover();
        remover.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => remover.DownloadModelAsync());
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        // Sahifa yopilganda ham, konteyner tugaganda ham Dispose chaqirilishi mumkin.
        var remover = new OnnxBackgroundRemover();

        remover.Dispose();
        remover.Dispose();
    }

    // =================================================================================
    //  Kirish ma'lumotlarini tekshirish (model umuman yuklanmaydi)
    // =================================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_source_path_is_rejected_before_any_work_starts(string path)
    {
        using var remover = new OnnxBackgroundRemover();

        // Xato Task ichida emas, darhol tashlanadi — shuning uchun oddiy Assert.Throws.
        var error = Assert.Throws<PdfServiceException>(
            () => { _ = remover.RemoveBackgroundToBitmapAsync(path); });

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task A_missing_image_file_is_reported_before_the_model_is_touched()
    {
        // Rasm yo'qligi model yo'qligidan oldin tekshiriladi — foydalanuvchi "AI modeli
        // topilmadi" degan chalg'ituvchi xabarni ko'rmasligi kerak.
        using var remover = new OnnxBackgroundRemover();

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => remover.RemoveBackgroundToBitmapAsync(_temp.At("yo-q.png")));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public void Saving_without_an_output_path_is_rejected()
    {
        using var remover = new OnnxBackgroundRemover();
        using var bitmap = new SKBitmap(1, 1);

        var error = Assert.Throws<PdfServiceException>(() => { _ = remover.SaveAsPngAsync(bitmap, "  "); });

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>
    /// Dastur papkasidagi <c>Models</c> jildiga soxta model fayli qo'yadi. Haqiqiy model
    /// mavjud bo'lsa sinov uni buzmasligi uchun to'xtaydi.
    /// </summary>
    private string PlaceModel(string fileName)
    {
        var path = Path.Combine(AppModelsFolder, fileName);
        Assert.False(File.Exists(path), $"'{path}' oldindan mavjud — sinov uni almashtirmasligi kerak.");

        if (!Directory.Exists(AppModelsFolder))
        {
            Directory.CreateDirectory(AppModelsFolder);
            _createdModelsFolder = true;
        }

        File.WriteAllText(path, "soxta model — hech qachon ONNX ga uzatilmaydi");
        _placedFiles.Add(path);
        return path;
    }
}
