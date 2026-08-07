using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Helpers;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using SkiaSharp;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Orqa fonni olib tashlash" ishchi oynasi: rasm tanlanadi, AI modeli (u2net) fonni ajratadi,
/// natija shaffof PNG sifatida saqlanadi yoki PDF ga aylantiriladi.
/// <para>
/// Natija ikki nusxada saqlanadi: <see cref="ResultImage"/> — UI ga ko'rsatish uchun muzlatilgan
/// tasvir, <c>_resultBitmap</c> esa — saqlash/PDF uchun kerak bo'ladigan xom Skia tasviri.
/// Shuning uchun sinf <see cref="IDisposable"/> ni amalga oshiradi.
/// </para>
/// </summary>
public sealed partial class BackgroundRemoverViewModel : ViewModelBase, IDisposable
{
    /// <summary>Ko'rish uchun yetarli kenglik: kattaroq rasmni xotirada saqlashning hojati yo'q.</summary>
    private const int PreviewWidth = 900;

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    private readonly IImageBackgroundRemover _remover;
    private readonly IPdfService _pdfService;

    /// <summary>Natijaning xom nusxasi — PNG saqlash va PDF yasash shundan bajariladi.</summary>
    private SKBitmap? _resultBitmap;

    private bool _isDisposed;

    public BackgroundRemoverViewModel(
        IImageBackgroundRemover remover,
        IPdfService pdfService,
        IDialogService dialogService)
        : base(dialogService)
    {
        _remover = remover;
        _pdfService = pdfService;

        _modelPath = remover.ModelPath;
        _isModelAvailable = remover.IsModelAvailable;
    }

    public override string Title => "Orqa fonni olib tashlash";

    public override string Description =>
        "AI yordamida rasmlar fonini bir soniyada shaffof qiling.";

    /// <summary>"‹ Orqaga" bosilganda qobiq (shell) shu hodisani eshitadi.</summary>
    public event EventHandler? BackRequested;

    // -----------------------------------------------------------------
    // Holat
    // -----------------------------------------------------------------

    /// <summary>Chap paneldagi original rasm ko'rinishi (muzlatilgan).</summary>
    [ObservableProperty]
    private BitmapSource? _sourceImage;

    /// <summary>O'ng paneldagi natija — shaffof fonli tasvir (muzlatilgan).</summary>
    [ObservableProperty]
    private BitmapSource? _resultImage;

    /// <summary>Tanlangan rasmning to'liq yo'li.</summary>
    [ObservableProperty]
    private string? _sourcePath;

    /// <summary>Tanlangan rasm fayli hajmi (bayt).</summary>
    [ObservableProperty]
    private long _sourceSizeBytes;

    /// <summary>ONNX modeli joyidami — yo'q bo'lsa yuqorida ogohlantirish paneli chiqadi.</summary>
    [ObservableProperty]
    private bool _isModelAvailable;

    /// <summary>Model fayli kutilayotgan to'liq yo'l (ogohlantirish panelida ko'rsatiladi).</summary>
    [ObservableProperty]
    private string _modelPath = string.Empty;

    /// <summary>Rasm tanlanganmi.</summary>
    public bool HasSource => SourcePath is not null;

    /// <summary>Natija tayyormi.</summary>
    public bool HasResult => ResultImage is not null && _resultBitmap is not null;

    /// <summary>Fayl nomi — rasm ostidagi izohda ko'rsatiladi.</summary>
    public string SourceFileName => SourcePath is null ? string.Empty : Path.GetFileName(SourcePath);

