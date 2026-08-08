using Yordamchi.Remoting.Discovery;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Lokal tarmoqda topilish mayog'i. Discovery portiga har xil UDP paketlari tushadi,
/// shuning uchun begona yoki buzuq xabar <b>istisno emas, <c>null</c></b> qaytarishi kerak.
/// </summary>
public sealed class DiscoveryBeaconTests
{
    [Fact]
    public void A_beacon_survives_serialize_then_parse()
    {
        var beacon = new DiscoveryBeacon(PeerRole.Agent, 5405, "SINF-PC-12");

        var parsed = DiscoveryBeacon.TryParse(beacon.ToBytes());

        Assert.NotNull(parsed);
        Assert.Equal(PeerRole.Agent, parsed!.Role);
        Assert.Equal(5405, parsed.TcpPort);
        Assert.Equal("SINF-PC-12", parsed.MachineName);
    }

    [Fact]
    public void An_empty_machine_name_is_allowed()
    {
        var parsed = DiscoveryBeacon.TryParse(new DiscoveryBeacon(PeerRole.Master, 9000, string.Empty).ToBytes());

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, parsed!.MachineName);
    }

    [Fact]
    public void A_very_long_name_is_trimmed_but_still_valid()
    {
        var parsed = DiscoveryBeacon.TryParse(new DiscoveryBeacon(PeerRole.Agent, 5405, new string('A', 500)).ToBytes());

        Assert.NotNull(parsed);
        Assert.True(parsed!.MachineName.Length <= 64);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 })]                          // magic emas
    [InlineData(new byte[] { 0x59, 0x41, 0x44 })]                          // faqat magic, qolgani yo'q
    [InlineData(new byte[] { 0x59, 0x41, 0x44, 99, 2, 0x2D, 0x15, 0 })]    // versiya mos emas
    public void Garbage_or_short_data_parses_to_null(byte[] data)
        => Assert.Null(DiscoveryBeacon.TryParse(data));

    [Fact]
    public void An_unknown_role_is_rejected()
    {
        var bytes = new DiscoveryBeacon(PeerRole.Agent, 5405, "PC").ToBytes();
        bytes[4] = 99; // yaroqsiz rol

        Assert.Null(DiscoveryBeacon.TryParse(bytes));
    }

    [Fact]
    public void A_zero_port_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryBeacon(PeerRole.Agent, 0, "PC"));
    }
}
