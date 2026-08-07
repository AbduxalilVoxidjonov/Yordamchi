using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>Arxiv sahifasining ikki rejimi.</summary>
public enum ArchiveMode
{
    /// <summary>Fayl va papkalardan yangi <c>.zip</c> yig'ish.</summary>
    Create,

    /// <summary>Mavjud arxivni ochib, ichidagilarni papkaga chiqarish.</summary>
    Extract
}

/// <summary>
/// "Arxiv" bo'limi: bitta sahifada ikkita rejim — arxivlash va arxivdan ochish.
/// <para>
/// Ekran yozuvi kabi, bu ham PDF vositalari kartochkalari orasida emas, yon panelda alohida
/// bo'lim. Sababi bir xil: u <see cref="IPdfEngineService"/> quvuriga umuman kirmaydi va
/// o'z servisiga (<see cref="IArchiveService"/>) to'g'ridan-to'g'ri murojaat qiladi.
/// </para>
/// </summary>
public sealed partial class ArchiveViewModel : ViewModelBase
{
    private readonly IArchiveService _archive;

    public ArchiveViewModel(IArchiveService archive, IDialogService dialogService)
        : base(dialogService)
    {
        _archive = archive;

        Sources.CollectionChanged += OnSourcesChanged;
    }

    public override string Title => "Arxiv";

    public override string Description => "Fayllarni ZIP ga jamlang yoki arxivni oching — parol bilan ham.";

    // =================================================================================
    //  Rejim
    // =================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateMode))]
    [NotifyPropertyChangedFor(nameof(IsExtractMode))]
    private ArchiveMode _mode = ArchiveMode.Create;

    public bool IsCreateMode => Mode == ArchiveMode.Create;

    public bool IsExtractMode => Mode == ArchiveMode.Extract;

    [RelayCommand]
    private void ShowCreate() => Mode = ArchiveMode.Create;

    [RelayCommand]
    private void ShowExtract() => Mode = ArchiveMode.Extract;

    partial void OnModeChanged(ArchiveMode value)
    {
        StatusMessage = string.Empty;
        LastResultPath = null;
        RefreshCommands();
    }

    // =================================================================================
    //  1-rejim: arxivlash
    // =================================================================================

    /// <summary>Arxivga tushadigan fayl va papkalar.</summary>
    public ObservableCollection<ArchiveSourceViewModel> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    /// <summary>Ro'yxat ostidagi jamlama: "3 ta element · 12,4 MB".</summary>
    public string SourcesSummary
    {
        get
        {
            if (Sources.Count == 0)
                return string.Empty;

            var files = Sources.Sum(source => source.FileCount);
            var bytes = Sources.Sum(source => source.SizeBytes);

            return $"{Sources.Count} ta element · {files} ta fayl · {FormatSize(bytes)}";
        }
    }

    [ObservableProperty]
    private ArchiveCompressionLevel _compressionLevel = ArchiveCompressionLevel.Normal;

    /// <summary>Papka qo'shilganda ichki tuzilishi saqlansinmi.</summary>
    [ObservableProperty]
    private bool _keepFolderStructure = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _usePassword;

    /// <summary>
    /// Parol maydonlari ataylab oddiy <c>TextBox</c> ga bog'lanadi: WPF ning <c>PasswordBox</c>
    /// idagi <c>Password</c> xossasi dependency property emas va MVVM ga bog'lanmaydi. Dasturning
    /// "PDF himoyalash" vositasi ham aynan shu yechimni ishlatadi.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    private ZipEncryption _encryption = ZipEncryption.Aes256;

    public bool PasswordsMatch => string.Equals(Password, ConfirmPassword, StringComparison.Ordinal);

