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
    private readonly ScreenRecorderViewModel _screenRecorder;

    private RecordingOverlayWindow? _overlay;
    private bool _usesMicaBackdrop;

    public MainWindow(
        MainViewModel viewModel,
        IThemeService themeService,
        ScreenRecorderViewModel screenRecorder,
        AboutViewModel about)
    {
        _themeService = themeService;
        _screenRecorder = screenRecorder;

        InitializeComponent();
        DataContext = viewModel;

        _themeService.ThemeChanged += OnThemeChanged;

        // Oyna holati va suzuvchi panel — Window ustidagi amallar, ViewModel'da mumkin emas.
        // Ekran yozuvi boshlanganda dasturning o'zi videoga tushib qolmasligi uchun kerak.
        screenRecorder.MinimizeRequested += OnMinimizeRequested;
        screenRecorder.RestoreRequested += OnRestoreRequested;
        screenRecorder.OverlayVisibilityChanged += OnOverlayVisibilityChanged;

        // Yangilanish o'rnatilishidan oldin dastur yopilishi kerak — o'rnatgich Program Files
        // dagi fayllarni almashtiradi. Yopish ham Window darajasidagi amal.
        about.RestartRequested += OnRestartRequested;

        Closed += (_, _) =>
        {
            _themeService.ThemeChanged -= OnThemeChanged;
            screenRecorder.MinimizeRequested -= OnMinimizeRequested;
            screenRecorder.RestoreRequested -= OnRestoreRequested;
            screenRecorder.OverlayVisibilityChanged -= OnOverlayVisibilityChanged;
            about.RestartRequested -= OnRestartRequested;

            CloseOverlay();
        };
    }

    private void OnMinimizeRequested(object? sender, EventArgs e) => WindowState = WindowState.Minimized;

    private void OnRestartRequested(object? sender, EventArgs e) => Application.Current?.Shutdown();

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Yozuv davomidagi suzuvchi boshqaruv panelini ochadi va yopadi.
    /// <para>
    /// Panel har safar qaytadan yaratiladi: u <c>SourceInitialized</c> da o'zini yozuvdan
    /// yashiradi, ya'ni "yopish" o'rniga shunchaki berkitib qo'yish shu himoyani keyingi
    /// seansda qayta qo'llash imkonini qoldirmasdi.
    /// </para>
    /// </summary>
    private void OnOverlayVisibilityChanged(object? sender, bool visible)
    {
        if (!visible)
        {
            CloseOverlay();
            return;
        }

        CloseOverlay();

        _overlay = new RecordingOverlayWindow
        {
            // Egasi ko'rsatilmaydi: asosiy oyna kichraytirilganda panel ham u bilan birga
            // yashirinib qolardi.
            DataContext = _screenRecorder
        };

        _overlay.Show();
    }

    private void CloseOverlay()
    {
        if (_overlay is null)
            return;

        _overlay.Close();
        _overlay = null;
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
