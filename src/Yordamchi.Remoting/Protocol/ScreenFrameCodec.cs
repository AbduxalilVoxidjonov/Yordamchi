using System.Buffers.Binary;

namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// <see cref="PacketType.ScreenFrame"/> yukining ichki tuzilishi: kadr o'lchami va rasm
/// baytlari. Ham agent (yozadi), ham master (o'qiydi) shu bir formatdan foydalanadi.
/// <para>
/// Format (little-endian): <c>width(4) || height(4) || rasm baytlari</c>. Rasm baytlari
/// uzunligi paket yukidan kelib chiqadi, shuning uchun alohida uzunlik maydoni shart emas.
/// Kodlash usuli (xom BGRA, JPEG, ...) keyingi bosqichda shu qatlam ustida belgilanadi.
/// </para>
/// </summary>
public static class ScreenFrameCodec
{
    private const int HeaderSize = 8;

    /// <summary>Aql bovar qilmas o'lcham buzilgan ma'lumotni bildiradi — himoya chegarasi.</summary>
    private const int MaxDimension = 16384;

    /// <summary>Kadrni yukka o'giradi.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> image)
    {
        if (width is <= 0 or > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Kadr kengligi noto'g'ri.");

        if (height is <= 0 or > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Kadr balandligi noto'g'ri.");

        var payload = new byte[HeaderSize + image.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), height);
        image.CopyTo(payload.AsSpan(HeaderSize));

        return payload;
    }

    /// <summary>Yukdan kadrni o'qishga urinadi. Buzuq bo'lsa <c>false</c>.</summary>
    public static bool TryParse(ReadOnlySpan<byte> payload, out int width, out int height, out byte[] image)
    {
        width = 0;
        height = 0;
        image = [];

        if (payload.Length < HeaderSize)
            return false;

        var w = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
        var h = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));

        if (w is <= 0 or > MaxDimension || h is <= 0 or > MaxDimension)
            return false;

        width = w;
        height = h;
        image = payload[HeaderSize..].ToArray();
        return true;
    }
}
