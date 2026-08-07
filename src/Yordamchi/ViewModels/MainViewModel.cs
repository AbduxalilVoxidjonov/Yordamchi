using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// Qobiq (shell) view model: yon panel, joriy sahifa va mavzu (tema) kaliti.
/// <para>
/// Navigatsiya juda sodda: bosh sahifada vosita tanlanadi → ishchi oyna ochiladi → "Orqaga"
/// bosilganda yana bosh sahifaga qaytiladi. Ishchi oyna bitta nusxada yashaydi va har safar
/// <see cref="ToolWorkspaceViewModel.Activate"/> bilan qayta sozlanadi, shu tufayli sahifalar
/// almashganda yuklangan eskizlar behuda tashlab yuborilmaydi.
/// </para>
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly DashboardViewModel _dashboard;
    private readonly ToolWorkspaceViewModel _workspace;
    private readonly BackgroundRemoverViewModel _backgroundRemover;
    private readonly ArchiveViewModel _archive;
    private readonly ScreenRecorderViewModel _screenRecorder;
    private readonly AboutViewModel _about;
    private readonly IUpdateService _updateService;

    public MainViewModel(
        DashboardViewModel dashboard,
        ToolWorkspaceViewModel workspace,
        BackgroundRemoverViewModel backgroundRemover,
        ArchiveViewModel archive,
        ScreenRecorderViewModel screenRecorder,
        AboutViewModel about,
        IThemeService themeService,
        IUpdateService updateService)
    {
        _updateService = updateService;
        _dashboard = dashboard;
        _workspace = workspace;
        _backgroundRemover = backgroundRemover;
        _archive = archive;
        _screenRecorder = screenRecorder;
        _about = about;
        _themeService = themeService;

        _themeService.ThemeChanged += (_, _) => OnPropertyChanged(nameof(IsDarkMode));

        _dashboard.ToolSelected += OnToolSelected;
        _workspace.BackRequested += (_, _) => GoHome();
        _backgroundRemover.BackRequested += (_, _) => GoHome();

        // Arxiv va ekran yozuvi PDF vositasi emas — ular bosh sahifadagi kartochkalar
        // orasida emas, yon panelda alohida bo'lim sifatida turadi.
        NavigationItems =
        [
            new NavigationItemViewModel("\uE80F", dashboard),      // PDF vositalari
            new NavigationItemViewModel("\uF12B", archive),        // Arxiv
            new NavigationItemViewModel("\uE714", screenRecorder), // Ekran yozuvi
            new NavigationItemViewModel("\uE946", about)           // Dastur haqida
        ];

        SelectedNavigationItem = NavigationItems[0];

        // Jimgina fon tekshiruvi: natija bo'lsa yon panelda kichik bildirishnoma paydo bo'ladi,
        // bo'lmasa (yoki internet yo'q bo'lsa) foydalanuvchi buni umuman sezmaydi.
        _ = CheckForUpdateSilentlyAsync();
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>Kontent hududida ko'rinadigan sahifa; ko'rinishga App.xaml dagi shablonlar bog'laydi.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    public string ApplicationTitle => "Yordamchi";

    public string ApplicationSubtitle => "PDF vositalari, arxiv va ekran yozuvi";

    public string VersionText =>
        $"Versiya {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.1.0"}";

    public string AuthorText => "Abduxalil Voxidjonov";

    // -----------------------------------------------------------------
    //  Yangilanish bildirishnomasi
    // -----------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText))]
    private UpdateInfo? _availableUpdate;

    /// <summary>Yon paneldagi bildirishnoma tugmasi shu bo'lgandagina ko'rinadi.</summary>
    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateBannerText =>
        AvailableUpdate is null ? string.Empty : $"Yangi versiya: {AvailableUpdate.VersionText}";

    /// <summary>
    /// Fon tekshiruvi. Har qanday nosozlik yutiladi: dastur ochilishida yangilanish xatosi
    /// haqida oyna chiqishi foydalanuvchining ishiga aloqasi yo'q va faqat bezovta qiladi.
    /// </summary>
    private async Task CheckForUpdateSilentlyAsync()
    {
        try
        {
            AvailableUpdate = await _updateService.CheckForUpdateAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AvailableUpdate = null;
        }
    }

    /// <summary>Yon paneldagi mavzu kalitiga ikki tomonlama bog'langan.</summary>
    public bool IsDarkMode
    {
        get => _themeService.IsDarkMode;
        set
        {
            if (value == _themeService.IsDarkMode)
                return;

            _themeService.SetTheme(value ? AppTheme.Dark : AppTheme.Light);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void UseSystemTheme() => _themeService.SetTheme(AppTheme.System);

    /// <summary>Muallif haqidagi sahifaga o'tadi (yon paneldagi imzo bosilganda).</summary>
    /// <remarks>
    /// Indeks bo'yicha emas, mazmuni bo'yicha qidiriladi: yon panelga yangi bo'lim
    /// qo'shilganda bu komanda jimgina boshqa sahifani ochib yubormasligi kerak.
    /// </remarks>
    [RelayCommand]
    private void ShowAbout() =>
        SelectedNavigationItem = NavigationItems.FirstOrDefault(item => item.Content == _about);

    /// <summary>Bosh sahifaga qaytaradi.</summary>
    [RelayCommand]
    private void GoHome()
    {
        // Yon paneldagi tanlov allaqachon "Bosh sahifa" bo'lsa, hodisa qayta ishga tushmaydi,
        // shuning uchun kontentni qo'lda ham tiklaymiz.
        if (SelectedNavigationItem == NavigationItems[0])
            CurrentViewModel = _dashboard;
        else
            SelectedNavigationItem = NavigationItems[0];
    }

    private void OnToolSelected(object? sender, ToolDescriptor tool)
    {
        if (tool.Id == ToolId.BackgroundRemover)
        {
            // Fon olib tashlash uchun ikki panelli maxsus oyna bor — universal oyna emas.
            CurrentViewModel = _backgroundRemover;
            return;
        }

        _workspace.Activate(tool);
        CurrentViewModel = _workspace;
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? oldValue, NavigationItemViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;

        if (newValue is null)
            return;

        newValue.IsSelected = true;
        CurrentViewModel = newValue.Content;
    }
}
