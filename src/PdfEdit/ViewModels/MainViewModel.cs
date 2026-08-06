using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.ViewModels;

/// <summary>
/// Shell view model: owns the navigation rail, the current workspace and the theme toggle.
/// The three workspaces are created once at startup and kept alive, so switching pages never
/// throws away loaded thumbnails.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IThemeService _themeService;

    public MainViewModel(
        PdfBuilderViewModel builder,
        PageEditorViewModel editor,
        ImageToPdfViewModel imageToPdf,
        IThemeService themeService)
    {
        _themeService = themeService;
        _themeService.ThemeChanged += (_, _) => OnPropertyChanged(nameof(IsDarkMode));

        NavigationItems =
        [
            new NavigationItemViewModel("\uE8A5", builder),  // Document
            new NavigationItemViewModel("\uE71D", editor),      // AllApps / grid
            new NavigationItemViewModel("\uE91B", imageToPdf)   // Photo
        ];

        SelectedNavigationItem = NavigationItems[1];
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentViewModel))]
    private NavigationItemViewModel? _selectedNavigationItem;

    /// <summary>What the content host shows; templated to a view by the data templates in App.xaml.</summary>
    public ViewModelBase? CurrentViewModel => SelectedNavigationItem?.Content;

    public string ApplicationTitle => "PdfEdit";

    public string VersionText =>
        $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    /// <summary>Two-way bound to the theme switch in the sidebar.</summary>
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

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? oldValue, NavigationItemViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;

        if (newValue is not null)
            newValue.IsSelected = true;
    }
}
