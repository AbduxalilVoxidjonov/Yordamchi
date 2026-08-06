using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PdfEdit.Services;
using PdfEdit.Services.Abstractions;
using PdfEdit.ViewModels;
using PdfEdit.Views;

namespace PdfEdit;

/// <summary>
/// Composition root. Builds the service container, applies the startup theme and shows the shell.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();

        // The theme must be applied before the first window is created, otherwise the title bar
        // flashes in the wrong colour on dark-mode systems.
        _services.GetRequiredService<IThemeService>().Initialize(AppTheme.System);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Workspaces are singletons so switching pages preserves loaded thumbnails and edits.
        services.AddSingleton<PdfBuilderViewModel>();
        services.AddSingleton<PageEditorViewModel>();
        services.AddSingleton<ImageToPdfViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Last line of defence: a bug in the UI layer should surface as a message, not as a
    /// silent process exit that loses the user's unsaved page order.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"{e.Exception.Message}\n\nThe application will try to continue.",
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
