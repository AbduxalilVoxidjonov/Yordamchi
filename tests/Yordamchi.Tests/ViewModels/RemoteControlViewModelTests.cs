using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// "Kompyuterlarni boshqarish" sahifasi. Xizmat haqiqiy — u tarmoqqa faqat yaroqli manzil
/// bilan chiqadi, tekshiriladigan mantiq esa (tugma qachon faol, holat matni) undan mustaqil.
/// </summary>
public sealed class RemoteControlViewModelTests
{
    private readonly FakeDialogService _dialogs = new();

    private RemoteControlViewModel CreateViewModel() => new(new RemoteControlService(), _dialogs);

    [Fact]
    public void The_page_starts_with_no_url_and_download_disabled()
    {
        var vm = CreateViewModel();

        Assert.Equal(string.Empty, vm.AgentDownloadUrl);
        Assert.False(vm.DownloadAgentCommand.CanExecute(null));
    }

    [Fact]
    public void A_github_url_enables_the_download_button()
    {
        var vm = CreateViewModel();

        vm.AgentDownloadUrl = vm.ExampleDownloadUrl;

        Assert.True(vm.DownloadAgentCommand.CanExecute(null));
    }

    [Fact]
    public void An_off_github_url_keeps_the_download_button_disabled()
    {
        var vm = CreateViewModel();

        vm.AgentDownloadUrl = "https://example.com/agent.exe";

        Assert.False(vm.DownloadAgentCommand.CanExecute(null));
    }

    [Fact]
    public void Nothing_can_be_copied_or_opened_before_the_agent_is_downloaded()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsAgentDownloaded);
        Assert.False(vm.OpenDownloadFolderCommand.CanExecute(null));
        Assert.False(vm.CopyAgentPathCommand.CanExecute(null));
        Assert.Contains("hali yuklab olinmagan", vm.AgentStatus);
    }

    [Fact]
    public void The_install_sequence_is_numbered_in_order()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.InstallSteps);
        Assert.Equal(
            Enumerable.Range(1, vm.InstallSteps.Count),
            vm.InstallSteps.Select(step => step.Number));

        // Ruxsat/xabardorlik qadami ochiq yozilgan bo'lishi kerak — bu bo'limning asosiy sharti.
        Assert.Contains(vm.InstallSteps, step => step.Detail.Contains("ko'rinadigan belgi"));
    }
}
