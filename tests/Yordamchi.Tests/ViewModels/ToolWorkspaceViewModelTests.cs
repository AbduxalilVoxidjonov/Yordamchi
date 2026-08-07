using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="ToolWorkspaceViewModel"/> sinovlari. Bitta oyna 17 ta vositani xizmat qiladi,
/// shuning uchun eng qimmat xatolik — <b>vositalar orasidagi sizib o'tish</b>: oldingi vositaning
/// fayli, sahifasi yoki natijasi yangisida qolib ketishi.
/// <para>
/// Hech qanday PDF ochilmaydi: <see cref="IPdfEngineService"/> va <see cref="IPdfService"/>
/// o'rniga substitute turadi, dialoglar esa <see cref="FakeDialogService"/> orqali oldindan
/// javob beradi.
/// </para>
/// </summary>
public sealed class ToolWorkspaceViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly IPdfEngineService _engine = Substitute.For<IPdfEngineService>();
    private readonly IPdfService _pdfService = Substitute.For<IPdfService>();
    private readonly FakeDialogService _dialogs = new();
    private readonly ToolWorkspaceViewModel _vm;

    /// <summary>Oxirgi <c>ExecuteAsync</c> ga uzatilgan so'rov — <see cref="EngineSucceeds"/> to'ldiradi.</summary>
    private ToolRequest? _sentRequest;

    public ToolWorkspaceViewModelTests()
    {
        // Standart holat: so'rov to'g'ri, tashqi komponentlar joyida, fayllar o'qiladi va
        // hujjatlar bo'sh. Har bir sinov faqat o'ziga kerakli shartni o'zgartiradi.
        //
        // Ikkala tekshiruv ham ataylab aniq null qilib qo'yiladi: substitute'ning standart
        // javobi bo'sh satr bo'lib, u "muammo bor" deb o'qilardi.
        _engine.Validate(default!).ReturnsForAnyArgs((string?)null);
        _engine.CheckPrerequisites(default, default).ReturnsForAnyArgs((string?)null);

        _pdfService.GetPageCountAsync(default!, default, default).ReturnsForAnyArgs(1);
        _pdfService.RenderPageAsync(default!, default, default, default, default).ReturnsForAnyArgs(_ => Pixel());
        _pdfService.RenderImageThumbnailAsync(default!, default, default).ReturnsForAnyArgs(_ => Pixel());
        PagesPerDocument(0);

        _vm = new ToolWorkspaceViewModel(_engine, _pdfService, _dialogs);
    }

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Faollashtirish
    // =================================================================================

    [Fact]
    public void The_workspace_starts_without_a_tool()
    {
        Assert.False(_vm.HasTool);
        Assert.Equal("Vosita", _vm.Title);
        Assert.False(_vm.ExecuteCommand.CanExecute(null));
        Assert.False(_vm.DownloadMissingComponentCommand.CanExecute(null));
    }

    [Fact]
    public void Activate_rebuilds_the_whole_header_for_the_chosen_tool()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));

        Assert.True(_vm.HasTool);
        Assert.Equal("PDF siqish", _vm.Title);
        Assert.Equal("Siqish", _vm.ExecuteButtonText);
        Assert.Equal(".pdf", _vm.AcceptedExtensions);
        Assert.IsType<CompressOptionsViewModel>(_vm.Options);
    }

    [Fact]
    public void Activate_refuses_a_null_tool() =>
        Assert.Throws<ArgumentNullException>(() => _vm.Activate(null!));

    [Fact]
    public async Task Activate_wipes_the_files_pages_result_and_status_of_the_previous_tool()
    {
        // Eng xavfli sizib o'tish: oldingi vositaning fayllari yangi vositada "Bajarish"
        // tugmasi bilan birga qolib ketsa, foydalanuvchi butunlay boshqa hujjatni qayta ishlaydi.
        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize, "eski.pdf");
        _vm.LastResult = ToolRunResult.Ok("Tayyor", _temp.At("eski-natija.pdf"));
        _vm.StatusMessage = "Tayyor";
        _vm.IsPreviewOpen = true;
        _vm.PreviewImage = Pixel();

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));

        Assert.Empty(_vm.Files);
        Assert.Empty(_vm.Pages);
        Assert.Null(_vm.LastResult);
        Assert.False(_vm.HasResult);
        Assert.Equal(string.Empty, _vm.StatusMessage);
        Assert.False(_vm.IsPreviewOpen);
        Assert.Null(_vm.PreviewImage);
        Assert.Equal(0, _vm.SelectedCount);
    }

    [Fact]
    public void Activate_gives_each_tool_its_own_options_panel()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Rotate));
        var first = _vm.Options;
        Assert.IsType<RotateOptionsViewModel>(first);

        _vm.Activate(ToolCatalog.Get(ToolId.Rotate));
        Assert.NotSame(first, _vm.Options);

        // Birlashtirishda sozlama yo'q — panel butunlay yashirilishi kerak.
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));
        Assert.Null(_vm.Options);
    }

    [Fact]
    public void The_accepted_extensions_follow_the_input_kind_of_the_tool()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        Assert.Contains(".png", _vm.AcceptedExtensions);
        Assert.DoesNotContain(".pdf", _vm.AcceptedExtensions);

        _vm.Activate(ToolCatalog.Get(ToolId.WordToPdf));
        Assert.Contains(".docx", _vm.AcceptedExtensions);
    }

    [Fact]
    public void The_output_hint_tells_apart_a_single_file_from_a_whole_folder()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.PdfToWord));
        Assert.Contains(".docx", _vm.OutputHintText);

        _vm.Activate(ToolCatalog.Get(ToolId.Split));
        Assert.Contains("papka", _vm.OutputHintText);
    }

    // =================================================================================
    //  Fayl qo'shish
    // =================================================================================

    [Fact]
    public async Task Files_that_do_not_fit_the_tool_are_dropped_with_an_explanation()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));

        await Drop(_temp.WriteFile("hujjat.txt", "matn"));

        Assert.Empty(_vm.Files);
        Assert.Contains("to'g'ri kelmadi", _vm.StatusMessage);
    }

    [Fact]
    public async Task Only_the_fitting_files_survive_a_mixed_drop()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"), _temp.WriteFile("rasm.png", "x"));

        Assert.Single(_vm.Files);
        Assert.Equal("bir.pdf", _vm.Files[0].FileName);
    }

    [Fact]
    public async Task A_single_file_tool_replaces_the_previous_document()
    {
        // Siqish bitta hujjat bilan ishlaydi: ikkinchi fayl birinchisining ustiga tushishi kerak,
        // aks holda qaysi biri siqilishi tushunarsiz bo'lardi.
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        await Drop(_temp.WriteFile("ikki.pdf", "%PDF"));

        Assert.Single(_vm.Files);
        Assert.Equal("ikki.pdf", _vm.Files[0].FileName);
    }

    [Fact]
    public async Task A_multi_file_tool_keeps_every_file_and_numbers_them_in_order()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));

        await Drop(_temp.WriteFile("a.png", "x"), _temp.WriteFile("b.png", "x"), _temp.WriteFile("c.png", "x"));

        Assert.Equal(["a.png", "b.png", "c.png"], _vm.Files.Select(file => file.FileName));
        Assert.Equal([1, 2, 3], _vm.Files.Select(file => file.OrderNumber));
        Assert.True(_vm.HasFiles);
        Assert.True(_vm.ShowsFileGallery);
        Assert.False(_vm.ShowsFileList);
    }

    [Fact]
    public async Task The_same_file_is_not_added_twice()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));
        var file = _temp.WriteFile("bir.pdf", "%PDF");

        await Drop(file);
        await Drop(file);

        Assert.Single(_vm.Files);
        Assert.Contains("allaqachon bor", _vm.StatusMessage);
    }

    [Fact]
    public async Task An_empty_drop_changes_nothing()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));

        await _vm.DropFilesCommand.ExecuteAsync((string[]?)null);
        await Drop();

        Assert.Single(_vm.Files);
    }

    [Fact]
    public async Task Adding_a_file_makes_the_previous_result_stale()
    {
        // Yangi fayl qo'shilgach pastdagi "Tayyor!" paneli eski natijani ko'rsatib turmasligi kerak.
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        _vm.LastResult = ToolRunResult.Ok("Tayyor", _temp.At("eski.pdf"));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));

        Assert.Null(_vm.LastResult);
        Assert.False(_vm.HasResult);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_adds_nothing()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));

        // OpenFilesResults bo'sh — dialog null qaytaradi, ya'ni foydalanuvchi bekor qildi.
        await _vm.OpenFilesCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Files);
    }

    [Fact]
    public async Task A_single_file_tool_opens_the_single_file_dialog()
    {
        // Ko'p tanlovli dialog bitta fayl qabul qiladigan vositada chalg'itadi.
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        _dialogs.OpenFileResults.Enqueue(_temp.WriteFile("bir.pdf", "%PDF"));

        await _vm.OpenFilesCommand.ExecuteAsync(null);

        Assert.Single(_vm.Files);
    }

    // =================================================================================
    //  Tartib va olib tashlash
    // =================================================================================

    [Fact]
    public async Task Reordering_is_offered_only_when_the_order_can_matter()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        Assert.False(_vm.MoveFileUpCommand.CanExecute(_vm.Files[0]));

        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        await Drop(_temp.WriteFile("a.png", "x"));
        Assert.False(_vm.MoveFileDownCommand.CanExecute(_vm.Files[0]));

        await Drop(_temp.WriteFile("b.png", "x"));
        Assert.True(_vm.MoveFileDownCommand.CanExecute(_vm.Files[0]));
    }

    [Fact]
    public async Task Moving_a_file_renumbers_the_whole_list()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        await Drop(_temp.WriteFile("a.png", "x"), _temp.WriteFile("b.png", "x"), _temp.WriteFile("c.png", "x"));

        _vm.MoveFileUpCommand.Execute(_vm.Files[2]);

        Assert.Equal(["a.png", "c.png", "b.png"], _vm.Files.Select(file => file.FileName));
        Assert.Equal([1, 2, 3], _vm.Files.Select(file => file.OrderNumber));
    }

    [Fact]
    public async Task The_first_file_cannot_be_moved_above_the_list()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        await Drop(_temp.WriteFile("a.png", "x"), _temp.WriteFile("b.png", "x"));

        _vm.MoveFileUpCommand.Execute(_vm.Files[0]);
        _vm.MoveFileDownCommand.Execute(_vm.Files[1]);

        Assert.Equal(["a.png", "b.png"], _vm.Files.Select(file => file.FileName));
    }

    [Fact]
    public async Task Removing_a_file_renumbers_the_rest()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        await Drop(_temp.WriteFile("a.png", "x"), _temp.WriteFile("b.png", "x"), _temp.WriteFile("c.png", "x"));

        _vm.Files[0].RemoveCommand.Execute(null);

        Assert.Equal(["b.png", "c.png"], _vm.Files.Select(file => file.FileName));
        Assert.Equal([1, 2], _vm.Files.Select(file => file.OrderNumber));
    }

    [Fact]
    public async Task Removing_a_file_also_removes_the_pages_that_came_from_it()
    {
        // Aks holda o'chirilgan hujjatning sahifalari natijaga jimgina tushib ketardi.
        PagesPerDocument(2);
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"), _temp.WriteFile("ikki.pdf", "%PDF"));

        Assert.Equal(4, _vm.Pages.Count);

        _vm.RemoveFileCommand.Execute(_vm.Files[0]);

        Assert.Equal(2, _vm.Pages.Count);
        Assert.All(_vm.Pages, page => Assert.EndsWith("ikki.pdf", page.Model.SourceFilePath));
        Assert.Equal([1, 2], _vm.Pages.Select(page => page.PageNumber));
    }

    [Fact]
    public async Task The_source_badge_appears_only_when_pages_come_from_several_documents()
    {
        // Bitta hujjatda fayl nomi nishoni har bir kartochkada takrorlanib, faqat joy egallaydi.
        PagesPerDocument(2);
        _vm.Activate(ToolCatalog.Get(ToolId.Merge));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        Assert.All(_vm.Pages, page => Assert.False(page.ShowSourceBadge));

        await Drop(_temp.WriteFile("ikki.pdf", "%PDF"));
        Assert.All(_vm.Pages, page => Assert.True(page.ShowSourceBadge));
    }

    [Fact]
    public async Task Clear_empties_both_lists_and_drops_the_result()
    {
        PagesPerDocument(2);
        _vm.Activate(ToolCatalog.Get(ToolId.Organize));
        Assert.False(_vm.ClearCommand.CanExecute(null));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        _vm.LastResult = ToolRunResult.Ok("Tayyor", _temp.At("natija.pdf"));

        Assert.True(_vm.ClearCommand.CanExecute(null));
        _vm.ClearCommand.Execute(null);

        Assert.Empty(_vm.Files);
        Assert.Empty(_vm.Pages);
        Assert.Null(_vm.LastResult);
        Assert.Equal("Ro'yxat tozalandi", _vm.StatusMessage);
        Assert.False(_vm.ClearCommand.CanExecute(null));
    }

    // =================================================================================
    //  Sahifa amallari
    // =================================================================================

    [Fact]
    public async Task Page_actions_wait_for_a_selection()
    {
        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize);

        Assert.True(_vm.ShowsPageGrid);
        Assert.True(_vm.SelectAllCommand.CanExecute(null));
        Assert.False(_vm.RotateSelectedClockwiseCommand.CanExecute(null));
        Assert.False(_vm.DeleteSelectedCommand.CanExecute(null));

        _vm.SelectAllCommand.Execute(null);

        Assert.Equal(3, _vm.SelectedCount);
        Assert.True(_vm.RotateSelectedClockwiseCommand.CanExecute(null));

        _vm.ClearSelectionCommand.Execute(null);

        Assert.Equal(0, _vm.SelectedCount);
        Assert.False(_vm.ClearSelectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task Inverting_the_selection_swaps_the_ticked_pages()
    {
        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize);
        _vm.Pages[0].IsSelected = true;

        _vm.InvertSelectionCommand.Execute(null);

        Assert.Equal([false, true, true], _vm.Pages.Select(page => page.IsSelected));
        Assert.Equal(2, _vm.SelectedCount);
    }

    [Fact]
    public async Task Rotating_touches_only_the_ticked_pages()
    {
        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize);
        _vm.Pages[1].IsSelected = true;

        _vm.RotateSelectedClockwiseCommand.Execute(null);

        Assert.Equal(
            [PageRotation.None, PageRotation.Rotate90, PageRotation.None],
            _vm.Pages.Select(page => page.Rotation));
    }

    [Fact]
    public async Task Deleting_the_selected_pages_renumbers_the_rest()
    {
        PagesPerDocument(4);
        await ActivateWith(ToolId.Organize);
        _vm.Pages[1].IsSelected = true;
        _vm.Pages[2].IsSelected = true;

        _vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal([1, 2], _vm.Pages.Select(page => page.PageNumber));
        Assert.Equal([1, 4], _vm.Pages.Select(page => page.OriginalPageNumber));
        Assert.Equal(0, _vm.SelectedCount);
    }

    [Fact]
    public async Task Rotate_all_applies_the_angle_chosen_in_the_options_panel()
    {
        PagesPerDocument(2);
        await ActivateWith(ToolId.Rotate);
        ((RotateOptionsViewModel)_vm.Options!).Angle = 180;

        _vm.RotateAllCommand.Execute(null);

        Assert.All(_vm.Pages, page => Assert.Equal(PageRotation.Rotate180, page.Rotation));
        Assert.Contains("180°", _vm.StatusMessage);
    }

    [Fact]
    public async Task Rotate_selected_without_a_selection_says_why_nothing_happened()
    {
        // Tugma bosildi, lekin hech nima o'zgarmadi — foydalanuvchi dasturni buzuq deb
        // o'ylamasligi uchun sabab yozilishi kerak.
        PagesPerDocument(2);
        await ActivateWith(ToolId.Rotate);
        ((RotateOptionsViewModel)_vm.Options!).ApplyToAll = false;

        _vm.RotateAllCommand.Execute(null);

        Assert.All(_vm.Pages, page => Assert.Equal(PageRotation.None, page.Rotation));
        Assert.Contains("Sahifa tanlanmagan", _vm.StatusMessage);
    }

    [Fact]
    public async Task The_summary_counts_files_pages_and_the_selection()
    {
        Assert.Equal("Fayl tanlanmagan", _vm.SummaryText);

        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize, "hisobot.pdf");

        Assert.Contains("hisobot.pdf", _vm.SummaryText);
        Assert.Contains("3 sahifa", _vm.SummaryText);

        _vm.Pages[0].IsSelected = true;
        Assert.Contains("1 ta tanlandi", _vm.SummaryText);
    }

    [Fact]
    public void Zooming_stays_inside_the_readable_range()
    {
        for (var i = 0; i < 20; i++)
            _vm.ZoomInCommand.Execute(null);

        Assert.Equal(300d, _vm.ThumbnailSize);

        for (var i = 0; i < 20; i++)
            _vm.ZoomOutCommand.Execute(null);

        Assert.Equal(110d, _vm.ThumbnailSize);
    }

    // =================================================================================
    //  Tashqi komponentlar
    // =================================================================================

    [Fact]
    public void Activate_asks_the_engine_about_the_components_the_new_tool_needs()
    {
        MissingOcrLanguages();

        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));

        Assert.Equal("Til fayllari yetishmayapti", _vm.PrerequisiteWarning);
        Assert.True(_vm.HasPrerequisiteWarning);
        Assert.Equal(DownloadableComponent.OcrLanguages, _vm.MissingComponent);
        Assert.True(_vm.HasDownloadableComponent);
        Assert.True(_vm.DownloadMissingComponentCommand.CanExecute(null));
    }

    [Fact]
    public void Switching_to_a_healthy_tool_takes_the_previous_warning_away()
    {
        // Ogohlantirish paneli oldingi vositadan qolib ketsa, foydalanuvchi mavjud bo'lmagan
        // muammoni hal qilishga urinadi.
        MissingOcrLanguages();
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));

        Assert.Null(_vm.PrerequisiteWarning);
        Assert.False(_vm.HasPrerequisiteWarning);
        Assert.Equal(DownloadableComponent.None, _vm.MissingComponent);
        Assert.False(_vm.DownloadMissingComponentCommand.CanExecute(null));
    }

    [Fact]
    public void A_failing_check_still_lets_the_workspace_open()
    {
        // Tekshiruvning o'zi ishdan chiqsa ham vosita ochilishi kerak — foydalanuvchi hech
        // bo'lmaganda sababini o'qiydi.
        _engine.CheckPrerequisites(default, default).ThrowsForAnyArgs(new InvalidOperationException("Registrni o'qib bo'lmadi"));

        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));

        Assert.True(_vm.HasTool);
        Assert.Equal("Registrni o'qib bo'lmadi", _vm.PrerequisiteWarning);
        Assert.Equal(DownloadableComponent.None, _vm.MissingComponent);
    }

    [Fact]
    public void Changing_the_ocr_language_re_checks_the_components()
    {
        // Til fayllari tanlangan tilga bog'liq: "uzb" o'rnatilgan, "kor" esa yo'q bo'lishi mumkin.
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));
        var options = (OcrOptionsViewModel)_vm.Options!;

        options.Language = "eng";

        _engine.Received(2).CheckPrerequisites(ToolId.OcrToWord, Arg.Any<object?>());

        // Boshqa sozlamalar tashqi komponentga ta'sir qilmaydi — har bir belgida qayta
        // tekshirish ortiqcha ish bo'lardi.
        options.Dpi = 400;

        _engine.Received(2).CheckPrerequisites(ToolId.OcrToWord, Arg.Any<object?>());
    }

    // =================================================================================
    //  Yetishmayotgan komponentni yuklab olish
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_confirmation_downloads_nothing()
    {
        MissingOcrLanguages();
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));
        _dialogs.ConfirmResult = false;

        await _vm.DownloadMissingComponentCommand.ExecuteAsync(null);

        await _engine.DidNotReceiveWithAnyArgs().DownloadComponentAsync(default, default, default, default);
        Assert.Single(_dialogs.Confirmations);
        Assert.Equal(DownloadableComponent.OcrLanguages, _vm.MissingComponent);
    }

    [Fact]
    public async Task Downloading_the_languages_passes_the_language_chosen_in_the_panel()
    {
        MissingOcrLanguages();
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));
        ((OcrOptionsViewModel)_vm.Options!).Language = "uzb+eng";

        await _vm.DownloadMissingComponentCommand.ExecuteAsync(null);

        await _engine.Received(1).DownloadComponentAsync(
            DownloadableComponent.OcrLanguages,
            Arg.Is<OcrOptions>(options => options.Language == "uzb+eng"),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_finished_download_re_checks_and_hides_the_warning()
    {
        // Yuklab olingandan keyin panel o'z-o'zidan yo'qolishi kerak: foydalanuvchi vositani
        // yopib qayta ochishi shart emas.
        MissingOcrLanguages();
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));

        _engine.When(engine => engine.DownloadComponentAsync(
                Arg.Any<DownloadableComponent>(), Arg.Any<object?>(),
                Arg.Any<IProgress<PdfProgress>?>(), Arg.Any<CancellationToken>()))
            .Do(_ => AllComponentsReady());

        await _vm.DownloadMissingComponentCommand.ExecuteAsync(null);

        Assert.Null(_vm.PrerequisiteWarning);
        Assert.Equal(DownloadableComponent.None, _vm.MissingComponent);
        Assert.False(_vm.DownloadMissingComponentCommand.CanExecute(null));
        Assert.Equal("Til fayllari yuklandi", _vm.StatusMessage);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_download_keeps_the_warning_and_shows_the_error()
    {
        MissingOcrLanguages();
        _vm.Activate(ToolCatalog.Get(ToolId.OcrToWord));

        _engine.DownloadComponentAsync(default, default, default, default)
            .ThrowsAsyncForAnyArgs(new PdfServiceException(PdfErrorKind.OperationFailed, "Internet yo'q"));

        await _vm.DownloadMissingComponentCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Internet yo'q", _dialogs.ShownErrors[0]);
        Assert.Equal(DownloadableComponent.OcrLanguages, _vm.MissingComponent);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task The_ai_model_download_asks_its_own_question()
    {
        _engine.GetMissingComponent(ToolId.BackgroundRemover, Arg.Any<object?>())
            .Returns(DownloadableComponent.AiModel);
        _engine.CheckPrerequisites(ToolId.BackgroundRemover, Arg.Any<object?>())
            .Returns("AI modeli yuklab olinmagan");

        _vm.Activate(ToolCatalog.Get(ToolId.BackgroundRemover));
        await _vm.DownloadMissingComponentCommand.ExecuteAsync(null);

        Assert.Equal(["AI modelini yuklab olish"], _dialogs.Confirmations);
        await _engine.Received(1).DownloadComponentAsync(
            DownloadableComponent.AiModel,
            Arg.Any<object?>(),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    // =================================================================================
    //  Bajarish qoidalari
    // =================================================================================

    [Fact]
    public async Task Execute_stays_blocked_until_a_file_is_added()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        Assert.False(_vm.ExecuteCommand.CanExecute(null));

        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));

        Assert.True(_vm.ExecuteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Execute_stays_blocked_while_a_file_failed_to_load()
    {
        // Ochilmagan hujjatni qayta ishlashga urinish faqat ikkinchi xato oynasini beradi.
        _pdfService.GetPageCountAsync(default!, default, default)
            .ThrowsAsyncForAnyArgs(new PdfServiceException(PdfErrorKind.CorruptedDocument, "Shikastlangan"));

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("buzuq.pdf", "shikastlangan"));

        Assert.True(_vm.Files[0].HasError);
        Assert.False(_vm.ExecuteCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_execute_button_is_told_when_a_file_turns_out_to_be_broken()
    {
        // Regressiya. Fayl xatosi eskiz yuklangandan KEYIN to'ldiriladi — o'sha paytga kelib
        // kolleksiya o'zgarishi va u bilan birga CanExecute qayta baholanishi allaqachon o'tib
        // ketgan bo'ladi. Xabar bo'lmasa WPF eski javobni ("yoqiq") saqlab qolar va tugma
        // shikastlangan hujjatda ham bosiladigan bo'lib turardi.
        //
        // Xato ataylab qo'lda, obunadan KEYIN qo'yiladi: shunda tekshiriladigan narsa aynan
        // "kech kelgan xato xabar qiladimi" bo'lib qoladi, drop paytidagi umumiy
        // RefreshCommands chaqiruvlari natijani bo'yab yubormaydi.
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("yaxshi.pdf", "%PDF"));

        Assert.True(_vm.ExecuteCommand.CanExecute(null));

        var notified = false;
        _vm.ExecuteCommand.CanExecuteChanged += (_, _) => notified = true;

        _vm.Files[0].ErrorMessage = "Shikastlangan hujjat";

        Assert.True(notified, "kech kelgan xato uchun CanExecuteChanged ko'tarilmadi");
        Assert.False(_vm.ExecuteCommand.CanExecute(null));
    }


    [Fact]
    public async Task Every_command_is_blocked_while_the_page_is_busy()
    {
        PagesPerDocument(2);
        await ActivateWith(ToolId.Organize);
        _vm.Pages[0].IsSelected = true;

        _vm.IsBusy = true;

        Assert.False(_vm.ExecuteCommand.CanExecute(null));
        Assert.False(_vm.OpenFilesCommand.CanExecute(null));
        Assert.False(_vm.ClearCommand.CanExecute(null));
        Assert.False(_vm.SelectAllCommand.CanExecute(null));
        Assert.False(_vm.DeleteSelectedCommand.CanExecute(null));
        Assert.True(_vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_runs_nothing()
    {
        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));

        // SaveFileResults bo'sh — dialog null qaytaradi, ya'ni foydalanuvchi bekor qildi.
        await _vm.ExecuteCommand.ExecuteAsync(null);

        await _engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        Assert.False(_vm.HasResult);
    }

    [Fact]
    public async Task Broken_options_stop_the_run_before_the_save_dialog_opens()
    {
        // Foydalanuvchi bekorga fayl nomi o'ylab o'tirmasligi kerak: xato sozlama darhol aytiladi.
        _vm.Activate(ToolCatalog.Get(ToolId.Protect));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Parol kiriting", _dialogs.ShownErrors[0]);
        Assert.Single(_dialogs.SaveFileResults); // saqlash oynasi umuman ochilmadi
        _engine.DidNotReceiveWithAnyArgs().Validate(default!);
    }

    [Fact]
    public async Task A_complaint_from_the_engine_stops_the_run()
    {
        _engine.Validate(default!).ReturnsForAnyArgs("Avval fayl tanlang.");

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        await _engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
        Assert.Equal("Avval fayl tanlang.", _vm.StatusMessage);
        Assert.Single(_dialogs.ShownErrors);
    }

    [Fact]
    public async Task Executing_sends_the_files_in_their_current_order_with_the_chosen_output()
    {
        var target = _temp.At("natija.pdf");
        EngineSucceeds("Tayyor", target);

        _vm.Activate(ToolCatalog.Get(ToolId.ImageToPdf));
        await Drop(_temp.WriteFile("a.png", "x"), _temp.WriteFile("b.png", "x"));
        _vm.MoveFileUpCommand.Execute(_vm.Files[1]);
        _dialogs.SaveFileResults.Enqueue(target);

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.NotNull(_sentRequest);
        Assert.Equal(ToolId.ImageToPdf, _sentRequest!.Tool);
        Assert.Equal(["b.png", "a.png"], _sentRequest.InputFiles.Select(Path.GetFileName));
        Assert.Equal(target, _sentRequest.OutputPath);
        Assert.Null(_sentRequest.OutputFolder);
        Assert.IsType<ImageToPdfOptions>(_sentRequest.Options);
    }

    [Fact]
    public async Task A_folder_tool_sends_the_folder_of_the_chosen_file()
    {
        // IDialogService da papka tanlash oynasi yo'q — foydalanuvchi papka ichida istalgan
        // nom bilan "saqlash" oynasidan o'tadi, oyna esa faqat papkani oladi.
        EngineSucceeds("Tayyor", _temp.At("natija/1.pdf"));

        await ActivateWith(ToolId.Split);
        _dialogs.SaveFileResults.Enqueue(_temp.At("istalgan-nom.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(_temp.Root, _sentRequest?.OutputFolder);
        Assert.Null(_sentRequest?.OutputPath);
    }

    [Fact]
    public async Task The_unlock_password_travels_in_its_own_field()
    {
        // Parol sozlamalar modeliga tushmaydi — u ToolRequest.Password da alohida ketadi.
        EngineSucceeds();

        _vm.Activate(ToolCatalog.Get(ToolId.Unlock));
        await Drop(_temp.WriteFile("qulflangan.pdf", "%PDF"));
        ((UnlockOptionsViewModel)_vm.Options!).Password = "Parol123";
        _dialogs.SaveFileResults.Enqueue(_temp.At("ochilgan.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.Equal("Parol123", _sentRequest?.Password);
        Assert.Null(_sentRequest?.Options);
    }

    [Fact]
    public async Task The_page_plan_carries_the_edited_order_and_rotation()
    {
        // Eskizlarda qilingan har bir tahrir aynan shu reja orqali dvigatelga yetadi.
        EngineSucceeds();
        PagesPerDocument(3);
        await ActivateWith(ToolId.Organize, "manba.pdf");

        _vm.Pages[0].IsSelected = true;
        _vm.RotateSelectedClockwiseCommand.Execute(null);
        _vm.RemovePage(_vm.Pages[1]);
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        var plan = _sentRequest?.PagePlan;
        Assert.NotNull(plan);
        Assert.Equal([0, 2], plan!.Select(page => page.SourcePageIndex));
        Assert.Equal([PageRotation.Rotate90, PageRotation.None], plan.Select(page => page.Rotation));
    }

    [Fact]
    public async Task Tools_without_thumbnails_send_no_page_plan()
    {
        EngineSucceeds();

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.pdf"));

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.Null(_sentRequest?.PagePlan);
    }

    // =================================================================================
    //  Natija
    // =================================================================================

    [Fact]
    public async Task A_finished_run_offers_the_result_to_be_opened()
    {
        var target = _temp.WriteFile("natija.pdf", "%PDF");
        EngineSucceeds("Hujjat siqildi", target);

        _vm.Activate(ToolCatalog.Get(ToolId.Compress));
        await Drop(_temp.WriteFile("bir.pdf", "%PDF"));
        _dialogs.SaveFileResults.Enqueue(target);

        await _vm.ExecuteCommand.ExecuteAsync(null);

        Assert.True(_vm.HasResult);
        Assert.Equal("Hujjat siqildi", _vm.ResultMessage);
        Assert.Equal("Hujjat siqildi", _vm.StatusMessage);
        Assert.Equal(target, _vm.ResultLocation);
        Assert.True(_vm.OpenResultCommand.CanExecute(null));

        _vm.OpenResultCommand.Execute(null);
        Assert.Equal([target], _dialogs.RevealedPaths);
    }

    [Fact]
    public void Several_output_files_are_summarised_by_their_folder()
    {
        // Bo'lish vositasi o'nlab fayl yozadi — ulardan bittasining nomini ko'rsatish adashtiradi.
        _vm.LastResult = ToolRunResult.Ok("2 ta fayl", _temp.At("natija/1.pdf"), _temp.At("natija/2.pdf"));

        Assert.Equal(_temp.At("natija"), _vm.ResultLocation);

        _vm.RevealResultCommand.Execute(null);
        Assert.Equal([_temp.At("natija")], _dialogs.RevealedPaths);
    }

    [Fact]
    public void Without_a_result_there_is_nothing_to_open()
    {
        Assert.False(_vm.HasResult);
        Assert.Equal(string.Empty, _vm.ResultLocation);
        Assert.False(_vm.OpenResultCommand.CanExecute(null));
        Assert.False(_vm.RevealResultCommand.CanExecute(null));
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>Sinovlar uchun eng arzon haqiqiy <see cref="BitmapSource"/> — 1×1 piksel.</summary>
    private static BitmapSource Pixel()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Har bir hujjat shuncha sahifa bilan "ochiladi".</summary>
    private void PagesPerDocument(int count) =>
        _pdfService.RenderPdfPagesAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs(call => Enumerable.Range(0, count)
                .Select(index => new PageModel
                {
                    SourceFilePath = (string)call[0],
                    SourcePageIndex = index,
                    Thumbnail = Pixel()
                })
                .ToList());

    private void MissingOcrLanguages()
    {
        _engine.CheckPrerequisites(ToolId.OcrToWord, Arg.Any<object?>()).Returns("Til fayllari yetishmayapti");
        _engine.GetMissingComponent(ToolId.OcrToWord, Arg.Any<object?>()).Returns(DownloadableComponent.OcrLanguages);
    }

    private void AllComponentsReady()
    {
        _engine.CheckPrerequisites(Arg.Any<ToolId>(), Arg.Any<object?>()).Returns((string?)null);
        _engine.GetMissingComponent(Arg.Any<ToolId>(), Arg.Any<object?>()).Returns(DownloadableComponent.None);
    }

    private void EngineSucceeds(string message = "Tayyor", params string[] files) =>
        _engine.ExecuteAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            _sentRequest = (ToolRequest)call[0];
            return ToolRunResult.Ok(message, files);
        });

    private Task Drop(params string[] paths) => _vm.DropFilesCommand.ExecuteAsync(paths);

    private async Task<string> ActivateWith(ToolId id, string fileName = "manba.pdf")
    {
        _vm.Activate(ToolCatalog.Get(id));

        var path = _temp.WriteFile(fileName, "%PDF-1.4 soxta");
        await Drop(path);
        return path;
    }
}
