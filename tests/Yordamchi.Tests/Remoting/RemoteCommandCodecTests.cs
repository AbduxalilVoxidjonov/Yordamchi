using System.Text;
using Yordamchi.Remoting.Command;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Cheklangan buyruqlarning kodlanishi. Eng muhim shart — <b>ro'yxat yopiq</b>: noma'lum tur
/// bajarilmasligi kerak, chunki buyruq bajarish masofaviy boshqaruvning eng xavfli qismi.
/// </summary>
public sealed class RemoteCommandCodecTests
{
    [Fact]
    public void A_message_survives_the_round_trip()
    {
        var payload = RemoteCommandCodec.Encode(RemoteCommand.ShowMessage("Dars boshlandi"));

        Assert.True(RemoteCommandCodec.TryParse(payload, out var parsed));
        Assert.Equal(RemoteCommandKind.ShowMessage, parsed.Kind);
        Assert.Equal("Dars boshlandi", parsed.Text);
    }

    [Fact]
    public void A_message_with_uzbek_letters_is_not_damaged()
    {
        // O'zbek alifbosidagi belgilar UTF-8 da bir necha bayt — uzunlik baytlarda hisoblanishi
        // shart, aks holda matn kesilib qolardi.
        const string text = "Ekranni o'chiring, iltimos — mashg'ulot tugadi.";
        var payload = RemoteCommandCodec.Encode(RemoteCommand.ShowMessage(text));

        Assert.True(RemoteCommandCodec.TryParse(payload, out var parsed));
        Assert.Equal(text, parsed.Text);
    }

    [Fact]
    public void A_lock_screen_command_needs_no_text()
    {
        var payload = RemoteCommandCodec.Encode(RemoteCommand.LockScreen());

        Assert.True(RemoteCommandCodec.TryParse(payload, out var parsed));
        Assert.Equal(RemoteCommandKind.LockScreen, parsed.Kind);
        Assert.Equal(string.Empty, parsed.Text);
    }

    [Fact]
    public void A_very_long_message_is_shortened_instead_of_being_refused()
    {
        var command = RemoteCommand.ShowMessage(new string('a', RemoteCommand.MaxTextLength + 500));

        Assert.Equal(RemoteCommand.MaxTextLength, command.Text.Length);
        Assert.True(RemoteCommandCodec.TryParse(RemoteCommandCodec.Encode(command), out _));
    }

    [Fact]
    public void An_unknown_command_is_refused()
    {
        var payload = RemoteCommandCodec.Encode(RemoteCommand.LockScreen());
        payload[0] = 99;

        Assert.False(RemoteCommandCodec.TryParse(payload, out _));
    }

    [Fact]
    public void A_declared_length_that_does_not_match_the_payload_is_refused()
    {
        var text = Encoding.UTF8.GetBytes("salom");
        var payload = new byte[4 + text.Length];
        payload[0] = (byte)RemoteCommandKind.ShowMessage;
        payload[2] = 200; // haqiqiy matndan ancha uzun deb e'lon qilinadi
        text.CopyTo(payload, 4);

        Assert.False(RemoteCommandCodec.TryParse(payload, out _));
    }

    [Fact]
    public void A_truncated_payload_is_refused()
    {
        Assert.False(RemoteCommandCodec.TryParse([1, 0], out _));
    }
}
