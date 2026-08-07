using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="AboutViewModel"/> sinovlari. Bu sahifa foydalanuvchiga ixtiyoriy komponentlar
/// (OCR til fayllari, AI modeli, Microsoft Word) holatini <b>oldindan</b> ko'rsatadi va
/// ularni yuklab olishni taklif qiladi.
/// <para>
/// Servislar o'rniga substitute'lar turadi — hech qanday fayl yuklanmaydi. Tekshirilayotgani:
/// tasdiqsiz yuklab olish boshlanmasligi, yuklab olingandan keyin holat yangilanishi va
/// xatodan keyin sahifa band bo'lib qolmasligi.
/// </para>
/// </summary>
public sealed class AboutViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();

    private readonly IPdfEngineService _engine = Substitute.For<IPdfEngineService>();
    private readonly IOcrService _ocr = Substitute.For<IOcrService>();
    private readonly IImageBackgroundRemover _remover = Substitute.For<IImageBackgroundRemover>();
    private readonly IDocumentConversionService _conversion = Substitute.For<IDocumentConversionService>();
    private readonly FakeDialogService _dialogs = new();

    /// <summary>Holat yuklab olishdan keyin o'zgarishini ko'rsatish uchun o'zgaruvchan javoblar.</summary>
    private IReadOnlyList<string> _installedLanguages = [];
    private bool _modelAvailable;

    public AboutViewModelTests()
    {
        _ocr.GetInstalledLanguages().Returns(_ => _installedLanguages);
        _ocr.TessDataPath.Returns(_ => _temp.At("tessdata"));

        _remover.IsModelAvailable.Returns(_ => _modelAvailable);
        _remover.ModelPath.Returns(_ => _temp.At(System.IO.Path.Combine("Models", "u2net.onnx")));
        _remover.DownloadableModelName.Returns("u2net.onnx");
        _remover.DownloadableModelSizeText.Returns("~168 MB");

        _engine.Ocr.Returns(_ocr);
        _engine.BackgroundRemover.Returns(_remover);
        _engine.Conversion.Returns(_conversion);
    }

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Komponentlar holati
    // =================================================================================

    [Fact]
    public void The_page_lists_the_installed_ocr_languages()
    {
        _installedLanguages = ["eng", "uzb"];

        var vm = CreateViewModel();

        Assert.True(vm.IsOcrReady);
        Assert.Contains("eng", vm.OcrStatus);
        Assert.Contains("uzb", vm.OcrStatus);
    }

    [Fact]
    public void An_empty_language_folder_is_shown_as_not_ready()
    {
        // Foydalanuvchi OCR vositasi ishlamay qolgandan keyin emas, shu sahifada bilishi kerak.
        var vm = CreateViewModel();

        Assert.False(vm.IsOcrReady);
        Assert.Contains("yuklab oling", vm.OcrStatus);
    }

    [Fact]
    public void A_missing_ai_model_is_shown_with_its_size_and_the_expected_folder()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsAiReady);
        Assert.Contains("~168 MB", vm.AiStatus);
        Assert.Contains(_remover.ModelPath, vm.AiStatus);
    }

    [Fact]
    public void A_present_ai_model_is_shown_as_ready()
    {
        _modelAvailable = true;

        var vm = CreateViewModel();

        Assert.True(vm.IsAiReady);
        Assert.Contains("Tayyor", vm.AiStatus);
    }

    [Fact]
    public void A_missing_microsoft_word_is_not_presented_as_a_problem()
    {
        // Word ixtiyoriy: usiz ham ichki dvigatel ishlaydi, shuning uchun matn tinchlantiruvchi.
        _conversion.IsMicrosoftWordAvailable.Returns(false);

        var vm = CreateViewModel();

        Assert.False(vm.IsWordReady);
        Assert.Contains("ichki dvigateli", vm.WordStatus);
    }

    [Fact]
    public void Refresh_rereads_the_component_state()
    {
        // Foydalanuvchi fayllarni qo'lda joylashtirgan bo'lishi mumkin — "Yangilash" tugmasi
        // dasturni qayta ishga tushirishning o'rnini bosadi.
        var vm = CreateViewModel();
        Assert.False(vm.IsOcrReady);
        Assert.False(vm.IsAiReady);

        _installedLanguages = ["uzb"];
        _modelAvailable = true;
        vm.RefreshCommand.Execute(null);

        Assert.True(vm.IsOcrReady);
        Assert.True(vm.IsAiReady);
    }

    // =================================================================================
    //  Til fayllarini yuklab olish
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_confirmation_downloads_no_languages()
    {
        // Yuklab olish internet trafigi sarflaydi — foydalanuvchi roziligisiz boshlanmasligi kerak.
        _dialogs.ConfirmResult = false;
        var vm = CreateViewModel();

        await vm.DownloadOcrLanguagesCommand.ExecuteAsync(null);

        await _ocr.DidNotReceiveWithAnyArgs().DownloadLanguagesAsync(default!, default, default);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Downloading_asks_for_the_default_language_set()
    {
        var vm = CreateViewModel();

        await vm.DownloadOcrLanguagesCommand.ExecuteAsync(null);

        Assert.Contains("Til fayllarini yuklab olish", _dialogs.Confirmations);
        await _ocr.Received(1).DownloadLanguagesAsync(
            Arg.Is<IEnumerable<string>>(languages =>
                languages.SequenceEqual(OcrOptions.DefaultLanguage.Split('+'))),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_successful_language_download_updates_the_status_immediately()
    {
        // Yuklab olingandan keyin sahifa hali ham "o'rnatilmagan" deb tursa, foydalanuvchi
        // tugmani qayta-qayta bosardi.
        var vm = CreateViewModel();
        _ocr.DownloadLanguagesAsync(default!, default, default)
            .ReturnsForAnyArgs(_ =>
            {
                IReadOnlyList<string> downloaded = ["eng", "rus", "uzb"];
                _installedLanguages = downloaded;
                return downloaded;
            });

        await vm.DownloadOcrLanguagesCommand.ExecuteAsync(null);

        Assert.True(vm.IsOcrReady);
        Assert.Contains("uzb", vm.OcrStatus);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_language_download_shows_the_error_and_leaves_the_page_usable()
    {
        var vm = CreateViewModel();
        _ocr.DownloadLanguagesAsync(default!, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.MissingComponent, "Internet yo'q"));

        await vm.DownloadOcrLanguagesCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Internet yo'q", _dialogs.ShownErrors[0]);
        Assert.False(vm.IsOcrReady);

        // Sahifa band holatda qolib ketmasligi kerak — aks holda tugma abadiy o'chib qolardi.
        Assert.False(vm.IsBusy);
        Assert.True(vm.DownloadOcrLanguagesCommand.CanExecute(null));
    }

    // =================================================================================
    //  AI modelini yuklab olish
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_confirmation_downloads_no_model()
    {
        _dialogs.ConfirmResult = false;
        var vm = CreateViewModel();

        await vm.DownloadAiModelCommand.ExecuteAsync(null);

        await _remover.DidNotReceiveWithAnyArgs().DownloadModelAsync(default, default);
        Assert.False(vm.IsAiReady);
    }

    [Fact]
    public async Task A_successful_model_download_updates_the_status_immediately()
    {
        var vm = CreateViewModel();
        _remover.DownloadModelAsync(default, default).ReturnsForAnyArgs(_ =>
        {
            _modelAvailable = true;
            return "u2net.onnx";
        });

        await vm.DownloadAiModelCommand.ExecuteAsync(null);

        Assert.Contains("AI modelini yuklab olish", _dialogs.Confirmations);
        Assert.True(vm.IsAiReady);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_model_download_shows_the_error_and_leaves_the_page_usable()
    {
        var vm = CreateViewModel();
        _remover.DownloadModelAsync(default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OutputNotWritable, "Diskda joy yo'q"));

        await vm.DownloadAiModelCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Diskda joy yo'q", _dialogs.ShownErrors[0]);
        Assert.False(vm.IsAiReady);
        Assert.False(vm.IsBusy);
        Assert.True(vm.DownloadAiModelCommand.CanExecute(null));
    }

    // =================================================================================
    //  Papkalarni ochish
    // =================================================================================

    [Fact]
    public void Opening_the_ocr_folder_reveals_the_tessdata_path()
    {
        // Papka hali yo'q bo'lishi mumkin — sahifa uni yaratib, keyin ochishi kerak,
        // aks holda Explorer xato oynasi chiqarardi.
        var vm = CreateViewModel();

        vm.OpenOcrFolderCommand.Execute(null);

        Assert.Equal([_ocr.TessDataPath], _dialogs.RevealedPaths);
        Assert.True(System.IO.Directory.Exists(_ocr.TessDataPath));
    }

    [Fact]
    public void Opening_the_model_folder_reveals_the_folder_not_the_file()
    {
        var vm = CreateViewModel();

        vm.OpenModelFolderCommand.Execute(null);

        Assert.Equal([_temp.At("Models")], _dialogs.RevealedPaths);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private AboutViewModel CreateViewModel() => new(_engine, _dialogs);
}
