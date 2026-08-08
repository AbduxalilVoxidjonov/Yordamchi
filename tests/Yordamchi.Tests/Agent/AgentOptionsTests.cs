using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Hosting;

namespace Yordamchi.Tests.Agent;

/// <summary>
/// Buyruq satri sozlamalari. Bu kod ikki marta ishlatiladi — foydalanuvchi yozgan satrni o'qishda
/// va xizmat/bola jarayon uchun satrni <b>qaytadan yozishda</b>. Shu sababli "o'qish → yozish →
/// o'qish" aylanishi ham tekshiriladi: aks holda o'rnatishda tanlangan sozlama xizmatga
/// yetib bormay qolardi.
/// </summary>
public sealed class AgentOptionsTests
{
    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var options = AgentOptions.Parse([]);

        Assert.Null(options.Error);
        Assert.Equal(AgentRunMode.Console, options.Mode);
        Assert.Equal(AgentOptions.DefaultControlPort, options.Port);
        Assert.Equal(CaptureMode.Auto, options.Capture);
        Assert.True(options.AllowInput);
        Assert.True(options.AllowCommands);
        Assert.True(options.ShowTray);
        Assert.True(options.Announce);
    }

    [Fact]
    public void Every_switch_is_understood()
    {
        var options = AgentOptions.Parse(
            ["--port", "6000", "--quality", "80", "--fps", "5", "--capture", "gdi",
             "--no-input", "--no-commands", "--no-tray", "--no-discovery"]);

        Assert.Null(options.Error);
        Assert.Equal(6000, options.Port);
        Assert.Equal(80, options.JpegQuality);
        Assert.Equal(5, options.FramesPerSecond);
        Assert.Equal(CaptureMode.Gdi, options.Capture);
        Assert.False(options.AllowInput);
        Assert.False(options.AllowCommands);
        Assert.False(options.ShowTray);
        Assert.False(options.Announce);
    }

    [Fact]
    public void The_frame_interval_follows_the_frame_rate()
    {
        var options = AgentOptions.Parse(["--fps", "20"]);

        Assert.Equal(50, options.FrameInterval.TotalMilliseconds);
    }

    [Theory]
    [InlineData("--install", AgentRunMode.Install)]
    [InlineData("--uninstall", AgentRunMode.Uninstall)]
    [InlineData("--service", AgentRunMode.Service)]
    [InlineData("--help", AgentRunMode.Help)]
    public void Modes_are_recognised(string argument, AgentRunMode expected)
    {
        Assert.Equal(expected, AgentOptions.Parse([argument]).Mode);
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "99999")]
    [InlineData("--quality", "0")]
    [InlineData("--fps", "60")]
    [InlineData("--capture", "gpu")]
    public void An_out_of_range_value_is_reported_instead_of_being_silently_clamped(string name, string value)
    {
        var options = AgentOptions.Parse([name, value]);

        Assert.NotNull(options.Error);
        Assert.Equal(AgentRunMode.Help, options.Mode);
    }

    [Fact]
    public void A_missing_value_is_reported()
    {
        Assert.NotNull(AgentOptions.Parse(["--port"]).Error);
    }

    [Fact]
    public void An_unknown_switch_is_reported()
    {
        var options = AgentOptions.Parse(["--kuzatuv"]);

        Assert.NotNull(options.Error);
        Assert.Contains("--kuzatuv", options.Error);
    }

    [Fact]
    public void Settings_survive_being_written_back_to_a_command_line()
    {
        var original = AgentOptions.Parse(
            ["--port", "7000", "--quality", "30", "--fps", "4", "--capture", "dxgi", "--no-input", "--no-tray"]);

        var arguments = original.ToArgumentString(AgentRunMode.Service).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var reparsed = AgentOptions.Parse(arguments);

        Assert.Null(reparsed.Error);
        Assert.Equal(AgentRunMode.Service, reparsed.Mode);
        Assert.Equal(7000, reparsed.Port);
        Assert.Equal(30, reparsed.JpegQuality);
        Assert.Equal(4, reparsed.FramesPerSecond);
        Assert.Equal(CaptureMode.Dxgi, reparsed.Capture);
        Assert.False(reparsed.AllowInput);
        Assert.True(reparsed.AllowCommands);
        Assert.False(reparsed.ShowTray);
        Assert.True(reparsed.Announce);
    }

    [Fact]
    public void A_child_process_command_line_carries_the_parent_id_and_drops_service_mode()
    {
        var options = AgentOptions.Parse(["--service", "--port", "5406"]);

        var arguments = options.ToArgumentString(AgentRunMode.Console, parentProcessId: 4242)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var child = AgentOptions.Parse(arguments);

        Assert.Equal(AgentRunMode.Console, child.Mode);
        Assert.Equal(4242, child.ParentProcessId);
    }
}