    /// <summary>Parol bloki ostidagi izoh: xato bo'lsa ogohlantirish, aks holda moslik haqida eslatma.</summary>
    public string PasswordHint
    {
        get
        {
            if (!UsePassword)
                return string.Empty;

            if (string.IsNullOrEmpty(Password))
                return "Parolni kiriting.";

            if (!PasswordsMatch)
                return "Parollar bir xil emas.";

            return Encryption == ZipEncryption.Aes256
                ? "AES-256 kuchli, lekin Windows Explorer bunday arxivni ocholmaydi — "
                  + "qabul qiluvchida 7-Zip yoki WinRAR bo'lishi kerak."
                : "ZipCrypto Windows Explorer da ham ochiladi, lekin himoyasi zaif — "
                  + "muhim ma'lumot uchun AES-256 ni tanlang.";
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddFiles()
    {
        var files = DialogService.OpenFiles(
            "Arxivga qo'shiladigan fayllarni tanlang",
            "Barcha fayllar (*.*)|*.*");

        if (files is not null)
            AddPaths(files);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddFolder()
    {
        var folder = DialogService.SelectFolder("Arxivga qo'shiladigan papkani tanlang");
        if (folder is not null)
            AddPaths([folder]);
    }

    /// <summary>Explorer'dan tashlangan yo'llar (fayl ham, papka ham).</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void DropSources(string[]? paths)
    {
        if (paths is { Length: > 0 })
            AddPaths(paths);
    }

    [RelayCommand(CanExecute = nameof(CanClearSources))]
    private void ClearSources()
    {
        Sources.Clear();
        StatusMessage = string.Empty;
        LastResultPath = null;
    }

    private bool CanClearSources() => IsIdle && HasSources;

    private void AddPaths(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(
            Sources.Select(source => source.Path), StringComparer.OrdinalIgnoreCase);

        var added = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !known.Add(path))
                continue;

            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            Sources.Add(new ArchiveSourceViewModel(path, item => Sources.Remove(item)));
            added++;
        }

        StatusMessage = added switch
        {
            0 => "Yangi element qo'shilmadi — ular ro'yxatda bor.",
            1 => "1 ta element qo'shildi",
            _ => $"{added} ta element qo'shildi"
        };
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var suggested = Sources.Count == 1
            ? Sources[0].Name
            : "arxiv";

        var target = DialogService.SaveFile(
            "Arxivni saqlash",
            "ZIP arxiv (*.zip)|*.zip|Barcha fayllar (*.*)|*.*",
            Path.GetFileNameWithoutExtension(suggested) + ".zip",
            ".zip");

        if (target is null)
            return;

        var options = new CreateArchiveOptions
        {
            Level = CompressionLevel,
            KeepFolderStructure = KeepFolderStructure,
            Password = UsePassword ? Password : null,
            Encryption = Encryption
        };

        var sources = Sources.Select(source => source.Path).ToList();
        var written = 0;

        var ok = await RunAsync(
            "Arxiv yig'ilmoqda…",
            async (progress, token) =>
            {
                written = await _archive
                    .CreateZipAsync(sources, target, options, progress, token)
                    .ConfigureAwait(true);
            });

        if (!ok)
            return;

        LastResultPath = target;
        StatusMessage = $"{written} ta fayl arxivlandi · {FormatSize(TryGetLength(target))}";
    }

    private bool CanCreate()
    {
        if (!IsIdle || !HasSources)
            return false;

        if (!UsePassword)
            return true;

        return !string.IsNullOrEmpty(Password) && PasswordsMatch;
    }

    // =================================================================================
    //  2-rejim: arxivdan ochish
    // =================================================================================

    /// <summary>Ochilgan arxivdagi yozuvlar.</summary>
    public ObservableCollection<ArchiveEntryViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArchive))]
    [NotifyPropertyChangedFor(nameof(ArchiveName))]
    private string? _archivePath;

    public bool HasArchive => !string.IsNullOrEmpty(ArchivePath);

    public string ArchiveName => string.IsNullOrEmpty(ArchivePath) ? string.Empty : Path.GetFileName(ArchivePath);

    /// <summary>Ro'yxat tepasidagi jamlama: format, fayllar soni va umumiy hajm.</summary>
    [ObservableProperty]
    private string _archiveSummary = string.Empty;

    /// <summary>
    /// Arxiv shifrlangani ma'lum bo'lganda <c>true</c> — parol maydoni shunda ko'rinadi.
    /// Xato "parol kerak" deb qaytganda ham yoqiladi.
    /// </summary>
    [ObservableProperty]
    private bool _needsPassword;

    [ObservableProperty]
    private string _extractPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private string? _targetFolder;

    /// <summary>Faqat belgilangan yozuvlar chiqarilsinmi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private int _selectedCount;

    public string SelectionSummary => Entries.Count == 0
        ? string.Empty
        : $"{SelectedCount} / {Entries.Count} ta yozuv tanlangan";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenArchiveAsync()
    {
        var file = DialogService.OpenFile("Arxivni tanlang", _archive.OpenFilter);
        if (file is null)
            return;

        await LoadArchiveAsync(file);
    }

    /// <summary>Explorer'dan tashlangan arxiv.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DropArchiveAsync(string[]? paths)
    {
        if (paths is { Length: > 0 })
            await LoadArchiveAsync(paths[0]);
    }

    /// <summary>Parol kiritilgandan keyin ro'yxatni qaytadan o'qish.</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (ArchivePath is not null)
            await LoadArchiveAsync(ArchivePath);
    }

    private bool CanReload() => IsIdle && HasArchive;

    private async Task LoadArchiveAsync(string path)
    {
        ArchivePath = path;
        Entries.Clear();
        SelectedCount = 0;
        ArchiveSummary = string.Empty;
        LastResultPath = null;

        // Chiqarish papkasi hali tanlanmagan bo'lsa — arxiv nomidagi papkani taklif qilamiz.
        TargetFolder ??= SuggestTargetFolder(path);

        ArchiveInfo? info = null;

        var ok = await RunAsync(
            "Arxiv o'qilmoqda…",
            async (_, token) =>
            {
                info = await _archive
                    .ReadAsync(path, string.IsNullOrEmpty(ExtractPassword) ? null : ExtractPassword, token)
                    .ConfigureAwait(true);
            });

        if (!ok || info is null)
        {
            // Xato "parol kerak" bo'lsa maydonni ochib qo'yamiz, foydalanuvchi qaytadan urinsin.
            NeedsPassword = true;
            RefreshCommands();
            return;
        }

        foreach (var entry in info.Entries.Where(entry => !entry.IsDirectory))
            Entries.Add(new ArchiveEntryViewModel(entry, UpdateSelectedCount));

        NeedsPassword = info.IsEncrypted;
        SelectedCount = Entries.Count;

        ArchiveSummary = $"{DescribeFormat(info.Format)} · {info.FileCount} ta fayl · {FormatSize(info.TotalSize)}"
            + (info.IsEncrypted ? " · parol bilan himoyalangan" : string.Empty);

        StatusMessage = $"{Entries.Count} ta yozuv o'qildi";

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(SelectionSummary));
        RefreshCommands();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void PickTargetFolder()
    {
        var folder = DialogService.SelectFolder("Fayllar chiqariladigan papkani tanlang", TargetFolder);
        if (folder is not null)
            TargetFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanChangeSelection))]
    private void SelectAllEntries() => SetAllSelected(true);

    [RelayCommand(CanExecute = nameof(CanChangeSelection))]
    private void ClearEntrySelection() => SetAllSelected(false);

    private bool CanChangeSelection() => IsIdle && HasEntries;

    private void SetAllSelected(bool selected)
    {
        foreach (var entry in Entries)
            entry.IsSelected = selected;

        UpdateSelectedCount();
    }

    private void UpdateSelectedCount() => SelectedCount = Entries.Count(entry => entry.IsSelected);

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        if (ArchivePath is null || string.IsNullOrWhiteSpace(TargetFolder))
            return;

        // Hammasi tanlangan bo'lsa ro'yxat uzatilmaydi: servis bu holda arxivni butunligicha
        // chiqaradi va minglab yo'lni solishtirib o'tirmaydi.
        var everything = SelectedCount == Entries.Count;
        var selected = everything
            ? null
            : Entries.Where(entry => entry.IsSelected).Select(entry => entry.Path).ToList();

        var folder = TargetFolder;
        var extracted = 0;

        var ok = await RunAsync(
            "Fayllar chiqarilmoqda…",
            async (progress, token) =>
            {
                extracted = await _archive
                    .ExtractAsync(
                        ArchivePath,
                        folder,
                        string.IsNullOrEmpty(ExtractPassword) ? null : ExtractPassword,
                        selected,
                        progress,
                        token)
                    .ConfigureAwait(true);
            });

        if (!ok)
        {
            NeedsPassword = true;
            return;
        }

        LastResultPath = folder;
        StatusMessage = $"{extracted} ta fayl chiqarildi";
    }

    private bool CanExtract() =>
        IsIdle && HasArchive && HasEntries && SelectedCount > 0 && !string.IsNullOrWhiteSpace(TargetFolder);

    // =================================================================================
    //  Natija
    // =================================================================================

    /// <summary>Oxirgi muvaffaqiyatli natija: yaratilgan arxiv fayli yoki chiqarilgan papka.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(RevealResultCommand))]
    private string? _lastResultPath;

    public bool HasResult => !string.IsNullOrEmpty(LastResultPath);

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void RevealResult()
    {
        if (LastResultPath is not null)
            DialogService.RevealInExplorer(LastResultPath);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    protected override void OnBusyStateChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        DropSourcesCommand.NotifyCanExecuteChanged();
        ClearSourcesCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();

        OpenArchiveCommand.NotifyCanExecuteChanged();
        DropArchiveCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        PickTargetFolderCommand.NotifyCanExecuteChanged();
        SelectAllEntriesCommand.NotifyCanExecuteChanged();
        ClearEntrySelectionCommand.NotifyCanExecuteChanged();
        ExtractCommand.NotifyCanExecuteChanged();
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(SourcesSummary));
        RefreshCommands();
    }

    /// <summary>Arxiv yonida, uning nomi bilan atalgan papka — eng kutilgan joy.</summary>
    private static string SuggestTargetFolder(string archivePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(archivePath);
            var name = Path.GetFileNameWithoutExtension(archivePath);

            return string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.Combine(folder, name);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    private static string DescribeFormat(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => "ZIP",
        ArchiveFormat.Rar => "RAR",
        ArchiveFormat.SevenZip => "7-Zip",
        ArchiveFormat.Tar => "TAR",
        ArchiveFormat.GZip => "GZip",
        ArchiveFormat.BZip2 => "BZip2",
        _ => "Arxiv"
    };

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
