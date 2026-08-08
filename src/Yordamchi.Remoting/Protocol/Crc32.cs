namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// CRC-32 (IEEE 802.3) nazorat yig'indisi. Bu <b>xavfsizlik</b> emas — buzilishni bilib
/// turib qilingan o'zgartirishdan himoya AES-GCM teg orqali bo'ladi. CRC faqat tasodifiy
/// buzilishni (tarmoqdagi bit almashinishi, noto'g'ri o'qilgan uzunlik) tez ilg'aydi.
/// </summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
                entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;

            table[i] = entry;
        }

        return table;
    }

    /// <summary>Berilgan baytlar uchun CRC-32 qiymatini hisoblaydi.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
            crc = (crc >> 8) ^ Table[(crc ^ value) & 0xFF];

        return crc ^ 0xFFFFFFFFu;
    }
}
