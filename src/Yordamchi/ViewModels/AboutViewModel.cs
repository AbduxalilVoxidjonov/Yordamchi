using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Dastur haqida" sahifasi: versiya, muallif, aloqa va tashqi komponentlar holati.
/// <para>
/// Komponentlar holati shu yerda ko'rsatiladi, chunki OCR til fayllari va AI modeli ixtiyoriy —
/// foydalanuvchi ular yo'qligini vosita ishlamay qolgandan keyin emas, oldindan bilishi kerak.
/// </para>
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    /// <summary>Muallifning Telegram manzili.</summary>
    public const string TelegramUrl = "https://t.me/abduxalilvoxidjonov";

    private readonly IPdfEngineService _engine;
    private readonly IUpdateService _updateService;

    public AboutViewModel(IPdfEngineService engine, IUpdateService updateService, IDialogService dialogService)
        : base(dialogService)
    {
        _engine = engine;
        _updateService = updateService;
        RefreshComponents();

        // Sahifa ochilishini kutmasdan holatni to'ldiramiz. Natija xizmatda keshlangani uchun
        // bu odatda tarmoqqa umuman chiqmaydi — qobiq allaqachon so'ragan bo'ladi.
        _ = LoadUpdateStatusAsync();
    }

    /// <summary>
    /// Yangilanish o'rnatilishidan oldin dastur yopilishi kerak. ViewModel WPF ni bilmaydi,
    /// shuning uchun yopishni oyna (<c>MainWindow</c>) shu hodisa orqali bajaradi —
    /// ekran yozuvidagi <c>MinimizeRequested</c> bilan bir xil naqsh.
    /// </summary>
    public event EventHandler? RestartRequested;

    public override string Title => "Dastur haqida";

    public override string Description => "Versiya, muallif va qo'shimcha komponentlar holati.";

    // -----------------------------------------------------------------
    //  Dastur ma'lumotlari
    // -----------------------------------------------------------------

    public string ApplicationName => "Yordamchi";

    public string VersionText => $"Versiya {Version}";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.2.0";

    public string AuthorName => "Abduxalil Voxidjonov";

    public string AuthorRole => "Dastur muallifi va ishlab chiquvchisi";

    public string TelegramHandle => "@abduxalilvoxidjonov";

    public string TelegramLink => TelegramUrl;

    public string CopyrightText => $"© 2026 {AuthorName}. Barcha huquqlar himoyalangan.";

    public string Tagline => "Windows uchun to'liq funksional PDF vositalari to'plami — internetsiz ishlaydi.";

    /// <summary>Dastur foydalanadigan ochiq kutubxonalar.</summary>
    public IReadOnlyList<ComponentInfo> Libraries { get; } =
    [
        new("PDFsharp", "PDF yozish, birlashtirish, himoyalash", "MIT"),
        new("pdfium / PDFtoImage", "Sahifalarni rasmga aylantirish", "MIT"),
        new("SkiaSharp", "Rasmlar bilan ishlash", "MIT"),
        new("UglyToad.PdfPig", "PDF dan matn va joylashuvni o'qish", "Apache-2.0"),
        new("DocumentFormat.OpenXml", "Word, Excel va PowerPoint fayllarini yozish", "MIT"),
        new("Tesseract OCR", "Skaner qilingan hujjatlardan matn tanish", "Apache-2.0"),
        new("ONNX Runtime", "AI modelini (u2net) ishga tushirish", "MIT"),
        new("SharpCompress", "ZIP, RAR, 7z va TAR arxivlarini o'qish", "MIT"),
        new("SharpZipLib", "Parolli (AES-256) ZIP arxiv yozish", "MIT"),
        new("ScreenRecorderLib", "Ekranni videoga yozib olish", "MIT"),
        new("CommunityToolkit.Mvvm", "MVVM arxitekturasi", "MIT")
    ];

    // -----------------------------------------------------------------
    //  Komponentlar holati
    // -----------------------------------------------------------------

    [ObservableProperty]
    private string _ocrStatus = string.Empty;

    [ObservableProperty]
    private bool _isOcrReady;

    [ObservableProperty]
    private string _aiStatus = string.Empty;

    [ObservableProperty]
    private bool _isAiReady;

    [ObservableProperty]
    private string _wordStatus = string.Empty;

    [ObservableProperty]
    private bool _isWordReady;

    private void RefreshComponents()
    {
        var installed = _engine.Ocr.GetInstalledLanguages();
        IsOcrReady = installed.Count > 0;
        OcrStatus = IsOcrReady
            ? $"Tayyor — o'rnatilgan tillar: {string.Join(", ", installed)}"
            : "Til fayllari o'rnatilmagan. OCR vositasidan foydalanishdan oldin ularni yuklab oling.";

        IsAiReady = _engine.BackgroundRemover.IsModelAvailable;
        AiStatus = IsAiReady
            ? "Tayyor — u2net modeli topildi"
            : $"Model topilmadi ({_engine.BackgroundRemover.DownloadableModelSizeText}). "
              + "\"Yuklab olish\" tugmasini bosing yoki faylni shu papkaga qo'ying:\n"
              + _engine.BackgroundRemover.ModelPath;

        IsWordReady = _engine.Conversion.IsMicrosoftWordAvailable;
        WordStatus = IsWordReady
            ? "Microsoft Word topildi — Word → PDF eng yuqori aniqlikda ishlaydi"
            : "Microsoft Word topilmadi — dasturning ichki dvigateli ishlatiladi";
    }

    // -----------------------------------------------------------------
    //  Yangilanish
    // -----------------------------------------------------------------

    /// <summary>Topilgan yangilanish; <c>null</c> — eng so'nggi versiya o'rnatilgan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    private string _updateStatus = "Yangilanish tekshirilmoqda…";

    public bool HasUpdate => AvailableUpdate is not null;

    /// <summary>Relizlar sahifasi — "Nima o'zgardi" tugmasining manzili.</summary>
    public string ReleasesPageUrl => _updateService.ReleasesPageUrl;

    private async Task LoadUpdateStatusAsync()
    {
        try
        {
            // Keshdan foydalanamiz: qobiq allaqachon so'ragan bo'lsa GitHub qayta bezovta qilinmaydi.
            ApplyUpdate(await _updateService.CheckForUpdateAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sahifa ochilishida oyna chiqarmaymiz: yangilanish — ixtiyoriy qulaylik, uni
            // tekshirib bo'lmagani foydalanuvchining ishiga to'sqinlik qilmasligi kerak.
            AvailableUpdate = null;
            UpdateStatus = "Yangilanishni tekshirib bo'lmadi — internetga ulanishni tekshiring.";
        }
    }

    private void ApplyUpdate(UpdateInfo? update)
    {
        AvailableUpdate = update;
        UpdateStatus = update is null
            ? $"Eng so'nggi versiya o'rnatilgan (v{Version})"
            : $"Yangi versiya tayyor: {update.VersionText} ({update.SizeText})";
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckForUpdateAsync()
    {
        var checkedOk = await RunAsync(
            "Yangilanish tekshirilmoqda…",
            async (_, token) =>
            {
                // force: dastur ochiq turganda chiqqan yangi relizni ko'rsatishning yagona yo'li.
                // Keshlangan javob qaytsa, bu tugma foydalanuvchi uchun umuman ishlamas edi.
                ApplyUpdate(await _updateService
                    .CheckForUpdateAsync(force: true, token)
                    .ConfigureAwait(true));
            });

        if (!checkedOk)
            UpdateStatus = "Yangilanishni tekshirib bo'lmadi — internetga ulanishni tekshiring.";
    }

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadAndInstallAsync()
    {
        var update = AvailableUpdate;
        if (update is null)
            return;

        // Bu amal dasturni yopadi va administrator ruxsatini so'raydi — foydalanuvchi nima
        // bo'lishini oldindan to'liq bilishi kerak.
        if (!DialogService.Confirm(
                "Yangilanishni o'rnatish",
                $"Yordamchi {update.VersionText} versiyasiga yangilanadi.\n\n"
                + $"1. O'rnatgich yuklab olinadi ({update.SizeText}).\n"
                + "2. Dastur yopiladi.\n"
                + "3. Yangi versiya o'rnatiladi — Windows administrator ruxsatini so'raydi.\n"
                + "4. Dastur qaytadan ochiladi.\n\n"
                + "Saqlanmagan ishlaringizni yakunlab oling. Davom etaylikmi?"))
        {
            return;
        }

        string? installerPath = null;

        var downloaded = await RunAsync(
            "Yangilanish yuklanmoqda…",
            async (progress, token) =>
            {
                installerPath = await _updateService
                    .DownloadAsync(update, progress, token)
                    .ConfigureAwait(true);
            },
            "Yangilanish yuklandi — dastur yopilmoqda…");

        if (!downloaded || string.IsNullOrEmpty(installerPath))
            return;

        try
        {
            _updateService.LaunchInstaller(installerPath);
        }
        catch (PdfServiceException ex)
        {
            // O'rnatgich ishga tushmadi — dasturni yopish mutlaqo mumkin emas, aks holda
            // foydalanuvchi na eski, na yangi versiyada qolardi.
            StatusMessage = ex.Message;
            DialogService.ShowError("Yangilashni boshlab bo'lmadi", ex.Message);
            return;
        }

        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ViewReleaseNotes() => OpenUrl(_updateService.ReleasesPageUrl);

    private bool CanDownloadUpdate => IsIdle && HasUpdate;

    // -----------------------------------------------------------------
    //  Amallar
    // -----------------------------------------------------------------

    [RelayCommand]
    private void OpenTelegram() => OpenUrl(TelegramUrl);

    [RelayCommand]
    private void OpenOcrFolder() => OpenFolder(_engine.Ocr.TessDataPath);

    [RelayCommand]
    private void OpenModelFolder() => OpenFolder(Path.GetDirectoryName(_engine.BackgroundRemover.ModelPath));

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DownloadOcrLanguagesAsync()
    {
        var languages = OcrOptions.DefaultLanguage.Split('+', StringSplitOptions.RemoveEmptyEntries);

        if (!DialogService.Confirm(
                "Til fayllarini yuklab olish",
                $"Quyidagi tillar internetdan yuklab olinadi: {string.Join(", ", languages)}.\n\nDavom etaylikmi?"))
        {
            return;
        }

        var downloaded = await RunAsync(
            "Til fayllari yuklanmoqda…",
            async (progress, token) =>
            {
                await _engine.Ocr.DownloadLanguagesAsync(languages, progress, token).ConfigureAwait(true);
            },
            "Til fayllari yuklandi");

        if (downloaded)
            RefreshComponents();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DownloadAiModelAsync()
    {
        var remover = _engine.BackgroundRemover;

        if (!DialogService.Confirm(
                "AI modelini yuklab olish",
                $"'{remover.DownloadableModelName}' ({remover.DownloadableModelSizeText}) internetdan yuklab olinadi "
                + "va bu bir marta bajariladi.\n\nDavom etaylikmi?"))
        {
            return;
        }

        var downloaded = await RunAsync(
            "AI modeli yuklanmoqda…",
            async (progress, token) =>
            {
                await remover.DownloadModelAsync(progress, token).ConfigureAwait(true);
            },
            "AI modeli yuklandi");

        if (downloaded)
            RefreshComponents();
    }

    [RelayCommand]
    private void Refresh() => RefreshComponents();

    protected override void OnBusyStateChanged()
    {
        DownloadOcrLanguagesCommand.NotifyCanExecuteChanged();
        DownloadAiModelCommand.NotifyCanExecuteChanged();
        CheckForUpdateCommand.NotifyCanExecuteChanged();
        DownloadAndInstallCommand.NotifyCanExecuteChanged();
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Brauzerni ochib bo'lmasa — havolani foydalanuvchi qo'lda ko'chira olishi uchun ko'rsatamiz.
            DialogService.ShowInformation("Havola", url);
        }
    }

    private void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Papkani yarata olmasak ham, quyida ochishga urinib ko'ramiz.
        }

        DialogService.RevealInExplorer(folder);
    }
}

/// <summary>"Dastur haqida" sahifasidagi kutubxona qatori.</summary>
/// <param name="Name">Kutubxona nomi.</param>
/// <param name="Purpose">Dasturdagi vazifasi.</param>
/// <param name="License">Litsenziyasi.</param>
public sealed record ComponentInfo(string Name, string Purpose, string License);
