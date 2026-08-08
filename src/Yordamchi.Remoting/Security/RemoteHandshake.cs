using System.IO;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Remoting.Security;

/// <summary>
/// Ulanish boshidagi kalit almashinuvini oqim (stream) ustida bajaradi. Ikkala tomon ham
/// shu bir kodni ishlatadi — master va agent alohida yozilsa, biri ikkinchisidan bir qadam
/// oldinga/orqaga ketib qolishi oson bo'lardi.
/// <para>
/// Oqim: master vaqtinchalik RSA juftini yaratib ochiq kalitni <see cref="PacketType.Handshake"/>
/// bilan yuboradi; agent yangi AES sessiya kalitini o'rab
/// <see cref="PacketType.HandshakeAck"/> bilan qaytaradi. Shundan so'ng ikkala tomon ham
/// sessiya kalitini biladi.
/// </para>
/// </summary>
public static class RemoteHandshake
{
    /// <summary>Ulanishni boshlagan tomon (master). Sessiya kalitini qaytaradi.</summary>
    public static async Task<byte[]> PerformAsMasterAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var keyPair = KeyExchange.NewKeyPair();
        var publicKey = KeyExchange.ExportPublicKey(keyPair);

        await PacketCodec.WriteAsync(stream, new Packet(PacketType.Handshake, publicKey), cancellationToken)
            .ConfigureAwait(false);

        var ack = await PacketCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        if (ack.Type != PacketType.HandshakeAck)
            throw new ProtocolException($"Handshake kutilgan edi, keldi: {ack.Type}.");

        return KeyExchange.UnwrapSessionKey(keyPair, ack.Payload);
    }

    /// <summary>Ulanishni qabul qilgan tomon (agent). Sessiya kalitini qaytaradi.</summary>
    public static async Task<byte[]> PerformAsAgentAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hello = await PacketCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        if (hello.Type != PacketType.Handshake)
            throw new ProtocolException($"Handshake kutilgan edi, keldi: {hello.Type}.");

        using var masterPublic = KeyExchange.ImportPublicKey(hello.Payload);

        var sessionKey = SessionCipher.NewKey();
        var wrapped = KeyExchange.WrapSessionKey(masterPublic, sessionKey);

        await PacketCodec.WriteAsync(stream, new Packet(PacketType.HandshakeAck, wrapped), cancellationToken)
            .ConfigureAwait(false);

        return sessionKey;
    }
}
