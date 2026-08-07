using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SkiaSharp;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="BackgroundRemoverViewModel"/> sinovlari. AI modeli bu yerda ishga tushmaydi —
/// <see cref="IImageBackgroundRemover"/> o'rniga substitute turadi.
/// <para>
/// Tekshirilayotgani sahifaning o'z qoidalari: model yo'q bo'lganda tugmalar bloklanishi,
/// tasdiq bekor qilinganda hech narsa yuklanmasligi, muvaffaqiyatdan keyin sahifa ishga
/// tayyor bo'lishi va xatodan keyin band holatda qolib ketmasligi.
/// </para>
/// </summary>
public sealed class BackgroundRemoverViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly IImageBackgroundRemover _remover = Substitute.For<IImageBackgroundRemover>();
    private readonly IPdfService _pdf = Substitute.For<IPdfService>();
    private readonly FakeDialogService _dialogs = new();
    private readonly BackgroundRemoverViewModel _vm;

    /// <summary>Model holati yuklab olishdan keyin o'zgarishini ko'rsatish uchun.</summary>
    private bool _modelAvailable;

    public BackgroundRemoverViewModelTests()
    {
        _remover.IsModelAvailable.Returns(_ => _modelAvailable);
        _remover.ModelPath.Returns(_ => _temp.At(Path.Combine("Models", "u2net.onnx")));
        _remover.DownloadableModelName.Returns("u2net.onnx");
        _remover.DownloadableModelSizeText.Returns("~168 MB");

        _pdf.RenderImageThumbnailAsync(default!, default, default).ReturnsForAnyArgs(_ => Thumbnail());

        _vm = new BackgroundRemoverViewModel(_remover, _pdf, _dialogs);
    }

    public void Dispose()
    {
        _vm.Dispose();
        _temp.Dispose();
    }

    // =================================================================================
    //  Boshlang'ich holat
    // =================================================================================

    [Fact]
    public void The_page_takes_the_model_state_from_the_service_at_startup()
    {
        // Ogohlantirish paneli aynan shu ikki qiymatga bog'langan.
        Assert.False(_vm.IsModelAvailable);
        Assert.Equal(_remover.ModelPath, _vm.ModelPath);
    }

    [Fact]
    public async Task Removing_the_background_stays_blocked_until_both_the_model_and_an_image_are_ready()
    {
        Assert.False(_vm.RemoveBackgroundCommand.CanExecute(null));

        await LoadImage();
        Assert.False(_vm.RemoveBackgroundCommand.CanExecute(null));

        _vm.IsModelAvailable = true;
        Assert.True(_vm.RemoveBackgroundCommand.CanExecute(null));
    }

    // =================================================================================
    //  Rasm tanlash
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_open_dialog_loads_nothing()
    {
        await _vm.OpenImageCommand.ExecuteAsync(null);

        await _pdf.DidNotReceiveWithAnyArgs().RenderImageThumbnailAsync(default!, default, default);
        Assert.False(_vm.HasSource);
    }

    [Fact]
    public async Task Dropping_a_mixed_selection_takes_the_first_supported_image()
    {
        var image = _temp.WriteFile("rasm.jpg", "soxta");

        await _vm.DropFilesCommand.ExecuteAsync(new[] { _temp.WriteFile("hujjat.txt", "x"), image });

        Assert.Equal(image, _vm.SourcePath);
        Assert.Equal("rasm.jpg", _vm.SourceFileName);
        await _pdf.Received(1).RenderImageThumbnailAsync(image, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dropping_files_without_an_image_only_reports_it()
    {
        // Foydalanuvchi PDF yoki hujjat tashlab yuborishi mumkin — bu xato oynasi emas,
        // shunchaki qisqa izoh bo'lishi kerak.
        await _vm.DropFilesCommand.ExecuteAsync(new[] { _temp.WriteFile("hujjat.txt", "x") });

        Assert.Contains("mos rasm fayli topilmadi", _vm.StatusMessage);
        Assert.False(_vm.HasSource);
        Assert.Empty(_dialogs.ShownErrors);
    }

    [Fact]
    public async Task A_broken_image_shows_the_error_and_keeps_the_page_empty()
    {
        _pdf.RenderImageThumbnailAsync(default!, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.UnsupportedImage, "Format mos emas"));

        _dialogs.OpenFileResults.Enqueue(_temp.WriteFile("rasm.jpg", "soxta"));
        await _vm.OpenImageCommand.ExecuteAsync(null);

        Assert.False(_vm.HasSource);
        Assert.Single(_dialogs.ShownErrors);
        Assert.False(_vm.IsBusy);
    }

    // =================================================================================
    //  Fonni olib tashlash
    // =================================================================================

    [Fact]
    public async Task A_successful_removal_unlocks_saving()
    {
        await PrepareResult();

        Assert.True(_vm.HasResult);
        Assert.NotNull(_vm.ResultImage);
        Assert.True(_vm.SavePngCommand.CanExecute(null));
        Assert.True(_vm.AddToPdfCommand.CanExecute(null));
        Assert.Contains("Fon olib tashlandi", _vm.StatusMessage);
    }

    [Fact]
    public async Task A_failed_removal_shows_the_error_and_leaves_nothing_to_save()
    {
        await LoadImage();
        _vm.IsModelAvailable = true;

        _remover.RemoveBackgroundToBitmapAsync(default!, default, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.MissingComponent, "Model topilmadi"));

        await _vm.RemoveBackgroundCommand.ExecuteAsync(null);

        Assert.False(_vm.HasResult);
        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Model topilmadi", _dialogs.ShownErrors[0]);

        // Xatodan keyin sahifa qayta urinishga tayyor bo'lishi kerak.
        Assert.False(_vm.IsBusy);
        Assert.True(_vm.RemoveBackgroundCommand.CanExecute(null));
    }

    [Fact]
    public async Task Loading_a_new_image_drops_the_previous_result()
    {
        // Aks holda o'ng panelda eski rasmning natijasi qolib, foydalanuvchi uni yangi
        // rasmniki deb o'ylardi.
        await PrepareResult();

        await LoadImage("boshqa.png");

        Assert.False(_vm.HasResult);
        Assert.Null(_vm.ResultImage);
    }

    // =================================================================================
    //  Natijani saqlash
    // =================================================================================

    [Fact]
    public async Task Saving_is_blocked_until_there_is_a_result()
    {
        Assert.False(_vm.SavePngCommand.CanExecute(null));
        Assert.False(_vm.AddToPdfCommand.CanExecute(null));

        await PrepareResult();

        Assert.True(_vm.SavePngCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_writes_nothing()
    {
        await PrepareResult();

        await _vm.SavePngCommand.ExecuteAsync(null);

        await _remover.DidNotReceiveWithAnyArgs().SaveAsPngAsync(default!, default!, default);
    }

    [Fact]
    public async Task Saving_a_png_uses_the_chosen_path_and_offers_to_reveal_it()
    {
        await PrepareResult();
        var target = _temp.At("natija.png");
        _dialogs.SaveFileResults.Enqueue(target);

        await _vm.SavePngCommand.ExecuteAsync(null);

        await _remover.Received(1).SaveAsPngAsync(Arg.Any<SKBitmap>(), target, Arg.Any<CancellationToken>());
        Assert.Equal([target], _dialogs.RevealedPaths);
    }

    [Fact]
    public async Task A_failed_save_shows_the_error_and_reveals_nothing()
    {
        await PrepareResult();
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.png"));
        _remover.SaveAsPngAsync(default!, default!, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OutputNotWritable, "Fayl band"));

        await _vm.SavePngCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Empty(_dialogs.RevealedPaths);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task Saving_as_pdf_goes_through_a_temporary_png()
    {
        // PDF importeri fayl yo'lini kutadi, shuning uchun natija avval PNG ga yoziladi —
        // va o'sha vaqtinchalik fayl PDF ga qo'shilishi kerak.
        await PrepareResult();
        var target = _temp.At("natija.pdf");
        _dialogs.SaveFileResults.Enqueue(target);

        await _vm.AddToPdfCommand.ExecuteAsync(null);

        await _pdf.Received(1).ConvertImagesToPdfAsync(
            Arg.Is<List<string>>(images => images.Count == 1 && images[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase)),
            target,
            Arg.Any<ImageToPdfOptions?>(),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_the_pdf_dialog_creates_nothing()
    {
        await PrepareResult();

        await _vm.AddToPdfCommand.ExecuteAsync(null);

        await _pdf.DidNotReceiveWithAnyArgs().ConvertImagesToPdfAsync(default!, default!, default, default, default);
    }

    // =================================================================================
    //  Modelni yuklab olish va tozalash
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_confirmation_downloads_no_model()
    {
        // 168 MB — foydalanuvchi roziligisiz boshlanmasligi kerak.
        _dialogs.ConfirmResult = false;

        await _vm.DownloadModelCommand.ExecuteAsync(null);

        await _remover.DidNotReceiveWithAnyArgs().DownloadModelAsync(default, default);
        Assert.False(_vm.IsModelAvailable);
    }

    [Fact]
    public async Task A_successful_model_download_makes_the_page_ready_without_a_restart()
    {
        await LoadImage();
        _remover.DownloadModelAsync(default, default).ReturnsForAnyArgs(_ =>
        {
            _modelAvailable = true;
            return _temp.At(Path.Combine("Models", "u2net.onnx"));
        });

        await _vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.True(_vm.IsModelAvailable);
        Assert.Equal(_remover.ModelPath, _vm.ModelPath);
        Assert.True(_vm.RemoveBackgroundCommand.CanExecute(null));
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_model_download_shows_the_error_and_keeps_the_page_locked()
    {
        _remover.DownloadModelAsync(default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.MissingComponent, "Internet yo'q"));

        await _vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Internet yo'q", _dialogs.ShownErrors[0]);
        Assert.False(_vm.IsModelAvailable);
        Assert.False(_vm.IsBusy);
        Assert.True(_vm.DownloadModelCommand.CanExecute(null));
    }

    [Fact]
    public void Opening_the_model_folder_rereads_the_model_state()
    {
        // Foydalanuvchi faylni papkaga o'zi tashlagan bo'lishi mumkin — oyna yopilgach
        // sahifa qayta tekshirishi kerak.
        _modelAvailable = true;

        _vm.OpenModelFolderCommand.Execute(null);

        Assert.Equal([_temp.At("Models")], _dialogs.RevealedPaths);
        Assert.True(_vm.IsModelAvailable);
    }

    [Fact]
    public async Task Clear_empties_both_panels()
    {
        await PrepareResult();

        Assert.True(_vm.ClearCommand.CanExecute(null));
        _vm.ClearCommand.Execute(null);

        Assert.False(_vm.HasSource);
        Assert.False(_vm.HasResult);
        Assert.Null(_vm.SourceImage);
        Assert.Equal(0, _vm.SourceSizeBytes);
        Assert.False(_vm.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void Back_asks_the_shell_to_navigate_away()
    {
        var raised = 0;
        _vm.BackRequested += (_, _) => raised++;

        _vm.BackCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private async Task LoadImage(string fileName = "rasm.jpg")
    {
        _dialogs.OpenFileResults.Enqueue(_temp.WriteFile(fileName, "soxta rasm"));
        await _vm.OpenImageCommand.ExecuteAsync(null);
    }

    /// <summary>Rasm yuklangan, model bor va fon olib tashlangan holatga olib keladi.</summary>
    private async Task PrepareResult()
    {
        await LoadImage();
        _vm.IsModelAvailable = true;

        _remover.RemoveBackgroundToBitmapAsync(default!, default, default, default)
            .ReturnsForAnyArgs(_ => new SKBitmap(2, 2));

        await _vm.RemoveBackgroundCommand.ExecuteAsync(null);
    }

    /// <summary>Muzlatilgan 1×1 tasvir — servis qaytaradigan eskizning o'rnini bosadi.</summary>
    private static BitmapSource Thumbnail()
    {
        var image = BitmapSource.Create(1, 1, 96d, 96d, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        image.Freeze();
        return image;
    }
}
