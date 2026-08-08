using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>"Kirill ↔ Lotin" bo'limining ikki rejimi.</summary>
public enum TransliterationMode
{
    /// <summary>Matnni to'g'ridan-to'g'ri yozib yoki qo'yib o'girish.</summary>
    Text,

    /// <summary>Word hujjatlari va matn fayllarini o'girish.</summary>
    Files
}

/// <summary>
/// "Kirill ↔ Lotin" bo'limi: matnni darhol o'giradi yoki Word/matn fayllarini yangi faylga yozadi.
/// <para>
/// Arxiv va ekran yozuvi kabi, bu ham bosh sahifadagi PDF kartochkalari orasida emas — yon
/// paneldagi alohida bo'lim: <see cref="IPdfEngineService"/> quvuriga umuman kirmaydi va o'z
/// servisiga (<see cref="ITransliterationService"/>) to'g'ridan-to'g'ri murojaat qiladi.
/// </para>
/// </summary>
public sealed partial class TransliterationViewModel : ViewModelBase
{
    /// <summary>
    /// Shu hajmdan katta matn har bosishda o'girilmaydi — foydalanuvchi "O'girish" tugmasini
    /// bosadi. Chegara katta qo'yilgan: odatdagi maqola bemalol jonli o'giriladi.
    /// </summary>
    private const int LiveConversionLimit = 100_000;

    private readonly ITransliterationService _transliteration;

    public TransliterationViewModel(ITransliterationService transliteration, IDialogService dialogService)
        : base(dialogService)
    {
        _transliteration = transliteration;

        Files.CollectionChanged += OnFilesChanged;
    }

    public override string Title => "Kirill ↔ Lotin";

    public override string Description =>
        "Matnni yoki Word hujjatini kirilldan lotinga va aksincha o'giring.";

    // =================================================================================
    //  Rejim
    // =================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode))]
    [NotifyPropertyChangedFor(nameof(IsFilesMode))]
    private TransliterationMode _mode = TransliterationMode.Text;

    public bool IsTextMode => Mode == TransliterationMode.Text;

    public bool IsFilesMode => Mode == TransliterationMode.Files;

    [RelayCommand]
    private void ShowText() => Mode = TransliterationMode.Text;

    [RelayCommand]
    private void ShowFiles() => Mode = TransliterationMode.Files;

    partial void OnModeChanged(TransliterationMode value)
    {
        StatusMessage = string.Empty;
        LastResultPath = null;
        RefreshCommands();
    }

    // =================================================================================
    //  Yo'nalish va sozlamalar
    // =================================================================================

