using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly TransliterationViewModel _transliteration;
    private readonly NumberSystemViewModel _numberSystem;
    private readonly RemoteControlViewModel _remoteControl;
    private readonly RemoteViewerViewModel _remoteViewer;
    private readonly AboutViewModel _about;

    public MainViewModel(
        DashboardViewModel dashboard,
        ToolWorkspaceViewModel workspace,
        BackgroundRemoverViewModel backgroundRemover,
        ArchiveViewModel archive,
        ScreenRecorderViewModel screenRecorder,
        TransliterationViewModel transliteration,
        NumberSystemViewModel numberSystem,
        RemoteControlViewModel remoteControl,
        RemoteViewerViewModel remoteViewer,
        AboutViewModel about,
        IThemeService themeService)
    {
        _dashboard = dashboard;
        _workspace = workspace;
        _backgroundRemover = backgroundRemover;
        _archive = archive;
        _screenRecorder = screenRecorder;
        _transliteration = transliteration;
        _numberSystem = numberSystem;
        _remoteControl = remoteControl;
        _remoteViewer = remoteViewer;
        _about = about;
        _themeService = themeService;

        _themeService.ThemeChanged += (_, _) => OnPropertyChanged(nameof(IsDarkMode));

        _dashboard.ToolSelected += OnToolSelected;
        _workspace.BackRequested += (_, _) => GoHome();
        _backgroundRemover.BackRequested += (_, _) => GoHome();

        // Arxiv, ekran yozuvi, o'girish va sanoq sistemalari PDF vositasi emas — ular bosh
        // sahifadagi kartochkalar orasida emas, yon panelda alohida bo'lim sifatida turadi.
        NavigationItems =
        [
            new NavigationItemViewModel("\uE80F", dashboard),       // PDF vositalari
            new NavigationItemViewModel("\uF12B", archive),         // Arxiv
            new NavigationItemViewModel("\uE714", screenRecorder),  // Ekran yozuvi
            new NavigationItemViewModel("\uF2B7", transliteration), // Kirill ↔ Lotin
            new NavigationItemViewModel("\uE8EF", numberSystem),    // Sanoq sistemasi
            new NavigationItemViewModel("\uE977", remoteControl),   // Kompyuterlarni boshqarish
            new NavigationItemViewModel("\uE7F4", remoteViewer),    // Kompyuter ekranlari
            new NavigationItemViewModel("\uE946", about)            // Dastur haqida
        ];

        SelectedNavigationItem = NavigationItems[0];

        // Yangilanishni qobiq emas, "Dastur haqida" sahifasi tekshiradi — bu yerda faqat
        // natijani ko'zgu qilamiz. Hodisaga tayanish shart: tekshiruv tarmoq orqali ketadi
        // va konstruktordan ancha keyin tugashi mumkin.
        _about.PropertyChanged += OnAboutPropertyChanged;
        RefreshAboutNotification();
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>Kontent hududida ko'rinadigan sahifa; ko'rinishga App.xaml dagi shablonlar bog'laydi.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    public string ApplicationTitle => "Yordamchi";

    public string ApplicationSubtitle => "PDF, arxiv, ekran yozuvi va boshqa vositalar";

    public string VersionText =>
        $"Versiya {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.3.0"}";

    public string AuthorText => "Abduxalil Voxidjonov";

    // -----------------------------------------------------------------
    //  Yon panelni yig'ish
    // -----------------------------------------------------------------

    /// <summary>
    /// Yon panel yig'ilganmi: yig'ilgan holatda faqat nishonlar ko'rinadi va ishchi hudud
    /// kengayadi. Holat faqat shu seansda saqlanadi — dasturda sozlamalarni diskka yozadigan
    /// joy yo'q (mavzu ham xuddi shunday), shuning uchun bu yerda ham yangi saqlash mexanizmi
    /// o'ylab topilmadi.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationToggleHint))]
    private bool _isNavigationCollapsed;

    /// <summary>Burger tugmasining izohi — u nima qilishini aytadi, holatni emas.</summary>
    public string NavigationToggleHint =>
        IsNavigationCollapsed ? "Yon panelni ochish" : "Yon panelni yig'ish";

    [RelayCommand]
    private void ToggleNavigation() => IsNavigationCollapsed = !IsNavigationCollapsed;

    // -----------------------------------------------------------------
    //  Yangilanish nishoni
    // -----------------------------------------------------------------

    private void OnAboutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Bo'sh nom — "hamma xossa o'zgardi" degan kelishuv, shuning uchun uni ham qabul qilamiz.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AboutViewModel.HasUpdate))
            RefreshAboutNotification();
    }

    /// <summary>
    /// "Dastur haqida" bandi yonidagi kichik nuqtani yoqadi yoki o'chiradi. Band indeks bo'yicha
    /// emas, mazmuni bo'yicha topiladi — yon panelga yangi bo'lim qo'shilsa ham to'g'ri qoladi.
    /// </summary>
    private void RefreshAboutNotification()
    {
        var item = NavigationItems.FirstOrDefault(navigationItem => navigationItem.Content == _about);

        if (item is not null)
            item.HasNotification = _about.HasUpdate;
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
