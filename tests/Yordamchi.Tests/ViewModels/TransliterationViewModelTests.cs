using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// "Kirill ↔ Lotin" sahifasi. Matn rejimi haqiqiy servis bilan sinaladi — u sof va tez, mock
/// esa aynan tekshirilishi kerak bo'lgan narsani (natija to'g'ri chiqdimi) yashirib qo'yardi.
/// Fayl rejimi esa mock bilan: u yerda qiziq narsa diskda emas, sahifaning xatoga munosabatida.
/// </summary>
public sealed class TransliterationViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly FakeDialogService _dialogs = new();

    public void Dispose() => _temp.Dispose();

    private TransliterationViewModel CreateViewModel(ITransliterationService? service = null)
        => new(service ?? new TransliterationService(), _dialogs);

    // =================================================================================
    //  Matn rejimi
    // =================================================================================

    [Fact]
    public void Typing_converts_the_text_straight_away()
    {
        var vm = CreateViewModel();

        vm.SourceText = "Ўзбекистон";

        Assert.Equal("O'zbekiston", vm.ResultText);
    }

    [Fact]
    public void The_direction_follows_the_alphabet_of_the_text()
    {
        var vm = CreateViewModel();

        vm.SourceText = "O'zbekiston";

        Assert.Equal(TransliterationDirection.LatinToCyrillic, vm.DetectedDirection);
        Assert.Equal("Ўзбекистон", vm.ResultText);
        Assert.Contains("Lotin → Kirill", vm.DirectionHint);
    }

    [Fact]
    public void The_direction_button_shows_what_is_actually_applied()
    {
        var vm = CreateViewModel();

        Assert.Equal("Kirill → Lotin", vm.DirectionLabel);

        vm.SourceText = "O'zbekiston";

        // Avtomatik holatda yorliq aniqlangan yo'nalishni ko'rsatishi kerak, tanlanganini emas.
        Assert.Equal("Lotin → Kirill", vm.DirectionLabel);
    }

    [Fact]
    public void The_direction_button_flips_the_direction_and_leaves_auto_mode()
    {
        var vm = CreateViewModel();
        vm.SourceText = "O'zbekiston";

        vm.ToggleDirectionCommand.Execute(null);

        Assert.False(vm.AutoDetectDirection);
        Assert.Equal(TransliterationDirection.CyrillicToLatin, vm.Direction);
        Assert.Equal("Kirill → Lotin", vm.DirectionLabel);

        // Matn lotinda, yo'nalish esa kirilldan — ya'ni tegilmaydi.
        Assert.Equal("O'zbekiston", vm.ResultText);

        vm.ToggleDirectionCommand.Execute(null);

        Assert.Equal(TransliterationDirection.LatinToCyrillic, vm.Direction);
        Assert.Equal("Ўзбекистон", vm.ResultText);
    }

    [Fact]
    public void A_chosen_direction_is_obeyed_even_when_it_looks_wrong()
    {
        var vm = CreateViewModel();

        vm.AutoDetectDirection = false;
        vm.Direction = TransliterationDirection.CyrillicToLatin;
        vm.SourceText = "O'zbekiston";

        // Matn allaqachon lotinda — tegilmasligi kerak, chunki yo'nalish qo'lda tanlangan.
        Assert.Equal("O'zbekiston", vm.ResultText);
    }

    [Fact]
    public void Changing_the_apostrophe_style_updates_the_result_at_once()
    {
        var vm = CreateViewModel();
        vm.SourceText = "тўғри";

        Assert.Equal("to'g'ri", vm.ResultText);

        vm.Apostrophe = ApostropheStyle.Typographic;

        Assert.Equal("toʻgʻri", vm.ResultText);
    }

    [Fact]
    public void Swapping_sends_the_result_back_as_the_new_source()
    {
        var vm = CreateViewModel();
        vm.SourceText = "Ўзбекистон";

        vm.SwapCommand.Execute(null);

        Assert.Equal("O'zbekiston", vm.SourceText);
        Assert.Equal("Ўзбекистон", vm.ResultText);

        // Almashtirish aniq yo'nalishni talab qiladi: aks holda avtomatik aniqlash matnni
        // darhol yana ortga o'girib yuborardi.
        Assert.False(vm.AutoDetectDirection);
        Assert.Equal(TransliterationDirection.LatinToCyrillic, vm.Direction);
    }

    [Fact]
    public void There_is_nothing_to_swap_before_anything_is_typed()
    {
        var vm = CreateViewModel();

        Assert.False(vm.SwapCommand.CanExecute(null));
        Assert.False(vm.CopyResultCommand.CanExecute(null));
        Assert.False(vm.ClearTextCommand.CanExecute(null));
    }

    [Fact]
    public void The_result_can_be_copied_without_the_view_model_touching_the_clipboard()
    {
        var vm = CreateViewModel();
        vm.SourceText = "Ўзбекистон";

        vm.CopyResultCommand.Execute(null);

        Assert.Equal("O'zbekiston", Assert.Single(_dialogs.ClipboardTexts));
    }

    [Fact]
    public void Clearing_empties_both_sides()
    {
        var vm = CreateViewModel();
        vm.SourceText = "Ўзбекистон";

        vm.ClearTextCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SourceText);
        Assert.Equal(string.Empty, vm.ResultText);
    }

    [Fact]
    public void The_summary_counts_what_was_typed()
    {
        var vm = CreateViewModel();

        vm.SourceText = "Salom dunyo";

        Assert.Contains("11", vm.SourceSummary);
        Assert.Contains("2", vm.SourceSummary);
    }

    // =================================================================================
    //  Fayl rejimi
    // =================================================================================

    [Fact]
    public void Only_supported_files_reach_the_list()
    {
        var service = CreateFileService();
        var vm = CreateViewModel(service);

        var word = _temp.WriteFile("hujjat.docx", "x");
        var image = _temp.WriteFile("rasm.png", "x");

        vm.DropFilesCommand.Execute(new[] { word, image });

        Assert.Equal(word, Assert.Single(vm.Files).Path);
        Assert.Contains("mos emas", vm.StatusMessage);
    }

    [Fact]
    public void The_same_file_is_not_added_twice()
    {
        var vm = CreateViewModel(CreateFileService());
        var word = _temp.WriteFile("hujjat.docx", "x");

        vm.DropFilesCommand.Execute(new[] { word });
        vm.DropFilesCommand.Execute(new[] { word });

        Assert.Single(vm.Files);
    }

    [Fact]
    public async Task Every_file_is_converted_and_the_result_can_be_shown_in_explorer()
    {
        var service = CreateFileService();
        var vm = CreateViewModel(service);

        var first = _temp.WriteFile("bir.docx", "x");
        var second = _temp.WriteFile("ikki.docx", "x");

        service
            .ConvertFileAsync(first, Arg.Any<string?>(), Arg.Any<TransliterationOptions>(), Arg.Any<IProgress<PdfProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result(first, _temp.At("bir-lotin.docx")));

        service
            .ConvertFileAsync(second, Arg.Any<string?>(), Arg.Any<TransliterationOptions>(), Arg.Any<IProgress<PdfProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result(second, _temp.At("ikki-lotin.docx")));

        vm.DropFilesCommand.Execute(new[] { first, second });

        await vm.ConvertFilesCommand.ExecuteAsync(null);

        Assert.All(vm.Files, file => Assert.True(file.IsDone));
        Assert.Contains("2 ta fayl o'girildi", vm.StatusMessage);
        Assert.Equal(_temp.At("ikki-lotin.docx"), vm.LastResultPath);
        Assert.True(vm.RevealResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task One_broken_file_does_not_stop_the_rest()
    {
        var service = CreateFileService();
        var vm = CreateViewModel(service);

        var broken = _temp.WriteFile("siniq.docx", "x");
        var good = _temp.WriteFile("yaxshi.docx", "x");

        service
            .ConvertFileAsync(broken, Arg.Any<string?>(), Arg.Any<TransliterationOptions>(), Arg.Any<IProgress<PdfProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new PdfServiceException(PdfErrorKind.CorruptedDocument, "Hujjat shikastlangan"));

        service
            .ConvertFileAsync(good, Arg.Any<string?>(), Arg.Any<TransliterationOptions>(), Arg.Any<IProgress<PdfProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result(good, _temp.At("yaxshi-lotin.docx")));

        vm.DropFilesCommand.Execute(new[] { broken, good });

        await vm.ConvertFilesCommand.ExecuteAsync(null);

        var brokenRow = vm.Files.Single(file => file.Path == broken);
        var goodRow = vm.Files.Single(file => file.Path == good);

        Assert.True(brokenRow.HasError);
        Assert.Equal("Hujjat shikastlangan", brokenRow.StatusText);
        Assert.True(goodRow.IsDone);

        // Xato qatorda ko'rinadi, oyna chiqmaydi: qolgan fayllar ustidan ish davom etadi.
        Assert.Empty(_dialogs.ShownErrors);
        Assert.Contains("1 ta fayl o'girildi", vm.StatusMessage);
    }

    [Fact]
    public void An_empty_list_has_nothing_to_convert()
    {
        var vm = CreateViewModel(CreateFileService());

        Assert.False(vm.ConvertFilesCommand.CanExecute(null));
        Assert.False(vm.ClearFilesCommand.CanExecute(null));
    }

    [Fact]
    public void The_output_folder_can_be_sent_back_to_the_source_folder()
    {
        var vm = CreateViewModel(CreateFileService());

        vm.OutputFolder = _temp.Root;
        vm.UseSourceFolderCommand.Execute(null);

        Assert.Null(vm.OutputFolder);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>Fayl rejimi uchun mock: qabul qilish qoidasi haqiqiy servisdagidek.</summary>
    private static ITransliterationService CreateFileService()
    {
        var service = Substitute.For<ITransliterationService>();
        var real = new TransliterationService();

        service.IsSupported(Arg.Any<string?>()).Returns(call => real.IsSupported(call.Arg<string?>()));
        service.OpenFilter.Returns(real.OpenFilter);

        return service;
    }

    private static Task<TransliterationFileResult> Result(string source, string output)
        => Task.FromResult(new TransliterationFileResult(
            source, output, TransliterationDirection.CyrillicToLatin, 100));
}
