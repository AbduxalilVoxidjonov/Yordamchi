using NSubstitute;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="PdfEngineService"/> ning <b>qaror qabul qiladigan</b> qismi: <c>Validate</c>,
/// <c>CheckPrerequisites</c> va <c>GetMissingComponent</c>.
/// <para>
/// Bu yerda hech qanday PDF ochilmaydi — sub-servislar o'rniga substitute'lar turadi.
/// Sinovlarning maqsadi: "Bajarish" tugmasi bosilishidan <i>oldin</i> foydalanuvchiga
/// to'g'ri xabar chiqishi va ogohlantirish yonidagi "Yuklab olish" tugmasi aynan kerakli
/// holatlarda ko'rinishi.
/// </para>
/// </summary>
public sealed class PdfEngineServiceTests : IDisposable
{
    private readonly TempWorkspace _temp = new();

    private readonly IPdfService _pages = Substitute.For<IPdfService>();
    private readonly IPdfManipulatorService _documents = Substitute.For<IPdfManipulatorService>();
    private readonly IDocumentConversionService _conversion = Substitute.For<IDocumentConversionService>();
    private readonly IOcrService _ocr = Substitute.For<IOcrService>();
    private readonly IImageBackgroundRemover _remover = Substitute.For<IImageBackgroundRemover>();

    private readonly PdfEngineService _engine;

    public PdfEngineServiceTests()
    {
        // Standart holat: hamma tashqi komponent joyida. Har bir sinov faqat o'ziga
        // kerakli shartni buzadi — shu tufayli sinov nimani tekshirayotgani ko'rinib turadi.
        AllLanguagesInstalled();
        _remover.IsModelAvailable.Returns(true);
        _remover.DownloadableModelName.Returns("u2net.onnx");
        _remover.DownloadableModelSizeText.Returns("~168 MB");
        _conversion.IsMicrosoftWordAvailable.Returns(true);

        _engine = new PdfEngineService(_pages, _documents, _conversion, _ocr, _remover);
    }

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Validate — so'rovning o'zi
    // =================================================================================

    [Fact]
    public void Validate_rejects_a_request_without_files()
    {
        var request = new ToolRequest { Tool = ToolId.Compress, InputFiles = [] };

        Assert.Equal("Avval fayl tanlang.", _engine.Validate(request));
    }

