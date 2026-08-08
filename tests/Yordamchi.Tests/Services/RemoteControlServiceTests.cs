using Yordamchi.Models;
using Yordamchi.Services;

namespace Yordamchi.Tests.Services;

/// <summary>
/// Agentni yuklab olish xizmati. Tarmoqqa chiqmasdan tekshiriladigan qismlar: manzil
/// tekshiruvi (faqat GitHub, https) va yaroqsiz manzil hech qanday so'rovsiz rad etilishi.
/// </summary>
public sealed class RemoteControlServiceTests
{
    private readonly RemoteControlService _service = new();

    [Theory]
    [InlineData("https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/download/agent-v1/YordamchiAgentSetup.exe")]
    [InlineData("https://objects.githubusercontent.com/some/asset.exe")]
    public void A_github_https_url_is_accepted(string url)
        => Assert.True(_service.IsDownloadUrlReady(url));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://github.com/a/b/c.exe")]          // https emas
    [InlineData("https://example.com/agent.exe")]        // begona xost
    [InlineData("https://raw.githubusercontent.com/x")]  // ruxsat etilmagan GitHub xosti
    [InlineData("ftp://github.com/a.exe")]
    [InlineData("shunchaki matn")]
    public void A_url_that_is_empty_or_off_github_is_rejected(string? url)
        => Assert.False(_service.IsDownloadUrlReady(url));

    [Fact]
    public void The_default_url_is_a_placeholder_and_is_not_ready()
    {
        // Placeholder holatda manzil bo'sh — foydalanuvchi real havolani o'zi kiritadi.
        Assert.False(_service.IsDownloadUrlReady(_service.DefaultDownloadUrl));
    }

    [Fact]
    public void The_example_url_shows_the_expected_github_shape()
    {
        Assert.True(_service.IsDownloadUrlReady(_service.ExampleDownloadUrl));
        Assert.Contains("github.com", _service.ExampleDownloadUrl);
    }

    [Fact]
    public void The_agent_path_sits_inside_the_download_folder()
    {
        Assert.StartsWith(_service.DownloadFolder, _service.AgentFilePath);
        Assert.EndsWith(_service.AgentFileName, _service.AgentFilePath);
    }

    [Fact]
    public async Task Downloading_an_invalid_url_is_refused_before_any_network_call()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.DownloadAgentAsync("https://example.com/agent.exe"));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task Downloading_an_empty_url_is_refused()
    {
        await Assert.ThrowsAsync<PdfServiceException>(() => _service.DownloadAgentAsync(string.Empty));
    }
}
