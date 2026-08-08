using System.Buffers.Binary;
using System.IO;

namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// Paketni baytlarga o'girish va qaytarish. Ramka (frame) tuzilishi — hammasi
/// <b>little-endian</b>:
/// <code>
///   ofset 0..1   magic       "YA" (0x59 0x41)
///   ofset 2      version     protokol versiyasi
///   ofset 3      type        PacketType
///   ofset 4      flags       PacketFlags
///   ofset 5      reserved    0 (kelajakka joy)
///   ofset 6..9   length      yuk uzunligi (uint32)
///   ofset 10..13 crc32       yukning CRC-32 si
///   ofset 14..   payload     yuk baytlari
/// </code>
/// <para>
/// <b>Nega uzunlik chegarasi bor.</b> TCP oqimidan kelgan uzunlikka ko'r-ko'rona ishonib
/// bo'lmaydi: buzuq yoki yovuz tomon 4 GB uzunlik yuborsa, biz shuncha xotira ajratib
/// osilib qolardik. Shuning uchun <see cref="MaxPayloadLength"/> dan katta uzunlik darhol
/// rad etiladi.
/// </para>
/// </summary>
public static class PacketCodec
{
    /// <summary>Sarlavha belgisi: begona ulanishni birinchi ikki baytda ajratadi.</summary>
    public static readonly byte[] Magic = [0x59, 0x41]; // "YA"

    /// <summary>Protokol versiyasi. O'zgarsa, eski tomon buni ko'rib mos kelmasligini biladi.</summary>
    public const byte ProtocolVersion = 1;

    /// <summary>Sarlavha uzunligi (bayt).</summary>
    public const int HeaderSize = 14;

    /// <summary>
    /// Bitta yukning eng katta uzunligi (64 MB). Ekran kadri ham shu chegaraga sig'adi,
    /// lekin yaroqsiz uzunlik xotirani tugatib qo'ymaydi.
    /// </summary>
    public const int MaxPayloadLength = 64 * 1024 * 1024;

    // =====================================================================================
    //  Kodlash
    // =====================================================================================

    /// <summary>Paketni to'liq ramka (sarlavha + yuk) qilib baytlarga o'giradi.</summary>
    public static byte[] Encode(in Packet packet)
    {
        var payload = packet.Payload;

        if (payload.Length > MaxPayloadLength)
        {
            throw new ProtocolException(
                $"Yuk juda katta: {payload.Length} bayt (chegara {MaxPayloadLength}).");
        }

        var buffer = new byte[HeaderSize + payload.Length];
        WriteHeader(buffer, packet.Type, packet.Flags, payload);
        payload.CopyTo(buffer.AsSpan(HeaderSize));

        return buffer;
    }

    private static void WriteHeader(Span<byte> buffer, PacketType type, PacketFlags flags, ReadOnlySpan<byte> payload)
    {
        buffer[0] = Magic[0];
        buffer[1] = Magic[1];
        buffer[2] = ProtocolVersion;
        buffer[3] = (byte)type;
        buffer[4] = (byte)flags;
        buffer[5] = 0; // reserved
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(6, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(10, 4), Crc32.Compute(payload));
    }

    // =====================================================================================
    //  Buferdan o'qish (nol nusxa bo'lmasa ham, sinovga qulay)
    // =====================================================================================

    /// <summary>
    /// Buferning boshidan bitta paketni o'qishga urinadi.
    /// </summary>
    /// <returns>
    /// To'liq paket bo'lsa <c>true</c> va <paramref name="bytesConsumed"/> — sarf bo'lgan
    /// baytlar. Baytlar hali yetarli emas bo'lsa <c>false</c>. Ramka buzuq bo'lsa
    /// <see cref="ProtocolException"/>.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out Packet packet, out int bytesConsumed)
    {
        packet = default;
        bytesConsumed = 0;

        if (buffer.Length < HeaderSize)
            return false;

        if (buffer[0] != Magic[0] || buffer[1] != Magic[1])
            throw new ProtocolException("Sarlavha belgisi (magic) mos kelmadi — begona yoki buzilgan ma'lumot.");

        if (buffer[2] != ProtocolVersion)
            throw new ProtocolException($"Protokol versiyasi mos kelmadi: {buffer[2]} (kutilgan {ProtocolVersion}).");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(6, 4));

        if (length > MaxPayloadLength)
            throw new ProtocolException($"Yuk uzunligi juda katta: {length} (chegara {MaxPayloadLength}).");

        var total = HeaderSize + (int)length;
        if (buffer.Length < total)
            return false; // Yuk hali to'liq kelmagan.

        var payloadSpan = buffer.Slice(HeaderSize, (int)length);
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(10, 4));

        if (Crc32.Compute(payloadSpan) != expectedCrc)
            throw new ProtocolException("Nazorat yig'indisi (CRC) mos kelmadi — yuk buzilgan.");

        packet = new Packet((PacketType)buffer[3], payloadSpan.ToArray(), (PacketFlags)buffer[4]);
        bytesConsumed = total;
        return true;
    }

    // =====================================================================================
    //  Oqimdan o'qish/yozish (agent va master shu bilan ishlaydi)
    // =====================================================================================

    /// <summary>Paketni oqimga yozadi.</summary>
    public static async Task WriteAsync(Stream stream, Packet packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bytes = Encode(packet);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Oqimdan bitta to'liq paketni o'qiydi: avval sarlavha, so'ng aynan e'lon qilingan
    /// uzunlikdagi yuk. Oqim tugab qolsa <see cref="EndOfStreamException"/>.
    /// </summary>
    public static async Task<Packet> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (header[0] != Magic[0] || header[1] != Magic[1])
            throw new ProtocolException("Sarlavha belgisi (magic) mos kelmadi — begona yoki buzilgan ma'lumot.");

        if (header[2] != ProtocolVersion)
            throw new ProtocolException($"Protokol versiyasi mos kelmadi: {header[2]} (kutilgan {ProtocolVersion}).");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(6, 4));

        if (length > MaxPayloadLength)
            throw new ProtocolException($"Yuk uzunligi juda katta: {length} (chegara {MaxPayloadLength}).");

        var payload = new byte[length];
        if (length > 0)
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(10, 4));
        if (Crc32.Compute(payload) != expectedCrc)
            throw new ProtocolException("Nazorat yig'indisi (CRC) mos kelmadi — yuk buzilgan.");

        return new Packet((PacketType)header[3], payload, (PacketFlags)header[4]);
    }
}