    [Fact]
    public void Validate_rejects_a_file_that_no_longer_exists()
    {
        // Foydalanuvchi faylni tanlagandan keyin uni o'chirib yuborishi mumkin.
        var request = Request(ToolId.Compress, [_temp.At("yo-q.pdf")], output: _temp.At("natija.pdf"));

        Assert.Contains("topilmadi", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_an_output_file_for_single_file_tools()
    {
        var request = Request(ToolId.Compress, [ExistingPdf()], output: null);

        Assert.Equal("Natija fayli uchun joy tanlang.", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_an_output_folder_for_tools_that_write_many_files()
    {
        // Split va PdfToImage papkaga yozadi; ularda OutputPath emas, OutputFolder kerak.
        var request = new ToolRequest
        {
            Tool = ToolId.Split,
            InputFiles = [ExistingPdf()],
            OutputPath = _temp.At("natija.pdf")
        };

        Assert.Equal("Natija saqlanadigan papkani tanlang.", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_at_least_two_files_to_merge()
    {
        var request = Request(ToolId.Merge, [ExistingPdf()], output: _temp.At("natija.pdf"));

        Assert.Contains("kamida ikkita", _engine.Validate(request));
    }

    [Fact]
    public void Validate_accepts_a_single_file_merge_when_a_page_plan_was_built()
    {
        // Foydalanuvchi eskizlarda sahifalarni qayta tartiblagan bo'lsa, bitta fayldan ham
        // ma'noli natija chiqadi — bu holat bloklanmasligi kerak.
        var request = new ToolRequest
        {
            Tool = ToolId.Merge,
            InputFiles = [ExistingPdf()],
            OutputPath = _temp.At("natija.pdf"),
            PagePlan = [new PageEdit(ExistingPdf(), 0, PageRotation.None)]
        };

        Assert.Null(_engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_a_password_to_unlock()
    {
        var request = Request(ToolId.Unlock, [ExistingPdf()], output: _temp.At("natija.pdf"));

        Assert.Contains("parolini kiriting", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_at_least_one_password_to_protect()
    {
        var request = Request(ToolId.Protect, [ExistingPdf()], output: _temp.At("natija.pdf"),
            options: new ProtectOptions());

        Assert.Contains("Parol kiriting", _engine.Validate(request));
    }

    [Fact]
    public void Validate_accepts_an_owner_only_password_because_that_is_restrictions_only_mode()
    {
        // Ochish paroli bo'sh, egalik paroli bor: hujjat erkin ochiladi, lekin cheklovlar
        // amal qiladi. Bu haqiqiy stsenariy va rad etilmasligi kerak.
        var request = Request(ToolId.Protect, [ExistingPdf()], output: _temp.At("natija.pdf"),
            options: new ProtectOptions { OwnerPassword = "egasi" });

        Assert.Null(_engine.Validate(request));
    }

    [Fact]
    public void Validate_blocks_selected_pages_rotation_when_no_thumbnails_were_loaded()
    {
        // Tanlangan sahifalar eskizlar ro'yxatidan olinadi. U bo'sh bo'lsa jimgina
        // barcha sahifani burib yuborish — ma'lumot yo'qotadigan xatolik bo'lardi.
        var request = Request(ToolId.Rotate, [ExistingPdf()], output: _temp.At("natija.pdf"),
            options: new RotateRequest(90, ApplyToAll: false));

        Assert.Contains("Barcha sahifalarga qo'llansin", _engine.Validate(request));
    }

    [Fact]
    public void Validate_allows_rotate_all_without_thumbnails()
    {
        var request = Request(ToolId.Rotate, [ExistingPdf()], output: _temp.At("natija.pdf"),
            options: new RotateRequest(90, ApplyToAll: true));

        Assert.Null(_engine.Validate(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_requires_a_range_expression_in_range_split_mode(string expression)
    {
        var request = FolderRequest(ToolId.Split,
            new SplitOptions { Mode = SplitMode.Ranges, RangeExpression = expression });

        Assert.Contains("oraliqlarini kiriting", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_at_least_one_page_per_chunk()
    {
        var request = FolderRequest(ToolId.Split,
            new SplitOptions { Mode = SplitMode.FixedChunks, PagesPerFile = 0 });

        Assert.Contains("kamida 1", _engine.Validate(request));
    }

    [Fact]
    public void Validate_requires_watermark_text()
    {
        var request = Request(ToolId.Watermark, [ExistingPdf()], output: _temp.At("natija.pdf"),
            options: new WatermarkOptions { Text = "  " });

        Assert.Contains("Suv belgisi matnini", _engine.Validate(request));
    }

    // =================================================================================
    //  CheckPrerequisites / GetMissingComponent — tashqi komponentlar
    // =================================================================================

    [Fact]
    public void Ocr_tool_reports_the_missing_languages_by_name()
    {
        LanguagesMissing("uzb", "rus");

        var warning = _engine.CheckPrerequisites(ToolId.OcrToWord, OcrOptions.Default);

        Assert.NotNull(warning);
        Assert.Contains("uzb", warning);
        Assert.Contains("rus", warning);
    }

    [Fact]
    public void Ocr_tool_offers_a_download_when_languages_are_missing()
    {
        LanguagesMissing("uzb");

        Assert.Equal(DownloadableComponent.OcrLanguages, _engine.GetMissingComponent(ToolId.OcrToWord, OcrOptions.Default));
    }

    [Fact]
    public void Ocr_tool_is_ready_when_the_languages_are_installed()
    {
        AllLanguagesInstalled();

        Assert.Null(_engine.CheckPrerequisites(ToolId.OcrToWord, OcrOptions.Default));
        Assert.Equal(DownloadableComponent.None, _engine.GetMissingComponent(ToolId.OcrToWord, OcrOptions.Default));
    }

    [Fact]
    public void PdfToWord_needs_no_ocr_in_text_layer_only_mode()
    {
        // Matn qatlami bor hujjatda OCR umuman chaqirilmaydi — til fayllari yo'q bo'lsa ham
        // vosita ishlashi kerak, aks holda foydalanuvchi bekorga 5 MB yuklab olardi.
        LanguagesMissing("uzb");

        var options = new PdfToWordOptions { Recognition = TextRecognitionMode.TextLayerOnly };

        Assert.Null(_engine.CheckPrerequisites(ToolId.PdfToWord, options));
        Assert.Equal(DownloadableComponent.None, _engine.GetMissingComponent(ToolId.PdfToWord, options));
    }

    [Fact]
    public void PdfToWord_needs_ocr_in_automatic_mode()
    {
        LanguagesMissing("uzb");

        var options = new PdfToWordOptions { Recognition = TextRecognitionMode.Automatic };

        Assert.NotNull(_engine.CheckPrerequisites(ToolId.PdfToWord, options));
        Assert.Equal(DownloadableComponent.OcrLanguages, _engine.GetMissingComponent(ToolId.PdfToWord, options));
    }

    [Fact]
    public void Background_remover_reports_the_model_with_its_size_and_path()
    {
        _remover.IsModelAvailable.Returns(false);
        _remover.ModelPath.Returns(@"C:\Users\test\AppData\Local\Yordamchi\Models\u2net.onnx");

        var warning = _engine.CheckPrerequisites(ToolId.BackgroundRemover);

        Assert.NotNull(warning);
        Assert.Contains("~168 MB", warning);
        Assert.Contains("u2net.onnx", warning);
        Assert.Equal(DownloadableComponent.AiModel, _engine.GetMissingComponent(ToolId.BackgroundRemover));
    }

    [Fact]
    public void Background_remover_is_ready_when_the_model_is_present()
    {
        _remover.IsModelAvailable.Returns(true);

        Assert.Null(_engine.CheckPrerequisites(ToolId.BackgroundRemover));
        Assert.Equal(DownloadableComponent.None, _engine.GetMissingComponent(ToolId.BackgroundRemover));
    }

    [Fact]
    public void Missing_microsoft_word_warns_but_offers_no_download()
    {
        // Word — foydalanuvchi o'zi o'rnatadigan tashqi dastur. Ogohlantirish chiqadi,
        // lekin "Yuklab olish" tugmasi ko'rinmasligi kerak.
        _conversion.IsMicrosoftWordAvailable.Returns(false);
        var options = new WordToPdfOptions { Engine = WordToPdfEngine.MicrosoftWord };

        Assert.Contains("Microsoft Word topilmadi", _engine.CheckPrerequisites(ToolId.WordToPdf, options));
        Assert.Equal(DownloadableComponent.None, _engine.GetMissingComponent(ToolId.WordToPdf, options));
    }

    [Fact]
    public void The_internal_engine_never_asks_for_microsoft_word()
    {
        _conversion.IsMicrosoftWordAvailable.Returns(false);

        foreach (var engine in new[] { WordToPdfEngine.Automatic, WordToPdfEngine.Builtin })
            Assert.Null(_engine.CheckPrerequisites(ToolId.WordToPdf, new WordToPdfOptions { Engine = engine }));
    }

    [Fact]
    public void Tools_without_external_dependencies_are_always_ready()
    {
        ToolId[] selfContained = [ToolId.Merge, ToolId.Split, ToolId.Organize, ToolId.Rotate,
            ToolId.Compress, ToolId.Protect, ToolId.Unlock, ToolId.Watermark, ToolId.PageNumbers,
            ToolId.PdfToImage, ToolId.ImageToPdf];

        foreach (var tool in selfContained)
        {
            Assert.Null(_engine.CheckPrerequisites(tool));
            Assert.Equal(DownloadableComponent.None, _engine.GetMissingComponent(tool));
        }
    }

    [Fact]
    public void The_warning_text_and_the_download_button_always_agree()
    {
        // Ikkalasi bitta manbadan kelib chiqishi kerak: tugma bor, lekin ogohlantirish yo'q
        // (yoki aksincha) — foydalanuvchi uchun tushunarsiz holat.
        LanguagesMissing("uzb");
        _remover.IsModelAvailable.Returns(false);

        foreach (var tool in Enum.GetValues<ToolId>())
        {
            var component = _engine.GetMissingComponent(tool);
            var warning = _engine.CheckPrerequisites(tool);

            if (component != DownloadableComponent.None)
                Assert.False(string.IsNullOrWhiteSpace(warning), $"{tool}: tugma bor, matn yo'q");
        }
    }

    // =================================================================================
    //  DownloadComponentAsync — tugma qaysi servisga boradi
    // =================================================================================

    [Fact]
    public async Task Downloading_ocr_languages_passes_the_selected_language_expression()
    {
        await _engine.DownloadComponentAsync(
            DownloadableComponent.OcrLanguages,
            new OcrOptions { Language = "uzb+eng" });

        await _ocr.Received(1).DownloadLanguagesAsync(
            Arg.Is<IEnumerable<string>>(languages => languages.SequenceEqual(new[] { "uzb", "eng" })),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Downloading_ocr_languages_falls_back_to_the_default_set()
    {
        await _engine.DownloadComponentAsync(DownloadableComponent.OcrLanguages);

        await _ocr.Received(1).DownloadLanguagesAsync(
            Arg.Is<IEnumerable<string>>(languages =>
                languages.SequenceEqual(OcrOptions.DefaultLanguage.Split('+'))),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Downloading_the_ai_model_goes_to_the_background_remover()
    {
        await _engine.DownloadComponentAsync(DownloadableComponent.AiModel);

        await _remover.Received(1).DownloadModelAsync(
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Downloading_nothing_touches_no_service()
    {
        await _engine.DownloadComponentAsync(DownloadableComponent.None);

        await _ocr.DidNotReceiveWithAnyArgs().DownloadLanguagesAsync(default!, default, default);
        await _remover.DidNotReceiveWithAnyArgs().DownloadModelAsync(default, default);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private string ExistingPdf() => _temp.WriteFile("manba.pdf", "%PDF-1.4 soxta");

    private ToolRequest Request(ToolId tool, IReadOnlyList<string> files, string? output, object? options = null) =>
        new() { Tool = tool, InputFiles = files, OutputPath = output, Options = options };

    private ToolRequest FolderRequest(ToolId tool, object? options) =>
        new()
        {
            Tool = tool,
            InputFiles = [ExistingPdf()],
            OutputFolder = _temp.CreateFolder("natija"),
            Options = options
        };

    private void AllLanguagesInstalled() =>
        _ocr.AreLanguagesInstalled(Arg.Any<string>(), out Arg.Any<IReadOnlyList<string>>())
            .Returns(call =>
            {
                call[1] = Array.Empty<string>();
                return true;
            });

    private void LanguagesMissing(params string[] missing) =>
        _ocr.AreLanguagesInstalled(Arg.Any<string>(), out Arg.Any<IReadOnlyList<string>>())
            .Returns(call =>
            {
                call[1] = missing;
                return false;
            });
}
