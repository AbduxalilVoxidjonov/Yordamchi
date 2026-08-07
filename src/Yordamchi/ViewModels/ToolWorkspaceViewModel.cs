using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// Universal ishchi oyna: bosh sahifadan tanlangan HAR QANDAY vosita shu bitta ekranda ochiladi.
/// <para>
/// Ekran uch qismdan iborat: manba fayllar (yoki sahifa eskizlari), vositaga xos sozlamalar
/// paneli (<see cref="Options"/>) va bajarish paneli. Vositalar orasidagi farq
/// <see cref="ToolDescriptor"/> va sozlama ViewModel ida jamlangan, shuning uchun yangi vosita
/// qo'shilganda bu sinfda faqat ikkita <c>switch</c> yangilanadi.
/// </para>
/// </summary>
public sealed partial class ToolWorkspaceViewModel : ViewModelBase, IPageItemHost
{
    /// <summary>Eskizlar shu kenglikda bir marta rasterizatsiya qilinadi, keyin faqat UI da masshtablanadi.</summary>
    private const int PageRenderWidth = 320;

    /// <summary>Fayl qatoridagi kichik ko'rinish kengligi.</summary>
    private const int FilePreviewWidth = 220;

    /// <summary>Kattalashtirilgan ko'rish uchun sahifa shu kenglikda qayta chiziladi.</summary>
    private const int PreviewRenderWidth = 1100;

    /// <summary>
    /// Eskizlar ro'yxatga shuncha donadan qo'shiladi. Katta hujjatda (500+ sahifa) hammasini
    /// bir yo'la qo'shish UI oqimini bir necha soniyaga muzlatib qo'yadi.
    /// </summary>
    private const int PageBatchSize = 24;

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    private static readonly string[] WordExtensions = [".docx", ".doc"];

    private static readonly string[] PdfExtensions = [".pdf"];

    private readonly IPdfEngineService _engine;
    private readonly IPdfService _pdfService;

    public ToolWorkspaceViewModel(IPdfEngineService engine, IPdfService pdfService, IDialogService dialogService)
        : base(dialogService)
    {
        _engine = engine;
        _pdfService = pdfService;

        Files.CollectionChanged += OnFilesChanged;
        Pages.CollectionChanged += OnPagesChanged;
    }

    // =================================================================================
    //  Vosita
    // =================================================================================

