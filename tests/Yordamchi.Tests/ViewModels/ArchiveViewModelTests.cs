using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="ArchiveViewModel"/> sinovlari. Arxivning o'zi bu yerda yaratilmaydi —
/// <see cref="IArchiveService"/> o'rniga substitute turadi, chunki tekshirilayotgan narsa
/// <b>sahifaning qoidalari</b>: tugma qachon faollashadi, servisga nima uzatiladi va xatodan
/// keyin sahifa qanday holatda qoladi.
/// </summary>
public sealed class ArchiveViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly IArchiveService _archive = Substitute.For<IArchiveService>();
    private readonly FakeDialogService _dialogs = new();
    private readonly ArchiveViewModel _vm;

    public ArchiveViewModelTests()
    {
        _archive.OpenFilter.Returns("Arxivlar|*.zip");
        _vm = new ArchiveViewModel(_archive, _dialogs);
    }

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Rejim
    // =================================================================================

    [Fact]
    public void The_page_opens_in_archiving_mode()
    {
        Assert.Equal(ArchiveMode.Create, _vm.Mode);
        Assert.True(_vm.IsCreateMode);
        Assert.False(_vm.IsExtractMode);
    }

    [Fact]
    public void Switching_mode_clears_the_previous_result()
    {
        // Aks holda "Arxivlash" dan keyin "Ochish" ga o'tganda pastda hali ham eski
        // arxivning "Papkada ko'rsatish" tugmasi turardi.
        _vm.LastResultPath = _temp.At("eski.zip");
        _vm.StatusMessage = "3 ta fayl arxivlandi";

        _vm.ShowExtractCommand.Execute(null);

        Assert.True(_vm.IsExtractMode);
        Assert.Null(_vm.LastResultPath);
        Assert.False(_vm.HasResult);
        Assert.Equal(string.Empty, _vm.StatusMessage);
    }

    // =================================================================================
    //  Manbalarni yig'ish
    // =================================================================================

    [Fact]
    public void Dropping_files_and_folders_adds_both()
    {
        var file = _temp.WriteFile("a.txt", "x");
        var folder = _temp.CreateFolder("papka");

        _vm.DropSourcesCommand.Execute(new[] { file, folder });

        Assert.Equal(2, _vm.Sources.Count);
        Assert.Contains(_vm.Sources, source => source.Path == file && !source.IsFolder);
        Assert.Contains(_vm.Sources, source => source.Path == folder && source.IsFolder);
    }

    [Fact]
    public void The_same_path_is_not_added_twice()
    {
        var file = _temp.WriteFile("a.txt", "x");

        _vm.DropSourcesCommand.Execute(new[] { file });
        _vm.DropSourcesCommand.Execute(new[] { file });

        Assert.Single(_vm.Sources);
        Assert.Contains("ro'yxatda bor", _vm.StatusMessage);
    }

    [Fact]
    public void Paths_that_do_not_exist_are_ignored()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.At("yo-q.txt") });

        Assert.Empty(_vm.Sources);
    }

    [Fact]
    public void The_summary_counts_files_inside_dropped_folders()
    {
        _temp.WriteFile("papka/bir.txt", new string('x', 100));
        _temp.WriteFile("papka/ichki/ikki.txt", new string('y', 200));

        _vm.DropSourcesCommand.Execute(new[] { _temp.At("papka") });

        Assert.Contains("1 ta element", _vm.SourcesSummary);
        Assert.Contains("2 ta fayl", _vm.SourcesSummary);
        Assert.Equal(300, _vm.Sources[0].SizeBytes);
    }

    [Fact]
    public void Removing_a_row_updates_the_summary_and_the_buttons()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });

        _vm.Sources[0].RemoveCommand.Execute(null);

        Assert.Empty(_vm.Sources);
        Assert.False(_vm.HasSources);
        Assert.False(_vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void Clear_empties_the_list()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });

        Assert.True(_vm.ClearSourcesCommand.CanExecute(null));
        _vm.ClearSourcesCommand.Execute(null);

        Assert.Empty(_vm.Sources);
        Assert.False(_vm.ClearSourcesCommand.CanExecute(null));
    }

    // =================================================================================
    //  Parol qoidalari
    // =================================================================================

    [Fact]
    public void Archiving_is_blocked_until_something_is_selected()
    {
        Assert.False(_vm.CreateCommand.CanExecute(null));

        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });

        Assert.True(_vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void Archiving_is_blocked_while_the_two_passwords_differ()
    {
        // Xato yozilgan parol bilan arxivlangan fayl — foydalanuvchi o'z ma'lumotini
        // qaytarib ololmaydigan holat. Shuning uchun tugma qat'iy bloklanadi.
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _vm.UsePassword = true;

        _vm.Password = "Parol123";
        _vm.ConfirmPassword = "Parol124";
        Assert.False(_vm.PasswordsMatch);
        Assert.False(_vm.CreateCommand.CanExecute(null));

        _vm.ConfirmPassword = "Parol123";
        Assert.True(_vm.PasswordsMatch);
        Assert.True(_vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void Archiving_is_blocked_when_the_password_box_is_empty()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _vm.UsePassword = true;

        Assert.False(_vm.CreateCommand.CanExecute(null));
        Assert.Equal("Parolni kiriting.", _vm.PasswordHint);
    }

    [Fact]
    public void Turning_the_password_off_unblocks_archiving_again()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _vm.UsePassword = true;
        _vm.Password = "abc";

        Assert.False(_vm.CreateCommand.CanExecute(null));

        _vm.UsePassword = false;

        Assert.True(_vm.CreateCommand.CanExecute(null));
        Assert.Equal(string.Empty, _vm.PasswordHint);
    }

    [Fact]
    public void The_hint_explains_the_compatibility_trade_off_of_each_encryption()
    {
        _vm.UsePassword = true;
        _vm.Password = "Parol123";
        _vm.ConfirmPassword = "Parol123";

        _vm.Encryption = ZipEncryption.Aes256;
        Assert.Contains("Windows Explorer", _vm.PasswordHint);

        _vm.Encryption = ZipEncryption.ZipCrypto;
        Assert.Contains("zaif", _vm.PasswordHint);
    }

    // =================================================================================
    //  Arxivlash
    // =================================================================================

    [Fact]
    public async Task Cancelling_the_save_dialog_calls_no_service()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        // SaveFileResults bo'sh — dialog null qaytaradi, ya'ni foydalanuvchi bekor qildi.

        await _vm.CreateCommand.ExecuteAsync(null);

        await _archive.DidNotReceiveWithAnyArgs()
            .CreateZipAsync(default!, default!, default, default, default);
        Assert.False(_vm.HasResult);
    }

    [Fact]
    public async Task Archiving_passes_the_chosen_settings_to_the_service()
    {
        var file = _temp.WriteFile("a.txt", "x");
        var target = _temp.At("natija.zip");

        _vm.DropSourcesCommand.Execute(new[] { file });
        _vm.CompressionLevel = ArchiveCompressionLevel.Maximum;
        _vm.KeepFolderStructure = false;
        _vm.UsePassword = true;
        _vm.Password = "Parol123";
        _vm.ConfirmPassword = "Parol123";
        _vm.Encryption = ZipEncryption.ZipCrypto;

        _dialogs.SaveFileResults.Enqueue(target);
        _archive.CreateZipAsync(default!, default!, default, default, default)
            .ReturnsForAnyArgs(1);

        await _vm.CreateCommand.ExecuteAsync(null);

        await _archive.Received(1).CreateZipAsync(
            Arg.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths[0] == file),
            target,
            Arg.Is<CreateArchiveOptions>(options =>
                options.Level == ArchiveCompressionLevel.Maximum
                && !options.KeepFolderStructure
                && options.Password == "Parol123"
                && options.Encryption == ZipEncryption.ZipCrypto),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_password_is_sent_when_the_checkbox_is_off()
    {
        // Parol yozilib, keyin belgi olib tashlangan bo'lsa ham arxiv shifrlanmasligi kerak.
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _vm.Password = "unutilgan";
        _vm.UsePassword = false;

        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.zip"));
        _archive.CreateZipAsync(default!, default!, default, default, default).ReturnsForAnyArgs(1);

        await _vm.CreateCommand.ExecuteAsync(null);

        await _archive.Received(1).CreateZipAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            Arg.Is<CreateArchiveOptions>(options => options.Password == null),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_successful_archive_offers_to_reveal_the_file()
    {
        var target = _temp.WriteFile("natija.zip", "soxta arxiv");

        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _dialogs.SaveFileResults.Enqueue(target);
        _archive.CreateZipAsync(default!, default!, default, default, default).ReturnsForAnyArgs(3);

        await _vm.CreateCommand.ExecuteAsync(null);

        Assert.True(_vm.HasResult);
        Assert.Equal(target, _vm.LastResultPath);
        Assert.Contains("3 ta fayl arxivlandi", _vm.StatusMessage);

        _vm.RevealResultCommand.Execute(null);
        Assert.Equal([target], _dialogs.RevealedPaths);
    }

    [Fact]
    public async Task A_failed_archive_shows_the_error_and_offers_nothing_to_reveal()
    {
        _vm.DropSourcesCommand.Execute(new[] { _temp.WriteFile("a.txt", "x") });
        _dialogs.SaveFileResults.Enqueue(_temp.At("natija.zip"));

        _archive.CreateZipAsync(default!, default!, default, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OutputNotWritable, "Diskda joy yo'q"));

        await _vm.CreateCommand.ExecuteAsync(null);

        Assert.False(_vm.HasResult);
        Assert.Single(_dialogs.ShownErrors);
        Assert.Contains("Diskda joy yo'q", _dialogs.ShownErrors[0]);
        Assert.False(_vm.IsBusy);
    }

    // =================================================================================
    //  Arxivdan ochish
    // =================================================================================

    [Fact]
    public async Task Opening_an_archive_lists_its_files_and_selects_them_all()
    {
        var archive = _temp.WriteFile("manba.zip", "soxta");
        _dialogs.OpenFileResults.Enqueue(archive);
        _archive.ReadAsync(default!, default, default).ReturnsForAnyArgs(ArchiveWith(
            Entry("papka/bir.txt", 100),
            Entry("papka/ikki.txt", 200)));

        await _vm.OpenArchiveCommand.ExecuteAsync(null);

        Assert.Equal(2, _vm.Entries.Count);
        Assert.Equal(2, _vm.SelectedCount);
        Assert.True(_vm.HasEntries);
        Assert.Equal("manba.zip", _vm.ArchiveName);
        Assert.Contains("ZIP", _vm.ArchiveSummary);
    }

    [Fact]
    public async Task Folder_entries_are_not_listed_as_files()
    {
        // Papka yozuvi ro'yxatda chalkashtiradi va "chiqarish" sanog'ini buzadi.
        SetArchive(ArchiveWith(
            new ArchiveEntryInfo("papka/", 0, 0, null, IsDirectory: true, false),
            Entry("papka/bir.txt", 10)));

        await LoadArchive();

        Assert.Single(_vm.Entries);
        Assert.Equal("bir.txt", _vm.Entries[0].Name);
        Assert.Equal("papka", _vm.Entries[0].FolderPath);
    }

    [Fact]
    public async Task An_encrypted_archive_turns_the_password_box_on()
    {
        SetArchive(ArchiveWith(true, Entry("maxfiy.txt", 10, encrypted: true)));

        await LoadArchive();

        Assert.True(_vm.NeedsPassword);
        Assert.Contains("parol bilan himoyalangan", _vm.ArchiveSummary);
    }

    [Fact]
    public async Task Opening_a_locked_archive_without_a_password_keeps_the_password_box_open()
    {
        // Foydalanuvchi parolni kiritib qayta urinishi kerak — sahifa "hech narsa
        // bo'lmagandek" holatga qaytmasligi shart.
        _archive.ReadAsync(default!, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.PasswordProtected, "Parol kerak"));

        _dialogs.OpenFileResults.Enqueue(_temp.WriteFile("qulflangan.zip", "soxta"));
        await _vm.OpenArchiveCommand.ExecuteAsync(null);

        Assert.True(_vm.NeedsPassword);
        Assert.Empty(_vm.Entries);
        Assert.Single(_dialogs.ShownErrors);
    }

    [Fact]
    public async Task Reload_sends_the_typed_password()
    {
        SetArchive(ArchiveWith(Entry("a.txt", 10)));
        await LoadArchive();

        _vm.ExtractPassword = "Parol123";
        await _vm.ReloadCommand.ExecuteAsync(null);

        await _archive.Received().ReadAsync(Arg.Any<string>(), "Parol123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_password_box_is_sent_as_no_password()
    {
        SetArchive(ArchiveWith(Entry("a.txt", 10)));

        await LoadArchive();

        await _archive.Received().ReadAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opening_an_archive_suggests_a_folder_named_after_it()
    {
        SetArchive(ArchiveWith(Entry("a.txt", 10)));

        await LoadArchive("hisobotlar.zip");

        Assert.Equal(_temp.At("hisobotlar"), _vm.TargetFolder);
    }

    // =================================================================================
    //  Tanlov va chiqarish
    // =================================================================================

    [Fact]
    public async Task Extracting_everything_sends_no_entry_list_at_all()
    {
        // Hammasi tanlanganda minglab yo'lni uzatish behuda — servis butun arxivni chiqaradi.
        SetArchive(ArchiveWith(Entry("bir.txt", 10), Entry("ikki.txt", 10)));
        await LoadArchive();
        _archive.ExtractAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(2);

        await _vm.ExtractCommand.ExecuteAsync(null);

        await _archive.Received(1).ExtractAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            null,
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Extracting_a_subset_sends_exactly_the_ticked_entries()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10), Entry("ikki.txt", 10), Entry("uch.txt", 10)));
        await LoadArchive();
        _archive.ExtractAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(1);

        _vm.Entries[0].IsSelected = false;
        _vm.Entries[2].IsSelected = false;

        Assert.Equal(1, _vm.SelectedCount);

        await _vm.ExtractCommand.ExecuteAsync(null);

        await _archive.Received(1).ExtractAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Is<IReadOnlyCollection<string>>(paths => paths.Count == 1 && paths.Single() == "ikki.txt"),
            Arg.Any<IProgress<PdfProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Extracting_is_blocked_when_nothing_is_ticked()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10)));
        await LoadArchive();

        _vm.ClearEntrySelectionCommand.Execute(null);

        Assert.Equal(0, _vm.SelectedCount);
        Assert.False(_vm.ExtractCommand.CanExecute(null));

        _vm.SelectAllEntriesCommand.Execute(null);

        Assert.Equal(1, _vm.SelectedCount);
        Assert.True(_vm.ExtractCommand.CanExecute(null));
    }

    [Fact]
    public async Task Extracting_is_blocked_without_a_target_folder()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10)));
        await LoadArchive();

        _vm.TargetFolder = "   ";

        Assert.False(_vm.ExtractCommand.CanExecute(null));
    }

    [Fact]
    public void Extracting_is_blocked_before_an_archive_is_opened() =>
        Assert.False(_vm.ExtractCommand.CanExecute(null));

    [Fact]
    public async Task A_wrong_password_during_extraction_reopens_the_password_box()
    {
        SetArchive(ArchiveWith(true, Entry("maxfiy.txt", 10, encrypted: true)));
        await LoadArchive();

        _archive.ExtractAsync(default!, default!, default, default, default, default)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.InvalidPassword, "Parol to'g'ri kelmadi"));

        await _vm.ExtractCommand.ExecuteAsync(null);

        Assert.True(_vm.NeedsPassword);
        Assert.False(_vm.HasResult);
        Assert.Contains("Parol to'g'ri kelmadi", _dialogs.ShownErrors[0]);
    }

    [Fact]
    public async Task A_successful_extraction_offers_to_open_the_folder()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10)));
        await LoadArchive();
        _archive.ExtractAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(7);

        await _vm.ExtractCommand.ExecuteAsync(null);

        Assert.Equal(_vm.TargetFolder, _vm.LastResultPath);
        Assert.Contains("7 ta fayl chiqarildi", _vm.StatusMessage);

        _vm.RevealResultCommand.Execute(null);
        Assert.Single(_dialogs.RevealedPaths);
    }

    [Fact]
    public async Task Picking_a_target_folder_replaces_the_suggestion()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10)));
        await LoadArchive();

        var chosen = _temp.CreateFolder("men-tanladim");
        _dialogs.SelectFolderResults.Enqueue(chosen);

        _vm.PickTargetFolderCommand.Execute(null);

        Assert.Equal(chosen, _vm.TargetFolder);
    }

    [Fact]
    public async Task Cancelling_the_folder_dialog_keeps_the_previous_choice()
    {
        SetArchive(ArchiveWith(Entry("bir.txt", 10)));
        await LoadArchive();

        var before = _vm.TargetFolder;
        _vm.PickTargetFolderCommand.Execute(null); // navbat bo'sh → null qaytadi

        Assert.Equal(before, _vm.TargetFolder);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private void SetArchive(ArchiveInfo info) =>
        _archive.ReadAsync(default!, default, default).ReturnsForAnyArgs(info);

    private async Task LoadArchive(string fileName = "manba.zip")
    {
        _dialogs.OpenFileResults.Enqueue(_temp.WriteFile(fileName, "soxta"));
        await _vm.OpenArchiveCommand.ExecuteAsync(null);
    }

    private static ArchiveEntryInfo Entry(string path, long size, bool encrypted = false) =>
        new(path, size, size / 2, new DateTime(2026, 1, 1), false, encrypted);

    private static ArchiveInfo ArchiveWith(params ArchiveEntryInfo[] entries) =>
        ArchiveWith(false, entries);

    private static ArchiveInfo ArchiveWith(bool encrypted, params ArchiveEntryInfo[] entries) =>
        new(ArchiveFormat.Zip, entries, entries.Sum(entry => entry.Size), encrypted);
}
