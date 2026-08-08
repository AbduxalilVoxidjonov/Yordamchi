using System.IO;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Protokol ramkasi (frame): paketni baytga o'girish, qaytarish va oqim orqali o'qish.
/// E'tibor buzilishni <b>ilg'ashda</b>: noto'g'ri magic, oshib ketgan uzunlik va CRC
/// mos kelmasligi ataylab rad etiladi — aks holda begona yoki buzuq ma'lumot ichkariga o'tardi.
/// </summary>
public sealed class PacketCodecTests
{
    [Fact]
    public void A_packet_survives_encode_then_decode()
    {
        var original = new Packet(PacketType.ScreenFrame, [1, 2, 3, 4, 5], PacketFlags.Encrypted);

        var bytes = PacketCodec.Encode(original);
        var ok = PacketCodec.TryDecode(bytes, out var decoded, out var consumed);

        Assert.True(ok);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(PacketType.ScreenFrame, decoded.Type);
        Assert.Equal(PacketFlags.Encrypted, decoded.Flags);
        Assert.True(decoded.IsEncrypted);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void An_empty_payload_is_valid()
    {
        var bytes = PacketCodec.Encode(new Packet(PacketType.Ping, []));

        Assert.True(PacketCodec.TryDecode(bytes, out var decoded, out _));
        Assert.Equal(PacketType.Ping, decoded.Type);
        Assert.Empty(decoded.Payload);
    }

    [Fact]
    public void A_half_arrived_frame_decodes_to_nothing_yet()
    {
        var bytes = PacketCodec.Encode(new Packet(PacketType.Command, [9, 8, 7, 6]));

        // Sarlavha to'liq, lekin yuk yarim kelgan — bu xato emas, shunchaki "hali kutamiz".
        var partial = bytes.AsSpan(0, bytes.Length - 2);

        Assert.False(PacketCodec.TryDecode(partial, out _, out var consumed));
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void A_wrong_magic_is_rejected()
    {
        var bytes = PacketCodec.Encode(new Packet(PacketType.Ping, [1]));
        bytes[0] = 0x00; // magic buzildi

        Assert.Throws<ProtocolException>(() => PacketCodec.TryDecode(bytes, out _, out _));
    }

    [Fact]
    public void A_tampered_payload_fails_the_checksum()
    {
        var bytes = PacketCodec.Encode(new Packet(PacketType.Command, [10, 20, 30]));
        bytes[^1] ^= 0xFF; // yuk o'zgartirildi, CRC esa eski

        Assert.Throws<ProtocolException>(() => PacketCodec.TryDecode(bytes, out _, out _));
    }

    [Fact]
    public async Task Packets_written_to_a_stream_read_back_in_order()
    {
        using var stream = new MemoryStream();

        await PacketCodec.WriteAsync(stream, new Packet(PacketType.Handshake, [1]));
        await PacketCodec.WriteAsync(stream, new Packet(PacketType.ScreenFrame, [2, 2, 2]));
        await PacketCodec.WriteAsync(stream, new Packet(PacketType.Disconnect, []));

        stream.Position = 0;

        var first = await PacketCodec.ReadAsync(stream);
        var second = await PacketCodec.ReadAsync(stream);
        var third = await PacketCodec.ReadAsync(stream);

        Assert.Equal(PacketType.Handshake, first.Type);
        Assert.Equal(new byte[] { 1 }, first.Payload);
        Assert.Equal(PacketType.ScreenFrame, second.Type);
        Assert.Equal(new byte[] { 2, 2, 2 }, second.Payload);
        Assert.Equal(PacketType.Disconnect, third.Type);
        Assert.Empty(third.Payload);
    }

    [Fact]
    public async Task A_truncated_stream_throws_end_of_stream()
    {
        var bytes = PacketCodec.Encode(new Packet(PacketType.Command, [5, 5, 5, 5]));
        using var stream = new MemoryStream(bytes[..^2]); // yuk yarim

        await Assert.ThrowsAsync<EndOfStreamException>(() => PacketCodec.ReadAsync(stream));
    }

    [Fact]
    public void The_crc_matches_a_known_value()
    {
        // "123456789" uchun CRC-32 (IEEE) — sinovlarda keng ishlatiladigan tekshiruv qiymati.
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xCBF43926u, Crc32.Compute(data));
    }
}
