using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// Qobiqning yon panelidagi yangilanish nishoni. Qobiq hech narsani <b>o'zi tekshirmaydi</b> —
/// tekshiruv "Dastur haqida" sahifasida bo'ladi, bu yerda esa faqat natija ko'zgu qilinadi:
/// yangi versiya topilsa bo'lim yonida kichik nuqta paydo bo'ladi.
/// <para>
/// Substitute'lar tayyor (allaqachon yakunlangan) <c>Task</c> qaytargani uchun
/// <see cref="AboutViewModel"/> konstruktoridagi tekshiruv shu yerdayoq oxirigacha bajariladi —
/// sinovda kutish shart emas.
/// </para>
/// </summary>
public sealed class MainViewModelTests
{
    private readonly IUpdateService _updates = Substitute.For<IUpdateService>();
    private readonly FakeDialogService _dialogs = new();

    private static UpdateInfo NewRelease() => new(
        new Version(2, 2, 0),
        "v2.2.0",
        "Yordamchi 2.2.0",
        "Izohlar",
        "https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/v2.2.0/YordamchiSetup-2.2.0.exe",
        "YordamchiSetup-2.2.0.exe",
        123456789,
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Without_an_update_no_navigation_item_is_marked()
    {
        var vm = CreateViewModel();

        Assert.All(vm.NavigationItems, item => Assert.False(item.HasNotification));
    }

    [Fact]
    public void An_available_update_marks_the_about_item()
    {
        _updates.CheckForUpdateAsync().ReturnsForAnyArgs(Task.FromResult<UpdateInfo?>(NewRelease()));

        var vm = CreateViewModel();

        var about = vm.NavigationItems.Single(item => item.Title == "Dastur haqida");
        Assert.True(about.HasNotification);

        // Nishon faqat o'sha bo'limda: qolganlariga tegmaydi.
        Assert.All(
            vm.NavigationItems.Where(item => item != about),
            item => Assert.False(item.HasNotification));
    }

    [Fact]
    public void The_mark_appears_from_the_real_github_release()
    {
        // Nishon qo'lda yasalgan namunada emas, serverdan olingan haqiqiy javobdan chiqqan
        // ma'lumot bilan tekshiriladi.
        var real = UpdateService.ParseRelease(GitHubReleaseSample.Json, new Version(2, 0, 0));
        Assert.NotNull(real);

        _updates.CheckForUpdateAsync().ReturnsForAnyArgs(Task.FromResult<UpdateInfo?>(real));

        var vm = CreateViewModel();

        Assert.True(vm.NavigationItems.Single(item => item.Title == "Dastur haqida").HasNotification);
    }

    [Fact]
    public void A_failed_check_is_swallowed_and_marks_nothing()
    {
        // Internet yo'qligi dastur ochilishida oyna chiqarmasligi va nishon qo'ymasligi kerak.
        _updates.CheckForUpdateAsync()
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OperationFailed, "Internet yo'q"));

        var vm = CreateViewModel();

        Assert.All(vm.NavigationItems, item => Assert.False(item.HasNotification));
        Assert.Empty(_dialogs.ShownErrors);
        Assert.Empty(_dialogs.ShownInformation);
    }

    [Fact]
    public async Task A_release_found_by_the_check_button_lights_the_mark_up()
    {
        // Ochilishda yangilanish yo'q edi; foydalanuvchi "Tekshirish" ni bosgan paytda paydo
        // bo'ldi — qobiq buni hodisa orqali bilishi kerak, boshlang'ich qiymatdan emas.
        var about = CreateAbout();
        var vm = CreateViewModel(about);
        var item = vm.NavigationItems.Single(navigationItem => navigationItem.Title == "Dastur haqida");

        Assert.False(item.HasNotification);

        _updates.CheckForUpdateAsync(true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UpdateInfo?>(NewRelease()));

        await about.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.True(item.HasNotification);
    }

    [Fact]
    public void The_notification_leads_to_the_about_page()
    {
        // Nishon bosiladigan tugma emas — yon paneldagi bo'limning o'zi "Dastur haqida" ni
        // ochadi, yangilanish kartochkasi esa aynan o'sha sahifada turadi.
        var vm = CreateViewModel();
        vm.ShowAboutCommand.Execute(null);

        Assert.Equal("Dastur haqida", vm.SelectedNavigationItem?.Title);
    }

    // =================================================================================
    //  Yon panelni yig'ish
    // =================================================================================

    [Fact]
    public void The_side_panel_starts_open()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsNavigationCollapsed);
        Assert.Equal("Yon panelni yig'ish", vm.NavigationToggleHint);
    }

    [Fact]
    public void The_burger_button_folds_the_side_panel_and_opens_it_again()
    {
        var vm = CreateViewModel();

        vm.ToggleNavigationCommand.Execute(null);

        Assert.True(vm.IsNavigationCollapsed);
        Assert.Equal("Yon panelni ochish", vm.NavigationToggleHint);

        vm.ToggleNavigationCommand.Execute(null);

        Assert.False(vm.IsNavigationCollapsed);
    }

    [Fact]
    public void Folding_the_panel_keeps_the_current_page()
    {
        // Yig'ish — faqat ko'rinish holati; u navigatsiyaga tegmasligi kerak.
        var vm = CreateViewModel();
        vm.ShowAboutCommand.Execute(null);

        var page = vm.CurrentViewModel;
        vm.ToggleNavigationCommand.Execute(null);

        Assert.Same(page, vm.CurrentViewModel);
        Assert.Equal("Dastur haqida", vm.SelectedNavigationItem?.Title);
    }

    private AboutViewModel CreateAbout() =>
        new(Substitute.For<IPdfEngineService>(), _updates, _dialogs);

    private MainViewModel CreateViewModel(AboutViewModel? about = null)
    {
        var engine = Substitute.For<IPdfEngineService>();
        var pdfService = Substitute.For<IPdfService>();

        return new MainViewModel(
            new DashboardViewModel(_dialogs),
            new ToolWorkspaceViewModel(engine, pdfService, _dialogs),
            new BackgroundRemoverViewModel(Substitute.For<IImageBackgroundRemover>(), pdfService, _dialogs),
            new ArchiveViewModel(Substitute.For<IArchiveService>(), _dialogs),
            new ScreenRecorderViewModel(Substitute.For<IScreenRecorderService>(), _dialogs),
            new TransliterationViewModel(Substitute.For<ITransliterationService>(), _dialogs),
            new NumberSystemViewModel(new NumberSystemService(), _dialogs),
            new RemoteControlViewModel(new RemoteControlService(), _dialogs),
            new RemoteViewerViewModel(_dialogs),
            about ?? CreateAbout(),
            Substitute.For<IThemeService>());
    }
}