    /// <summary>
    /// Yo'nalish matnning o'zidan aniqlanadimi. Standart holat — ha: foydalanuvchi ko'pincha
    /// matnni shunchaki qo'yadi va tugma qidirib o'tirishni istamaydi.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionHint))]
    [NotifyPropertyChangedFor(nameof(DirectionLabel))]
    private bool _autoDetectDirection = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionHint))]
    [NotifyPropertyChangedFor(nameof(DirectionLabel))]
    private TransliterationDirection _direction = TransliterationDirection.CyrillicToLatin;

    [ObservableProperty]
    private ApostropheStyle _apostrophe = ApostropheStyle.Ascii;

    /// <summary>Manba matndan aniqlangan yo'nalish; hech nima aniqlanmasa <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionHint))]
    [NotifyPropertyChangedFor(nameof(DirectionLabel))]
    private TransliterationDirection? _detectedDirection;

    /// <summary>Yo'nalish tugmasidagi matn: amalda qo'llanadigan yo'nalish.</summary>
    public string DirectionLabel => Describe(EffectiveDirection);

    /// <summary>Yo'nalish tugmasining izohi: qaysi yo'nalish va nima uchun tanlangani.</summary>
    public string DirectionHint
    {
        get
        {
            if (!AutoDetectDirection)
                return $"Qo'lda tanlangan: {Describe(Direction)}. Almashtirish uchun bosing.";

            return DetectedDirection is null
                ? "Yo'nalish matndan o'zi aniqlanadi. Qo'lda tanlash uchun bosing."
                : $"Matndan aniqlandi: {Describe(DetectedDirection.Value)}. Almashtirish uchun bosing.";
        }
    }

    /// <summary>Amalda qo'llanadigan yo'nalish — natija fayl nomi ham shunga qarab tanlanadi.</summary>
    public TransliterationDirection EffectiveDirection =>
        AutoDetectDirection ? DetectedDirection ?? Direction : Direction;

    /// <summary>
    /// Yo'nalishni teskarisiga buradi. Tanlash avtomatik holatdan chiqaradi: aks holda
    /// keyingi harfda aniqlagich foydalanuvchining tanlovini bekor qilardi.
    /// </summary>
    [RelayCommand]
    private void ToggleDirection()
    {
        Direction = EffectiveDirection == TransliterationDirection.CyrillicToLatin
            ? TransliterationDirection.LatinToCyrillic
            : TransliterationDirection.CyrillicToLatin;

        AutoDetectDirection = false;
    }

    private TransliterationOptions BuildOptions() => new()
    {
        Direction = Direction,
        Apostrophe = Apostrophe,
        AutoDetectDirection = AutoDetectDirection
    };

    partial void OnAutoDetectDirectionChanged(bool value) => ConvertPreview();

    partial void OnDirectionChanged(TransliterationDirection value) => ConvertPreview();

    partial void OnApostropheChanged(ApostropheStyle value) => ConvertPreview();

    private static string Describe(TransliterationDirection direction) =>
        direction == TransliterationDirection.CyrillicToLatin
            ? "Kirill → Lotin"
            : "Lotin → Kirill";

    // =================================================================================
    //  1-rejim: matn
    // =================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceSummary))]
    [NotifyCanExecuteChangedFor(nameof(ConvertTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearTextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultText))]
    [NotifyCanExecuteChangedFor(nameof(CopyResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapCommand))]
    private string _resultText = string.Empty;

    public bool HasResultText => !string.IsNullOrEmpty(ResultText);

    /// <summary>Manba maydoni ostidagi hisob: "1 240 belgi · 187 so'z".</summary>
    public string SourceSummary
    {
        get
        {
            if (string.IsNullOrEmpty(SourceText))
                return string.Empty;

            var words = SourceText.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

            return $"{SourceText.Length:N0} belgi · {words:N0} so'z";
        }
    }

    partial void OnSourceTextChanged(string value)
    {
        DetectedDirection = _transliteration.DetectDirection(value);
        ConvertPreview();
    }

    /// <summary>
    /// Manba matnni natija maydoniga o'giradi. Har bosishda chaqiriladi — shuning uchun juda
    /// katta matnda qo'lda ishga tushirishga qoldiriladi (<see cref="LiveConversionLimit"/>).
    /// </summary>
    private void ConvertPreview()
    {
        if (!IsTextMode)
            return;

        if (SourceText.Length > LiveConversionLimit)
        {
            StatusMessage = "Matn juda katta — natijani ko'rish uchun \"O'girish\" tugmasini bosing.";
            return;
        }

        ResultText = _transliteration.ConvertText(SourceText, BuildOptions());
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(HasSourceText))]
    private void ConvertText()
    {
        ResultText = _transliteration.ConvertText(SourceText, BuildOptions());
        StatusMessage = $"{ResultText.Length:N0} belgi o'girildi";
    }

    private bool HasSourceText() => !string.IsNullOrEmpty(SourceText);

    /// <summary>
    /// Natijani manba o'rniga qo'yadi va yo'nalishni teskarisiga buradi — o'girilgan matnni
    /// darhol qaytarib tekshirish uchun eng qisqa yo'l.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSwap))]
    private void Swap()
    {
        var previous = ResultText;

        // Almashtirish aniq yo'nalishni talab qiladi: aks holda avtomatik aniqlash matnni
        // darhol yana ortga o'girib yuborardi.
        Direction = EffectiveDirection == TransliterationDirection.CyrillicToLatin
            ? TransliterationDirection.LatinToCyrillic
            : TransliterationDirection.CyrillicToLatin;

        AutoDetectDirection = false;
        SourceText = previous;
    }

    private bool CanSwap() => HasResultText;

    [RelayCommand(CanExecute = nameof(HasResultText))]
    private void CopyResult()
    {
        DialogService.SetClipboardText(ResultText);
        StatusMessage = "Natija vaqtinchalik xotiraga (clipboard) ko'chirildi.";
    }

    [RelayCommand(CanExecute = nameof(HasSourceText))]
    private void ClearText()
    {
        SourceText = string.Empty;
        ResultText = string.Empty;
        StatusMessage = string.Empty;
    }

    // =================================================================================
    //  2-rejim: fayllar
    // =================================================================================

    public ObservableCollection<TransliterationFileViewModel> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public string FilesSummary => Files.Count == 0
        ? string.Empty
        : $"{Files.Count} ta fayl · {FormatSize(Files.Sum(file => file.SizeBytes))}";

    /// <summary>Natija fayllari yoziladigan papka; bo'sh bo'lsa har bir fayl o'z papkasida qoladi.</summary>
    [ObservableProperty]
    private string? _outputFolder;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddFiles()
    {
        var files = DialogService.OpenFiles("O'giriladigan hujjatlarni tanlang", _transliteration.OpenFilter);

        if (files is not null)
            AddPaths(files);
    }

    /// <summary>Explorer'dan tashlangan fayllar.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void DropFiles(string[]? paths)
    {
        if (paths is { Length: > 0 })
            AddPaths(paths);
    }

    [RelayCommand(CanExecute = nameof(CanClearFiles))]
    private void ClearFiles()
    {
        Files.Clear();
        StatusMessage = string.Empty;
        LastResultPath = null;
    }

    private bool CanClearFiles() => IsIdle && HasFiles;

    private void AddPaths(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var skipped = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            // Mos kelmaydigan faylni ro'yxatga qo'shib, keyin har birida xato ko'rsatishdan
            // ko'ra uni darhol chetlab o'tgan ma'qul.
            if (!_transliteration.IsSupported(path))
            {
                skipped++;
                continue;
            }

            if (!known.Add(path))
                continue;

            Files.Add(new TransliterationFileViewModel(path, file => Files.Remove(file)));
            added++;
        }

        StatusMessage = (added, skipped) switch
        {
            (0, 0) => "Yangi fayl qo'shilmadi — ular ro'yxatda bor.",
            (0, _) => "Faqat Word (.docx) va matn (.txt) fayllarini o'girib bo'ladi.",
            (_, 0) => $"{added} ta fayl qo'shildi",
            _ => $"{added} ta fayl qo'shildi · {skipped} tasi mos emas"
        };
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void PickOutputFolder()
    {
        var folder = DialogService.SelectFolder("Natija papkasini tanlang", OutputFolder);

        if (folder is not null)
            OutputFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void UseSourceFolder()
    {
        OutputFolder = null;
        StatusMessage = "Natija har bir faylning o'z papkasiga yoziladi.";
    }

    [RelayCommand(CanExecute = nameof(CanConvertFiles))]
    private async Task ConvertFilesAsync()
    {
        var files = Files.ToList();
        var options = BuildOptions();

        foreach (var file in files)
            file.ResetStatus();

        var succeeded = 0;
        var failed = 0;
        string? lastOutput = null;

        await RunAsync(
            "Fayllar o'girilmoqda…",
            async (progress, token) =>
            {
                for (var index = 0; index < files.Count; index++)
                {
                    token.ThrowIfCancellationRequested();

                    var file = files[index];
                    progress.Report(new PdfProgress(index, files.Count, file.Name));

                    try
                    {
                        // Natija nomini servis tanlaydi: yo'nalish har bir faylda alohida
                        // aniqlanishi mumkin, ya'ni nomni bu yerda oldindan yasab bo'lmaydi.
                        var result = await _transliteration
                            .ConvertFileAsync(file.Path, OutputFolder, options, null, token)
                            .ConfigureAwait(true);

                        file.MarkDone(Path.GetFileName(result.OutputPath));
                        lastOutput = result.OutputPath;
                        succeeded++;
                    }
                    catch (PdfServiceException ex)
                    {
                        // Bitta shikastlangan fayl qolganlarini to'xtatib qo'ymasligi kerak:
                        // xato o'sha qatorda ko'rinadi, ro'yxat esa oxirigacha boradi.
                        file.MarkFailed(ex.Message);
                        failed++;
                    }
                }

                progress.Report(new PdfProgress(files.Count, files.Count, "Tayyor"));
            });

        if (lastOutput is not null)
            LastResultPath = lastOutput;

        StatusMessage = (succeeded, failed) switch
        {
            (0, 0) => "Hech narsa o'girilmadi.",
            (_, 0) => $"{succeeded} ta fayl o'girildi",
            (0, _) => $"{failed} ta faylni o'girib bo'lmadi — sabab ro'yxatda ko'rsatilgan.",
            _ => $"{succeeded} ta fayl o'girildi · {failed} tasi xato berdi"
        };
    }

    private bool CanConvertFiles() => IsIdle && HasFiles;

    // =================================================================================
    //  Natija
    // =================================================================================

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
        DropFilesCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
        PickOutputFolderCommand.NotifyCanExecuteChanged();
        UseSourceFolderCommand.NotifyCanExecuteChanged();
        ConvertFilesCommand.NotifyCanExecuteChanged();
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(FilesSummary));
        RefreshCommands();
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
