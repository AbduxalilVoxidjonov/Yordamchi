using System.IO;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// Handshake tugagach, paketlarni sessiya kaliti bilan shifrlab yuboradigan/qabul qiladigan
/// yupqa qatlam. Yuqori kod endi shifrlash tafsilotini bilmaydi — u faqat "shu turdagi shu
/// yukni yubor" deydi, kanal esa uni AES-GCM bilan o'rab, <see cref="PacketFlags.Encrypted"/>
/// bayrog'i bilan jo'natadi.
/// </summary>
public static class SecureChannel
{
    /// <summary>Yukni shifrlab, berilgan turdagi paket qilib oqimga yozadi.</summary>
    public static Task SendAsync(
        Stream stream,
        byte[] sessionKey,
        PacketType type,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);

        var encrypted = SessionCipher.Encrypt(sessionKey, payload);
        return PacketCodec.WriteAsync(stream, new Packet(type, encrypted, PacketFlags.Encrypted), cancellationToken);
    }

    /// <summary>
    /// Oqimdan bitta paketni o'qiydi va shifrlangan bo'lsa ochadi. Natijada yuk allaqachon
    /// ochilgan (oddiy matn) holatda qaytadi.
    /// </summary>
    public static async Task<Packet> ReceiveAsync(
        Stream stream,
        byte[] sessionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var packet = await PacketCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);

        // Handshake'dan keyin hamma narsa shifrlangan bo'lishi kutiladi; shifrlanmagan paket
        // — protokol buzilishi yoki begona ma'lumot, shuning uchun ochilmaydi va o'zicha qaytadi.
        if (!packet.IsEncrypted)
            return packet;

        var plaintext = SessionCipher.Decrypt(sessionKey, packet.Payload);
        return new Packet(packet.Type, plaintext);
    }
}