    /// <summary>Hozir ochiq vosita; <c>null</c> bo'lsa oyna hali faollashtirilmagan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(HasTool))]
    [NotifyPropertyChangedFor(nameof(ToolGlyph))]
    [NotifyPropertyChangedFor(nameof(ToolAccentColor))]
    [NotifyPropertyChangedFor(nameof(ShowsPageGrid))]
    [NotifyPropertyChangedFor(nameof(ShowsFileGallery))]
    [NotifyPropertyChangedFor(nameof(ShowsFileList))]
    [NotifyPropertyChangedFor(nameof(SupportsPageActions))]
    [NotifyPropertyChangedFor(nameof(IsMultiFileTool))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateHint))]
    [NotifyPropertyChangedFor(nameof(OutputHintText))]
    private ToolDescriptor? _tool;

    public override string Title => Tool?.Title ?? "Vosita";

    public override string Description => Tool?.Description ?? string.Empty;

    public bool HasTool => Tool is not null;

    /// <summary>Sarlavhadagi rangli doiradagi ikonka.</summary>
    public string ToolGlyph => Tool?.Glyph ?? string.Empty;

    /// <summary>Vosita rangi (HEX) — XAML da cho'tkaga aylantiriladi.</summary>
    public string ToolAccentColor => Tool?.AccentColor ?? "#2B7FFF";

    /// <summary>Vositaga mos sozlamalar ViewModel i; <c>null</c> bo'lsa panel yashiriladi.</summary>
    [ObservableProperty]
    private object? _options;

    /// <summary>Drag&amp;drop va fayl dialogi qabul qiladigan kengaytmalar: <c>".pdf"</c>.</summary>
    [ObservableProperty]
    private string _acceptedExtensions = ".pdf";

    /// <summary>Pastki paneldagi katta tugma matni ("Birlashtirish", "Siqish" …).</summary>
    [ObservableProperty]
    private string _executeButtonText = "Bajarish";

    /// <summary>Tashqi komponent yetishmasa to'ldiriladi (masalan OCR til fayllari).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrerequisiteWarning))]
    private string? _prerequisiteWarning;

    public bool HasPrerequisiteWarning => !string.IsNullOrWhiteSpace(PrerequisiteWarning);

    /// <summary>
    /// Yetishmayotgan komponentni dastur o'zi yuklab olib bera olsa — qaysi birini.
    /// Ogohlantirish yonidagi "Yuklab olish" tugmasi faqat shu <c>None</c> bo'lmaganda chiqadi.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadableComponent))]
    [NotifyCanExecuteChangedFor(nameof(DownloadMissingComponentCommand))]
    private DownloadableComponent _missingComponent = DownloadableComponent.None;

    public bool HasDownloadableComponent => MissingComponent != DownloadableComponent.None;

    /// <summary>
    /// Vositani almashtiradi: barcha holat tozalanadi, sozlamalar paneli qayta quriladi va
    /// tashqi komponentlar tekshiriladi.
    /// </summary>
    /// <param name="tool">Bosh sahifada tanlangan vosita.</param>
    public void Activate(ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        DetachOptions();

        Tool = tool;
        ClearFiles();
        ClearPages();

        LastResult = null;
        StatusMessage = string.Empty;
        IsPreviewOpen = false;
        PreviewImage = null;

        Options = CreateOptions(tool.Id);
        AttachOptions();

        AcceptedExtensions = DescribeExtensions(tool.Input);
        ExecuteButtonText = DescribeExecuteButton(tool.Id);

        RefreshPrerequisites();
        RefreshCommands();
    }

    /// <summary>"Orqaga" tugmasi bosilganda ko'tariladi; qobiq bosh sahifaga qaytadi.</summary>
    public event EventHandler? BackRequested;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    // =================================================================================
    //  Manba fayllar va sahifalar
    // =================================================================================

    /// <summary>Tanlangan fayllar — foydalanuvchi belgilagan tartibda.</summary>
    public ObservableCollection<WorkspaceFileViewModel> Files { get; } = [];

    /// <summary>Sahifa eskizlari (faqat <see cref="ToolDescriptor.ShowsPageThumbnails"/> vositalarida).</summary>
    public ObservableCollection<PageItemViewModel> Pages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _selectedCount;

    /// <summary>Eskiz kartochkasining kengligi (masshtab tugmalari bilan boshqariladi).</summary>
    [ObservableProperty]
    private double _thumbnailSize = 168d;

    public bool HasFiles => Files.Count > 0;

    public bool HasPages => Pages.Count > 0;

    /// <summary>Sahifa eskizlari ko'rsatiladimi.</summary>
    public bool ShowsPageGrid => Tool?.ShowsPageThumbnails == true && Pages.Count > 0;

    /// <summary>Sahifa amallari (burish, o'chirish, tanlash) shu vositada ma'noga egami.</summary>
    public bool SupportsPageActions => Tool?.ShowsPageThumbnails == true;

    /// <summary>Rasm vositalarida fayllar katak (grid) ko'rinishida chiziladi.</summary>
    public bool ShowsFileGallery => Tool?.Input == ToolInputKind.Images && Files.Count > 0;

    /// <summary>PDF/Word vositalarida fayllar qator ko'rinishida chiziladi.</summary>
    public bool ShowsFileList => Files.Count > 0
        && Tool is not null
        && Tool.Input != ToolInputKind.Images
        && (Tool.Input == ToolInputKind.MultiplePdf || !Tool.ShowsPageThumbnails);

    /// <summary>Vosita bir nechta fayl qabul qiladimi (tartib tugmalari shunga qarab ko'rinadi).</summary>
    public bool IsMultiFileTool => Tool?.Input is ToolInputKind.MultiplePdf or ToolInputKind.Images;

    public string SummaryText
    {
        get
        {
            if (Files.Count == 0)
                return "Fayl tanlanmagan";

            var text = Files.Count == 1
                ? Files[0].FileName
                : $"{Files.Count} ta fayl";

            if (Pages.Count > 0)
                text += $" · {Pages.Count} sahifa";

            if (SelectedCount > 0)
                text += $" · {SelectedCount} ta tanlandi";

            return text;
        }
    }

    /// <summary>Sarlavha panelidagi o'ng tomondagi jami hajm.</summary>
    public long TotalSizeBytes => Files.Sum(file => file.FileSizeBytes);

    public string EmptyStateTitle => Tool?.Input switch
    {
        ToolInputKind.Images => "Rasmlarni shu yerga tashlang",
        ToolInputKind.WordDocument => "Word hujjatini shu yerga tashlang",
        ToolInputKind.MultiplePdf => "PDF fayllarni shu yerga tashlang",
        _ => "Faylni shu yerga tashlang"
    };

    public string EmptyStateHint => Tool?.Input switch
    {
        ToolInputKind.Images => "JPG, PNG, BMP, GIF, WEBP va TIFF qo'llab-quvvatlanadi.",
        ToolInputKind.WordDocument => ".docx va .doc hujjatlari qo'llab-quvvatlanadi.",
        ToolInputKind.MultiplePdf => "Bir nechta faylni birdaniga tanlash mumkin — tartibini keyin o'zgartirasiz.",
        _ => "…yoki kompyuteringizdan tanlang."
    };

    public string OutputHintText => Tool is null
        ? string.Empty
        : Tool.WritesToFolder
            ? "Natija: siz tanlagan papkaga bir nechta fayl yoziladi."
            : $"Natija: bitta {Tool.OutputExtension ?? ".pdf"} fayl.";

    // =================================================================================
    //  Fayl qo'shish / olib tashlash
    // =================================================================================

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenFilesAsync()
    {
        var tool = Tool;
        if (tool is null)
            return;

        var filter = DescribeFilter(tool.Input);
        var multiSelect = IsMultiFileTool;

        if (multiSelect)
        {
            var paths = DialogService.OpenFiles("Fayllarni tanlang", filter);
            if (paths is not null)
                await AddFilesAsync(paths).ConfigureAwait(true);
        }
        else
        {
            var path = DialogService.OpenFile("Faylni tanlang", filter);
            if (path is not null)
                await AddFilesAsync([path]).ConfigureAwait(true);
        }
    }

    /// <summary>Drag&amp;drop hududiga bog'lanadi.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task DropFilesAsync(string[]? paths)
        => paths is null or { Length: 0 } ? Task.CompletedTask : AddFilesAsync(paths);

    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var tool = Tool;
        if (tool is null)
            return;

        var allowed = AllowedExtensions(tool.Input);
        var accepted = paths
            .Where(path => allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (accepted.Count == 0)
        {
            StatusMessage = "Tanlangan fayllar bu vositaga to'g'ri kelmadi.";
            return;
        }

        // Bitta fayl bilan ishlaydigan vositalarda yangi fayl eskisini almashtiradi.
        var single = tool.Input is ToolInputKind.SinglePdf or ToolInputKind.WordDocument;
        if (single)
        {
            ClearFiles();
            ClearPages();
            accepted = [accepted[0]];
        }

        var added = new List<WorkspaceFileViewModel>();
        foreach (var path in accepted)
        {
            if (Files.Any(file => string.Equals(file.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new WorkspaceFileViewModel(path, RemoveFileCore);
            Files.Add(item);
            added.Add(item);
        }

        if (added.Count == 0)
        {
            StatusMessage = "Bu fayllar ro'yxatda allaqachon bor.";
            return;
        }

        // Yangi fayl qo'shilishi bilan avvalgi natija eskiradi.
        LastResult = null;
        StatusMessage = added.Count == 1
            ? $"{added[0].FileName} qo'shildi"
            : $"{added.Count} ta fayl qo'shildi";

        foreach (var item in added)
            await LoadFileAsync(item, tool).ConfigureAwait(true);
    }

    /// <summary>Bitta fayl uchun eskiz, sahifalar soni va (kerak bo'lsa) sahifa eskizlarini yuklaydi.</summary>
    private async Task LoadFileAsync(WorkspaceFileViewModel item, ToolDescriptor tool)
    {
        try
        {
            switch (tool.Input)
            {
                case ToolInputKind.Images:
                {
                    var thumbnail = await _pdfService
                        .RenderImageThumbnailAsync(item.FilePath, FilePreviewWidth)
                        .ConfigureAwait(true);

                    item.Thumbnail = thumbnail;
                    item.DimensionsText = $"{thumbnail.PixelWidth} × {thumbnail.PixelHeight}";
                    break;
                }

                case ToolInputKind.WordDocument:
                    // Word hujjati uchun ko'rinish chizilmaydi — qatorda fayl turi ikonasi qoladi.
                    break;

                default:
                {
                    if (tool.ShowsPageThumbnails)
                    {
                        // Sahifalar baribir chiziladi — alohida ko'rinish uchun qayta rasterizatsiya
                        // qilmaymiz, birinchi sahifaning eskizini qayta ishlatamiz.
                        await LoadPagesAsync(item).ConfigureAwait(true);
                    }
                    else
                    {
                        item.PageCount = await _pdfService
                            .GetPageCountAsync(item.FilePath)
                            .ConfigureAwait(true);

                        item.Thumbnail = await _pdfService
                            .RenderPageAsync(item.FilePath, 0, FilePreviewWidth)
                            .ConfigureAwait(true);
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            item.ErrorMessage = "Bekor qilindi";
        }
        catch (PdfServiceException ex)
        {
            item.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    /// <summary>Bitta hujjatning barcha sahifalarini bo'lakma-bo'lak eskizlar ro'yxatiga qo'shadi.</summary>
    private async Task LoadPagesAsync(WorkspaceFileViewModel item)
    {
        List<PageModel>? rendered = null;

        var loaded = await RunAsync(
            $"{item.FileName} — sahifalar chizilmoqda…",
            async (progress, token) =>
            {
                rendered = await _pdfService
                    .RenderPdfPagesAsync(item.FilePath, PageRenderWidth, password: null, progress, token)
                    .ConfigureAwait(true);
            },
            $"{item.FileName} yuklandi");

        if (!loaded || rendered is null)
            return;

        item.PageCount = rendered.Count;
        if (rendered.Count > 0)
            item.Thumbnail = rendered[0].Thumbnail;

        // Bo'lakli qo'shish: har 24 sahifadan keyin UI oqimiga nafas olish imkoni beriladi,
        // shunda 500 sahifali hujjatda ham oyna qotib qolmaydi.
        var index = 0;
        foreach (var page in rendered)
        {
            Pages.Add(new PageItemViewModel(page, this));

            if (++index % PageBatchSize == 0)
                await Task.Yield();
        }

        RefreshSourceBadges();
    }

    [RelayCommand]
    private void RemoveFile(WorkspaceFileViewModel? file)
    {
        if (file is not null)
            RemoveFileCore(file);
    }

    private void RemoveFileCore(WorkspaceFileViewModel file)
    {
        Files.Remove(file);

        // Shu fayldan kelgan sahifalar ham ro'yxatdan chiqadi.
        foreach (var page in Pages.Where(p => string.Equals(p.Model.SourceFilePath, file.FilePath, StringComparison.OrdinalIgnoreCase)).ToList())
            Pages.Remove(page);

        RefreshSourceBadges();
    }

    [RelayCommand(CanExecute = nameof(CanReorderFiles))]
    private void MoveFileUp(WorkspaceFileViewModel? file)
    {
        if (file is null)
            return;

        var index = Files.IndexOf(file);
        if (index > 0)
            Files.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(CanReorderFiles))]
    private void MoveFileDown(WorkspaceFileViewModel? file)
    {
        if (file is null)
            return;

        var index = Files.IndexOf(file);
        if (index >= 0 && index < Files.Count - 1)
            Files.Move(index, index + 1);
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void Clear()
    {
        ClearFiles();
        ClearPages();
        LastResult = null;
        StatusMessage = "Ro'yxat tozalandi";
    }

    // =================================================================================
    //  Sahifa amallari
    // =================================================================================

    /// <summary>Kartochkadagi "o'chirish" tugmasi shu yerga keladi (<see cref="IPageItemHost"/>).</summary>
    public void RemovePage(PageItemViewModel page) => Pages.Remove(page);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void RotateSelectedClockwise() => RotateSelected(90);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void RotateSelectedCounterClockwise() => RotateSelected(-90);

    private void RotateSelected(int degrees)
    {
        foreach (var page in Pages.Where(page => page.IsSelected))
            page.Rotation = page.Rotation.Add(degrees);
    }

    /// <summary>
    /// "Sahifalarni burish" vositasi uchun: sozlamalar panelidagi burchakni bir yo'la qo'llaydi.
    /// "Barcha sahifalarga qo'llansin" o'chirilgan bo'lsa — faqat tanlangan sahifalarga.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditPages))]
    private void RotateAll()
    {
        var rotate = Options as RotateOptionsViewModel;
        var degrees = rotate?.Angle ?? 90;
        var applyToAll = rotate?.ApplyToAll ?? true;

        // Tanlov bo'sh bo'lsa "tanlanganlarni burish" hech nima qilmaydi — foydalanuvchi
        // tugma ishlamayapti deb o'ylamasligi uchun buni ochiq aytamiz.
        if (!applyToAll && SelectedCount == 0)
        {
            StatusMessage = "Sahifa tanlanmagan — avval eskizlarni belgilang yoki "
                + "\"Barcha sahifalarga qo'llansin\" ni yoqing.";
            return;
        }

        var targets = applyToAll ? Pages : Pages.Where(page => page.IsSelected);

        var count = 0;
        foreach (var page in targets.ToList())
        {
            page.Rotation = page.Rotation.Add(degrees);
            count++;
        }

        StatusMessage = applyToAll
            ? $"Barcha sahifalar {degrees}° ga burildi"
            : $"{count} ta tanlangan sahifa {degrees}° ga burildi";
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void DeleteSelected()
    {
        foreach (var page in Pages.Where(page => page.IsSelected).ToList())
            Pages.Remove(page);

        StatusMessage = "Tanlangan sahifalar o'chirildi";
    }

    [RelayCommand(CanExecute = nameof(CanEditPages))]
    private void SelectAll()
    {
        foreach (var page in Pages)
            page.IsSelected = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditPages))]
    private void InvertSelection()
    {
        foreach (var page in Pages)
            page.IsSelected = !page.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void ClearSelection()
    {
        foreach (var page in Pages)
            page.IsSelected = false;
    }

    [RelayCommand]
    private void ZoomIn() => ThumbnailSize = Math.Min(ThumbnailSize + 26d, 300d);

    [RelayCommand]
    private void ZoomOut() => ThumbnailSize = Math.Max(ThumbnailSize - 26d, 110d);

    // =================================================================================
    //  Kattalashtirib ko'rish
    // =================================================================================

    [ObservableProperty]
    private bool _isPreviewOpen;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private string _previewTitle = string.Empty;

    /// <summary>Eskiz ustidagi "kattalashtirish" tugmasi: sahifani katta o'lchamda ko'rsatadi.</summary>
    [RelayCommand]
    private async Task PreviewPageAsync(PageItemViewModel? page)
    {
        if (page is null)
            return;

        // Avval mavjud eskizni ko'rsatamiz — oyna darhol ochiladi, sifatli rasm keyin keladi.
        PreviewTitle = $"{page.SourceFileName} — {page.OriginalPageNumber}-sahifa";
        PreviewImage = page.Thumbnail;
        IsPreviewOpen = true;

        try
        {
            var image = await _pdfService
                .RenderPageAsync(page.Model.SourceFilePath, page.Model.SourcePageIndex, PreviewRenderWidth)
                .ConfigureAwait(true);

            // Oyna bu orada yopilgan bo'lishi mumkin.
            if (IsPreviewOpen)
                PreviewImage = image;
        }
        catch (PdfServiceException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ClosePreview()
    {
        IsPreviewOpen = false;
        PreviewImage = null;
    }

    // =================================================================================
    //  Bajarish
    // =================================================================================

    /// <summary>Oxirgi muvaffaqiyatli operatsiya natijasi — "Tayyor!" paneli shundan chiziladi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(ResultMessage))]
    [NotifyPropertyChangedFor(nameof(ResultLocation))]
    [NotifyCanExecuteChangedFor(nameof(OpenResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevealResultCommand))]
    private ToolRunResult? _lastResult;

    public bool HasResult => LastResult is { Success: true };

    public string ResultMessage => LastResult?.Message ?? string.Empty;

    /// <summary>Natija joylashgan papka yoki fayl nomi.</summary>
    public string ResultLocation
    {
        get
        {
            var primary = LastResult?.PrimaryOutput;
            if (string.IsNullOrEmpty(primary))
                return string.Empty;

            return LastResult!.OutputFiles.Count > 1
                ? Path.GetDirectoryName(primary) ?? primary
                : primary;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteTool))]
    private async Task ExecuteAsync()
    {
        var tool = Tool;
        if (tool is null || Files.Count == 0)
            return;

        // Faqat UI da ko'rinadigan tekshiruvlar (masalan parolni tasdiqlash) — ular model
        // obyektiga tushmaydi, shuning uchun dvigatelgacha yetib bormaydi. Saqlash oynasini
        // ochishdan oldin tekshiramiz: foydalanuvchi bekorga fayl nomi tanlab o'tirmasin.
        if ((Options as ToolOptionsViewModel)?.Validate() is { } optionsProblem)
        {
            StatusMessage = optionsProblem;
            DialogService.ShowError("Sozlamalarni tekshiring", optionsProblem);
            return;
        }

        string? outputPath = null;
        string? outputFolder = null;

        if (tool.WritesToFolder)
        {
            // IDialogService da papka tanlash oynasi yo'q. Shuning uchun foydalanuvchi kerakli
            // papka ichida istalgan nom bilan "faylni saqlash" oynasidan o'tadi va biz faqat
            // uning papkasini olamiz — natija fayllarini dvigatel o'zi nomlaydi.
            var marker = DialogService.SaveFile(
                "Natija papkasini tanlang (papka ichida istalgan nom bilan saqlang)",
                DescribeSaveFilter(tool.OutputExtension ?? ".pdf"),
                SuggestFileName(tool),
                tool.OutputExtension ?? ".pdf");

            if (marker is null)
                return;

            outputFolder = Path.GetDirectoryName(marker);
            if (string.IsNullOrEmpty(outputFolder))
            {
                DialogService.ShowError("Papka aniqlanmadi", "Tanlangan joydan papka yo'lini olib bo'lmadi.");
                return;
            }
        }
        else
        {
            var extension = tool.OutputExtension ?? ".pdf";
            outputPath = DialogService.SaveFile(
                "Natijani saqlash",
                DescribeSaveFilter(extension),
                SuggestFileName(tool),
                extension);

            if (outputPath is null)
                return;
        }

        var request = new ToolRequest
        {
            Tool = tool.Id,
            InputFiles = Files.Select(file => file.FilePath).ToList(),
            OutputPath = outputPath,
            OutputFolder = outputFolder,
            Options = BuildOptionsModel(),
            Password = BuildPassword(),
            PagePlan = BuildPagePlan(tool)
        };

        var validation = _engine.Validate(request);
        if (validation is not null)
        {
            StatusMessage = validation;
            DialogService.ShowError("Ma'lumot yetarli emas", validation);
            return;
        }

        ToolRunResult? result = null;

        var completed = await RunAsync(
            $"{tool.Title} — bajarilmoqda…",
            async (progress, token) =>
            {
                result = await _engine.ExecuteAsync(request, progress, token).ConfigureAwait(true);
            });

        if (!completed || result is null)
            return;

        LastResult = result;
        StatusMessage = result.Message;
    }

    /// <summary>Natija faylini standart dastur bilan ochadi.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void OpenResult()
    {
        var primary = LastResult?.PrimaryOutput;
        if (!string.IsNullOrEmpty(primary))
            DialogService.RevealInExplorer(primary);
    }

    /// <summary>Natija papkasini Explorer da ochadi.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    private void RevealResult()
    {
        var primary = LastResult?.PrimaryOutput;
        if (string.IsNullOrEmpty(primary))
            return;

        var folder = Path.GetDirectoryName(primary);
        DialogService.RevealInExplorer(string.IsNullOrEmpty(folder) ? primary : folder);
    }

    private object? BuildOptionsModel() => (Options as ToolOptionsViewModel)?.ToModel();

    /// <summary>Qulfni ochish vositasida parol alohida maydonda uzatiladi.</summary>
    private string? BuildPassword() => Options is UnlockOptionsViewModel unlock && !string.IsNullOrEmpty(unlock.Password)
        ? unlock.Password
        : null;

    private IReadOnlyList<PageEdit>? BuildPagePlan(ToolDescriptor tool)
        => tool.ShowsPageThumbnails && Pages.Count > 0
            ? Pages.Select(page => page.ToPageEdit()).ToList()
            : null;

    /// <summary>Natija fayli uchun tushunarli nom taklif qiladi.</summary>
    private string SuggestFileName(ToolDescriptor tool)
    {
        var baseName = Files.Count > 0
            ? Path.GetFileNameWithoutExtension(Files[0].FilePath)
            : "hujjat";

        var suffix = tool.Id switch
        {
            ToolId.Merge => "birlashtirilgan",
            ToolId.Split => "bolingan",
            ToolId.Organize => "tartiblangan",
            ToolId.Rotate => "burilgan",
            ToolId.Compress => "siqilgan",
            ToolId.Protect => "himoyalangan",
            ToolId.Unlock => "ochilgan",
            ToolId.Watermark => "suv-belgisi",
            ToolId.PageNumbers => "raqamlangan",
            ToolId.PdfToImage => "rasm",
            ToolId.OcrToWord => "matn",
            ToolId.BackgroundRemover => "fonsiz",
            _ => "natija"
        };

        return $"{baseName}-{suffix}{tool.OutputExtension ?? ".pdf"}";
    }

    // =================================================================================
    //  Sozlamalar
    // =================================================================================

    /// <summary>Vositaga mos sozlamalar ViewModel ini yasaydi; sozlamasiz vositalar uchun <c>null</c>.</summary>
    private static ToolOptionsViewModel? CreateOptions(ToolId id) => id switch
    {
        ToolId.Split => new SplitOptionsViewModel(),
        ToolId.Rotate => new RotateOptionsViewModel(),
        ToolId.Compress => new CompressOptionsViewModel(),
        ToolId.Protect => new ProtectOptionsViewModel(),
        ToolId.Unlock => new UnlockOptionsViewModel(),
        ToolId.Watermark => new WatermarkOptionsViewModel(),
        ToolId.PageNumbers => new PageNumberOptionsViewModel(),
        ToolId.PdfToWord => new PdfToWordOptionsViewModel(),
        ToolId.WordToPdf => new WordToPdfOptionsViewModel(),
        ToolId.PdfToImage => new PdfToImageOptionsViewModel(),
        ToolId.ImageToPdf => new ImageToPdfOptionsViewModel(),
        ToolId.PdfToExcel => new PdfToExcelOptionsViewModel(),
        ToolId.PdfToPowerPoint => new PdfToPowerPointOptionsViewModel(),
        ToolId.OcrToWord => new OcrOptionsViewModel(),
        ToolId.BackgroundRemover => new BackgroundRemoverOptionsViewModel(),

        // Birlashtirishda sozlama yo'q — tartibni foydalanuvchi kartochkalar bilan belgilaydi.
        _ => null
    };

    private void AttachOptions()
    {
        if (Options is ToolOptionsViewModel options)
            options.PropertyChanged += OnOptionsPropertyChanged;
    }

    private void DetachOptions()
    {
        if (Options is ToolOptionsViewModel options)
            options.PropertyChanged -= OnOptionsPropertyChanged;
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tashqi komponent talabi faqat til/rejim kabi sozlamalarga bog'liq, shuning uchun
        // har bir belgida emas, faqat shu xossalar o'zgarganda qayta tekshiramiz.
        if (e.PropertyName is nameof(PdfToWordOptionsViewModel.OcrLanguage)
            or nameof(OcrOptionsViewModel.Language)
            or nameof(PdfToWordOptionsViewModel.Recognition)
            or nameof(WordToPdfOptionsViewModel.Engine))
        {
            RefreshPrerequisites();
        }

        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPrerequisites()
    {
        var tool = Tool;
        if (tool is null)
        {
            PrerequisiteWarning = null;
            MissingComponent = DownloadableComponent.None;
            return;
        }

        try
        {
            var options = BuildOptionsModel();
            PrerequisiteWarning = _engine.CheckPrerequisites(tool.Id, options);
            MissingComponent = _engine.GetMissingComponent(tool.Id, options);
        }
        catch (Exception ex)
        {
            // Tekshiruvning o'zi ishdan chiqsa ham ishchi oyna ochilishi kerak.
            PrerequisiteWarning = ex.Message;
            MissingComponent = DownloadableComponent.None;
        }
    }

    /// <summary>
    /// Yetishmayotgan komponentni ("Yuklab olish" tugmasi) shu yerdan oladi — foydalanuvchi
    /// vositani tashlab, "Dastur haqida" sahifasiga o'tishi shart emas.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadMissingComponent))]
    private async Task DownloadMissingComponentAsync()
    {
        var component = MissingComponent;
        if (component == DownloadableComponent.None)
            return;

        var (title, question, busy, done) = component switch
        {
            DownloadableComponent.OcrLanguages => (
                "Til fayllarini yuklab olish",
                "Yetishmayotgan OCR til fayllari internetdan yuklab olinadi.\n\nDavom etaylikmi?",
                "Til fayllari yuklanmoqda…",
                "Til fayllari yuklandi"),

            _ => (
                "AI modelini yuklab olish",
                $"'{_engine.BackgroundRemover.DownloadableModelName}' "
                + $"({_engine.BackgroundRemover.DownloadableModelSizeText}) internetdan yuklab olinadi "
                + "va bu bir marta bajariladi.\n\nDavom etaylikmi?",
                "AI modeli yuklanmoqda…",
                "AI modeli yuklandi")
        };

        if (!DialogService.Confirm(title, question))
            return;

        // Sozlamalar (masalan OCR tili) yuklash boshlanishidan oldingi holatda olinadi.
        var options = BuildOptionsModel();

        var downloaded = await RunAsync(
            busy,
            async (progress, token) =>
            {
                await _engine.DownloadComponentAsync(component, options, progress, token).ConfigureAwait(true);
            },
            done);

        if (downloaded)
            RefreshPrerequisites();
    }

    private bool CanDownloadMissingComponent() => IsIdle && HasDownloadableComponent;

    // =================================================================================
    //  Kengaytmalar va dialog filtrlari
    // =================================================================================

    private static string[] AllowedExtensions(ToolInputKind input) => input switch
    {
        ToolInputKind.Images => ImageExtensions,
        ToolInputKind.WordDocument => WordExtensions,
        _ => PdfExtensions
    };

    private static string DescribeExtensions(ToolInputKind input)
        => string.Join(',', AllowedExtensions(input));

    private static string DescribeFilter(ToolInputKind input) => input switch
    {
        ToolInputKind.Images =>
            "Rasmlar (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|Barcha fayllar (*.*)|*.*",
        ToolInputKind.WordDocument =>
            "Word hujjatlari (*.docx;*.doc)|*.docx;*.doc|Barcha fayllar (*.*)|*.*",
        _ =>
            "PDF hujjatlar (*.pdf)|*.pdf|Barcha fayllar (*.*)|*.*"
    };

    private static string DescribeSaveFilter(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => "Word hujjati (*.docx)|*.docx|Barcha fayllar (*.*)|*.*",
        ".xlsx" => "Excel kitobi (*.xlsx)|*.xlsx|Barcha fayllar (*.*)|*.*",
        ".pptx" => "PowerPoint taqdimoti (*.pptx)|*.pptx|Barcha fayllar (*.*)|*.*",
        ".png" => "PNG rasm (*.png)|*.png|Barcha fayllar (*.*)|*.*",
        ".jpg" => "JPEG rasm (*.jpg)|*.jpg|Barcha fayllar (*.*)|*.*",
        _ => "PDF hujjat (*.pdf)|*.pdf|Barcha fayllar (*.*)|*.*"
    };

    /// <summary>Pastki paneldagi katta tugma matni.</summary>
    private static string DescribeExecuteButton(ToolId id) => id switch
    {
        ToolId.Merge => "Birlashtirish",
        ToolId.Split => "Bo'lish",
        ToolId.Organize => "Saqlash",
        ToolId.Rotate => "Aylantirish",
        ToolId.Compress => "Siqish",
        ToolId.Protect => "Himoyalash",
        ToolId.Unlock => "Qulfni ochish",
        ToolId.Watermark => "Suv belgisi qo'yish",
        ToolId.PageNumbers => "Raqamlash",
        ToolId.PdfToWord => "Word ga o'tkazish",
        ToolId.WordToPdf => "PDF ga o'tkazish",
        ToolId.PdfToImage => "Rasmga o'tkazish",
        ToolId.ImageToPdf => "PDF yig'ish",
        ToolId.PdfToExcel => "Excel ga o'tkazish",
        ToolId.PdfToPowerPoint => "Slaydlarga o'tkazish",
        ToolId.OcrToWord => "Matnni tanib olish",
        ToolId.BackgroundRemover => "Fonni olib tashlash",
        _ => "Bajarish"
    };

    // =================================================================================
    //  Buyruqlar mavjudligi
    // =================================================================================

    private bool CanModifyList() => IsIdle && Files.Count > 0;

    private bool CanReorderFiles() => IsIdle && IsMultiFileTool && Files.Count > 1;

    private bool CanEditPages() => IsIdle && Pages.Count > 0;

    private bool CanActOnSelection() => IsIdle && SelectedCount > 0;

    private bool CanExecuteTool() => IsIdle && Files.Count > 0 && Files.All(file => !file.HasError);

    protected override void OnBusyStateChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        OpenFilesCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        MoveFileUpCommand.NotifyCanExecuteChanged();
        MoveFileDownCommand.NotifyCanExecuteChanged();
        RotateSelectedClockwiseCommand.NotifyCanExecuteChanged();
        RotateSelectedCounterClockwiseCommand.NotifyCanExecuteChanged();
        RotateAllCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        DownloadMissingComponentCommand.NotifyCanExecuteChanged();
    }

    // =================================================================================
    //  Kolleksiyalarni kuzatish
    // =================================================================================

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Files.Count; i++)
            Files[i].OrderNumber = i + 1;

        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowsFileList));
        OnPropertyChanged(nameof(ShowsFileGallery));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(TotalSizeBytes));
        RefreshCommands();
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PageItemViewModel page in e.OldItems)
                page.PropertyChanged -= OnPagePropertyChanged;

        if (e.NewItems is not null)
            foreach (PageItemViewModel page in e.NewItems)
                page.PropertyChanged += OnPagePropertyChanged;

        Renumber();
        UpdateSelectionCount();

        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(ShowsPageGrid));
        OnPropertyChanged(nameof(SummaryText));
        RefreshCommands();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageItemViewModel.IsSelected))
            UpdateSelectionCount();
    }

    /// <summary>Tartib o'zgargandan keyin sahifa raqamlarini ketma-ket qiladi.</summary>
    private void Renumber()
    {
        for (var i = 0; i < Pages.Count; i++)
            Pages[i].PageNumber = i + 1;
    }

    /// <summary>Bir nechta hujjat aralashganda kartochkada fayl nomi nishoni ko'rsatiladi.</summary>
    private void RefreshSourceBadges()
    {
        var mixed = Pages.Select(page => page.Model.SourceFilePath)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(2)
                         .Count() > 1;

        foreach (var page in Pages)
            page.ShowSourceBadge = mixed;
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = Pages.Count(page => page.IsSelected);
        RotateSelectedClockwiseCommand.NotifyCanExecuteChanged();
        RotateSelectedCounterClockwiseCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ClearFiles() => Files.Clear();

    private void ClearPages()
    {
        foreach (var page in Pages)
            page.PropertyChanged -= OnPagePropertyChanged;

        Pages.Clear();
    }
}
