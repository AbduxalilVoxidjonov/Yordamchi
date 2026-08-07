using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Yordamchi.Services;
using Yordamchi.Services.Abstractions;
using Yordamchi.ViewModels;
using Yordamchi.Views;

namespace Yordamchi;

/// <summary>
/// Kompozitsiya ildizi: xizmatlar konteynerini quradi, boshlang'ich mavzuni qo'llaydi va
/// asosiy oynani ko'rsatadi.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ConfigureServices();

        // Mavzu birinchi oyna yaratilishidan OLDIN qo'llanishi kerak, aks holda qorong'i
        // rejimdagi tizimda sarlavha paneli bir lahza noto'g'ri rangda yaltirab ketadi.
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

        // ---------- Modul xizmatlari ----------
        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IPdfManipulatorService, PdfManipulatorService>();
        services.AddSingleton<IOcrService, OcrService>();
        services.AddSingleton<IImageBackgroundRemover, OnnxBackgroundRemover>();
        services.AddSingleton<IDocumentConversionService, DocumentConversionService>();

        // Barcha modullarni birlashtiruvchi fasad — UI faqat shu bilan ishlaydi.
        services.AddSingleton<IPdfEngineService, PdfEngineService>();

        // Ekran yozuvi PDF quvuriga umuman aloqador emas, shuning uchun u fasadga
        // qo'shilmaydi va o'z sahifasi bilan to'g'ridan-to'g'ri ishlaydi.
        services.AddSingleton<IScreenRecorderService, ScreenRecorderService>();

        // ---------- Qobiq xizmatlari ----------
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // ---------- Sahifalar ----------
        // Yagona nusxada yashaydi: sahifalar almashganda yuklangan eskizlar saqlanib qoladi.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ToolWorkspaceViewModel>();
        services.AddSingleton<BackgroundRemoverViewModel>();
        services.AddSingleton<ScreenRecorderViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Oxirgi himoya chizig'i: UI qatlamidagi xato dasturni jimgina yopib, foydalanuvchining
    /// saqlanmagan ishini yo'qotmasligi kerak.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"{e.Exception.Message}\n\nDastur ishlashda davom etishga harakat qiladi.",
            "Kutilmagan xato",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
