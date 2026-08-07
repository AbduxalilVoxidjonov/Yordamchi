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
    private readonly ScreenRecorderViewModel _screenRecorder;
    private readonly AboutViewModel _about;

    public MainViewModel(
        DashboardViewModel dashboard,
        ToolWorkspaceViewModel workspace,
        BackgroundRemoverViewModel backgroundRemover,
        ScreenRecorderViewModel screenRecorder,
        AboutViewModel about,
        IThemeService themeService)
    {
        _dashboard = dashboard;
        _workspace = workspace;
        _backgroundRemover = backgroundRemover;
        _screenRecorder = screenRecorder;
        _about = about;
        _themeService = themeService;

        _themeService.ThemeChanged += (_, _) => OnPropertyChanged(nameof(IsDarkMode));

        _dashboard.ToolSelected += OnToolSelected;
        _workspace.BackRequested += (_, _) => GoHome();
        _backgroundRemover.BackRequested += (_, _) => GoHome();

        // Ekran yozuvi PDF vositasi emas — u bosh sahifadagi kartochkalar orasida emas,
        // yon panelda alohida bo'lim sifatida turadi.
        NavigationItems =
        [
            new NavigationItemViewModel("", dashboard),      // PDF vositalari
            new NavigationItemViewModel("", screenRecorder), // Ekran yozuvi
            new NavigationItemViewModel("", about)           // Dastur haqida
        ];

        SelectedNavigationItem = NavigationItems[0];
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>Kontent hududida ko'rinadigan sahifa; ko'rinishga App.xaml dagi shablonlar bog'laydi.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    public string ApplicationTitle => "Yordamchi";

    public string ApplicationSubtitle => "PDF vositalari va ekran yozuvi";

    public string VersionText =>
        $"Versiya {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0"}";

    public string AuthorText => "Abduxalil Voxidjonov";

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