    partial void OnSourcePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(SourceFileName));
        RefreshCommands();
    }

    partial void OnResultImageChanged(BitmapSource? value)
    {
        OnPropertyChanged(nameof(HasResult));
        RefreshCommands();
    }

    partial void OnIsModelAvailableChanged(bool value) => RefreshCommands();

    // -----------------------------------------------------------------
    // Rasm tanlash
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenImageAsync()
    {
        var path = DialogService.OpenFile("Rasm tanlash", IDialogService.Filters.Images);
        if (path is not null)
            await LoadSourceAsync(path);
    }

    /// <summary>Drag&amp;drop hududiga bog'langan: birinchi mos rasm olinadi.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DropFilesAsync(string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return;

        var first = paths.FirstOrDefault(p =>
            SupportedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));

        if (first is null)
        {
            StatusMessage = "Tanlovda mos rasm fayli topilmadi";
            return;
        }

        await LoadSourceAsync(first);
    }

    private async Task LoadSourceAsync(string path)
    {
        BitmapSource? preview = null;

        var loaded = await RunAsync(
            "Rasm yuklanmoqda…",
            async (_, token) =>
            {
                preview = await _pdfService
                    .RenderImageThumbnailAsync(path, PreviewWidth, token)
                    .ConfigureAwait(true);
            });

        if (!loaded || preview is null)
            return;

        // Yangi manba — eski natija endi tegishli emas.
        DiscardResult();

        SourceImage = preview;
        SourcePath = path;
        SourceSizeBytes = TryGetFileSize(path);
        StatusMessage = $"{Path.GetFileName(path)} yuklandi";
    }

    // -----------------------------------------------------------------
    // Fonni olib tashlash
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRemoveBackground))]
    private async Task RemoveBackgroundAsync()
    {
        var path = SourcePath;
        if (path is null)
            return;

        SKBitmap? produced = null;
        BitmapSource? preview = null;

        var completed = await RunAsync(
            "AI fonni ajratmoqda…",
            async (_, token) =>
            {
                // ViewModelBase o'zining IProgress<PdfProgress> ini beradi, servis esa
                // IProgress<int> kutadi — shuning uchun ko'prikni shu yerda yasaymiz.
                // Progress<T> UI kontekstini ushlab qolgani uchun qayta chaqiruv UI oqimida bajariladi.
                var reporter = new Progress<int>(percent =>
                {
                    IsProgressIndeterminate = false;
                    ProgressValue = percent;
                });

                produced = await _remover
                    .RemoveBackgroundToBitmapAsync(path, BackgroundRemovalOptions.Default, reporter, token)
                    .ConfigureAwait(true);

                // PNG ga kodlash alfa kanalni saqlaydi; og'ir ish UI oqimidan tashqarida bajariladi.
                var bitmap = produced;
                preview = await Task.Run(() => SkiaImageHelper.ToFrozenBitmapImage(bitmap), token)
                    .ConfigureAwait(true);
            },
            "Fon olib tashlandi");

        if (!completed || produced is null || preview is null)
        {
            produced?.Dispose();
            return;
        }

        _resultBitmap?.Dispose();
        _resultBitmap = produced;
        ResultImage = preview;
    }

    // -----------------------------------------------------------------
    // Natijani saqlash
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private async Task SavePngAsync()
    {
        var bitmap = _resultBitmap;
        if (bitmap is null)
            return;

        var suggested = $"{Path.GetFileNameWithoutExtension(SourcePath) ?? "rasm"}-fonsiz.png";
        var target = DialogService.SaveFile(
            "PNG sifatida saqlash",
            "PNG rasm (*.png)|*.png|Barcha fayllar (*.*)|*.*",
            suggested,
            ".png");

        if (target is null)
            return;

        var saved = await RunAsync(
            "PNG saqlanmoqda…",
            (_, token) => _remover.SaveAsPngAsync(bitmap, target, token),
            $"Saqlandi: {Path.GetFileName(target)}");

        if (saved && DialogService.Confirm(
                "Saqlandi",
                $"{Path.GetFileName(target)} saqlandi.\n\nExplorer'da ko'rsataymi?"))
        {
            DialogService.RevealInExplorer(target);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private async Task AddToPdfAsync()
    {
        var bitmap = _resultBitmap;
        if (bitmap is null)
            return;

        var suggested = $"{Path.GetFileNameWithoutExtension(SourcePath) ?? "rasm"}-fonsiz.pdf";
        var target = DialogService.SaveFile("PDF sifatida saqlash", IDialogService.Filters.Pdf, suggested);
        if (target is null)
            return;

        // PDF importeri fayl yo'lini kutadi, shuning uchun natija avval vaqtinchalik PNG ga yoziladi.
        var tempPng = Path.Combine(Path.GetTempPath(), $"pdfedit-fonsiz-{Guid.NewGuid():N}.png");

        var created = await RunAsync(
            "PDF yaratilmoqda…",
            async (progress, token) =>
            {
                try
                {
                    await _remover.SaveAsPngAsync(bitmap, tempPng, token).ConfigureAwait(true);

                    var options = new ImageToPdfOptions
                    {
                        PageSizeMode = PdfPageSizeMode.FitToImage
                    };

                    await _pdfService
                        .ConvertImagesToPdfAsync([tempPng], target, options, progress, token)
                        .ConfigureAwait(true);
                }
                finally
                {
                    TryDelete(tempPng);
                }
            },
            $"PDF yaratildi: {Path.GetFileName(target)}");

        if (created && DialogService.Confirm(
                "Tayyor",
                $"{Path.GetFileName(target)} yaratildi.\n\nExplorer'da ko'rsataymi?"))
        {
            DialogService.RevealInExplorer(target);
        }
    }

    // -----------------------------------------------------------------
    // Tozalash / navigatsiya / model
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        DiscardResult();
        SourceImage = null;
        SourcePath = null;
        SourceSizeBytes = 0;
        StatusMessage = "Tozalandi";
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Model papkasini Explorer'da ochadi va model paydo bo'lgan bo'lsa holatni yangilaydi.</summary>
    [RelayCommand]
    private void OpenModelFolder()
    {
        var folder = GetExistingModelFolder();
        if (folder is not null)
            DialogService.RevealInExplorer(folder);

        // Foydalanuvchi modelni papkaga tashlagan bo'lishi mumkin — holatni qayta o'qiymiz.
        IsModelAvailable = _remover.IsModelAvailable;
        ModelPath = _remover.ModelPath;
    }

    /// <summary>Modelni internetdan yuklab oladi va tugagach sahifani ishga tayyor holatga o'tkazadi.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DownloadModelAsync()
    {
        if (!DialogService.Confirm(
                "AI modelini yuklab olish",
                $"'{_remover.DownloadableModelName}' ({_remover.DownloadableModelSizeText}) internetdan yuklab olinadi "
                + "va bu bir marta bajariladi.\n\nDavom etaylikmi?"))
        {
            return;
        }

        var downloaded = await RunAsync(
            "AI modeli yuklanmoqda…",
            async (progress, token) =>
            {
                await _remover.DownloadModelAsync(progress, token).ConfigureAwait(true);
            },
            "AI modeli yuklandi");

        if (!downloaded)
            return;

        // Sessiya kech (lazy) ochilgani uchun dasturni qayta ishga tushirish shart emas.
        IsModelAvailable = _remover.IsModelAvailable;
        ModelPath = _remover.ModelPath;
    }

    private string? GetExistingModelFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(ModelPath);
            if (string.IsNullOrWhiteSpace(folder))
                return AppContext.BaseDirectory;

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return AppContext.BaseDirectory;
        }
    }

    // -----------------------------------------------------------------
    // Komandalar mavjudligi
    // -----------------------------------------------------------------

    private bool CanRemoveBackground() => IsIdle && SourcePath is not null && IsModelAvailable;

    private bool CanUseResult() => IsIdle && HasResult;

    private bool CanClear() => IsIdle && (HasSource || HasResult);

    protected override void OnBusyStateChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        OpenImageCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        RemoveBackgroundCommand.NotifyCanExecuteChanged();
        SavePngCommand.NotifyCanExecuteChanged();
        AddToPdfCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        DownloadModelCommand.NotifyCanExecuteChanged();
    }

    // -----------------------------------------------------------------
    // Yordamchilar
    // -----------------------------------------------------------------

    private void DiscardResult()
    {
        ResultImage = null;
        _resultBitmap?.Dispose();
        _resultBitmap = null;
        OnPropertyChanged(nameof(HasResult));
        RefreshCommands();
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Vaqtinchalik fayl qolib ketsa ham operatsiyani buzmaymiz.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _resultBitmap?.Dispose();
        _resultBitmap = null;
    }
}
