namespace Yordamchi.Remoting.Protocol;

/// <summary>Paket ustidagi bayroqlar. Hozircha bittasi bor, lekin bir bayt kelajakka joy qoldiradi.</summary>
[Flags]
public enum PacketFlags : byte
{
    None = 0,

    /// <summary>Yuk (payload) AES-GCM bilan shifrlangan.</summary>
    Encrypted = 1
}

/// <summary>
/// Master va agent orasidagi bitta xabar: turi, bayroqlari va xom yuki (payload).
/// <para>
/// Yukning <b>ichida nima borligini</b> paketning o'zi bilmaydi — u shunchaki baytlar
/// tashuvchisi. Shifrlash (<see cref="PacketFlags.Encrypted"/>) va yuk formatini yuqori
/// qatlam hal qiladi, shu tufayli protokol ramkasi kripto va ma'lumot ko'rinishidan mustaqil.
/// </para>
/// </summary>
public readonly struct Packet
{
    public Packet(PacketType type, byte[] payload, PacketFlags flags = PacketFlags.None)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Type = type;
        Payload = payload;
        Flags = flags;
    }

    public PacketType Type { get; }

    public PacketFlags Flags { get; }

    public byte[] Payload { get; }

    /// <summary>Yuk shifrlanganmi.</summary>
    public bool IsEncrypted => (Flags & PacketFlags.Encrypted) != 0;
}
