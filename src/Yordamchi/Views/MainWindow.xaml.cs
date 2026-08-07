using System.Windows;
using System.Windows.Media;
using Yordamchi.Helpers;
using Yordamchi.Services.Abstractions;
using Yordamchi.ViewModels;

namespace Yordamchi.Views;

/// <summary>
/// Application shell. The only code here is window composition — Mica backdrop and title bar
/// theming — which has no MVVM equivalent because it needs the HWND.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IThemeService _themeService;
    private bool _usesMicaBackdrop;

    public MainWindow(MainViewModel viewModel, IThemeService themeService, ScreenRecorderViewModel screenRecorder)
    {
        _themeService = themeService;

        InitializeComponent();
        DataContext = viewModel;

        _themeService.ThemeChanged += OnThemeChanged;

        // Oynani kichraytirish — Window ustidagi amal, ViewModel'da mumkin emas.
        // Ekran yozuvi boshlanganda dasturning o'zi videoga tushib qolmasligi uchun kerak.
        screenRecorder.MinimizeRequested += OnMinimizeRequested;

        Closed += (_, _) =>
        {
            _themeService.ThemeChanged -= OnThemeChanged;
            screenRecorder.MinimizeRequested -= OnMinimizeRequested;
        };
    }

    private void OnMinimizeRequested(object? sender, EventArgs e) => WindowState = WindowState.Minimized;

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
