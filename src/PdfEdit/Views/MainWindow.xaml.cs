using System.Windows;
using System.Windows.Media;
using PdfEdit.Helpers;
using PdfEdit.Services.Abstractions;
using PdfEdit.ViewModels;

namespace PdfEdit.Views;

/// <summary>
/// Application shell. The only code here is window composition — Mica backdrop and title bar
/// theming — which has no MVVM equivalent because it needs the HWND.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IThemeService _themeService;
    private bool _usesMicaBackdrop;

    public MainWindow(MainViewModel viewModel, IThemeService themeService)
    {
        _themeService = themeService;

        InitializeComponent();
        DataContext = viewModel;

        _themeService.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => _themeService.ThemeChanged -= OnThemeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyBackdrop();
    }

    private void OnThemeChanged(object? sender, bool isDarkMode)
    {
        WindowBackdrop.SetImmersiveDarkMode(this, isDarkMode);

        // Mica is tinted by the system theme; re-applying keeps the tint in step with ours.
        if (_usesMicaBackdrop)
            ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        _usesMicaBackdrop = WindowBackdrop.TryApplyMica(this, _themeService.IsDarkMode);

        if (_usesMicaBackdrop)
        {
            // The backdrop shows through the window; the sidebar keeps its own translucent fill
            // and the workspace panel supplies the opaque surface.
            Background = Brushes.Transparent;
        }
        else
        {
            // Windows 10 or an unsupported build: fall back to a solid window colour.
            SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        }
    }
}
