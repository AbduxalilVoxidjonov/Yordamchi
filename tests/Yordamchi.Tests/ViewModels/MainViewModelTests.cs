using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// Qobiqning yangilanish bildirishnomasi. Tekshiruv dastur ochilishida <b>jimgina</b> ketadi:
/// natija bo'lsa yon panelda kichik tugma paydo bo'ladi, xato bo'lsa foydalanuvchi buni
/// umuman sezmasligi kerak.
/// <para>
/// Substitute'lar tayyor (allaqachon yakunlangan) <c>Task</c> qaytargani uchun konstruktordagi
/// fon tekshiruvi shu yerdayoq oxirigacha bajariladi — sinovda kutish shart emas.
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
        "https://github.com/AbduxalilVoxidjonov/PdfEditor/releases/download/v2.2.0/YordamchiSetup-2.2.0.exe",
        "YordamchiSetup-2.2.0.exe",
        123456789,
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Without_an_update_the_sidebar_shows_no_notification()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasUpdate);
        Assert.Equal(string.Empty, vm.UpdateBannerText);
    }

    [Fact]
    public void An_available_update_is_announced_in_the_sidebar()
    {
        _updates.CheckForUpdateAsync().ReturnsForAnyArgs(Task.FromResult<UpdateInfo?>(NewRelease()));

        var vm = CreateViewModel();

        Assert.True(vm.HasUpdate);
        Assert.Equal("Yangi versiya: 2.2.0", vm.UpdateBannerText);
    }

    [Fact]
    public void A_failed_check_is_swallowed_and_shows_nothing()
    {
        // Internet yo'qligi dastur ochilishida oyna chiqarmasligi kerak.
        _updates.CheckForUpdateAsync()
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OperationFailed, "Internet yo'q"));

        var vm = CreateViewModel();

        Assert.False(vm.HasUpdate);
        Assert.Empty(_dialogs.ShownErrors);
        Assert.Empty(_dialogs.ShownInformation);
    }

    [Fact]
    public void The_notification_leads_to_the_about_page()
    {
        // Yon paneldagi tugma mavjud ShowAboutCommand ni chaqiradi — yangilanish kartochkasi
        // aynan o'sha sahifada turadi.
        _updates.CheckForUpdateAsync().ReturnsForAnyArgs(Task.FromResult<UpdateInfo?>(NewRelease()));

        var vm = CreateViewModel();
        vm.ShowAboutCommand.Execute(null);

        Assert.Equal("Dastur haqida", vm.SelectedNavigationItem?.Title);
    }

    private MainViewModel CreateViewModel()
    {
        var engine = Substitute.For<IPdfEngineService>();
        var pdfService = Substitute.For<IPdfService>();

        return new MainViewModel(
            new DashboardViewModel(_dialogs),
            new ToolWorkspaceViewModel(engine, pdfService, _dialogs),
            new BackgroundRemoverViewModel(Substitute.For<IImageBackgroundRemover>(), pdfService, _dialogs),
            new ArchiveViewModel(Substitute.For<IArchiveService>(), _dialogs),
            new ScreenRecorderViewModel(Substitute.For<IScreenRecorderService>(), _dialogs),
            new AboutViewModel(engine, _updates, _dialogs),
            Substitute.For<IThemeService>(),
            _updates);
    }
}
