using System.Buffers.Binary;
using System.Text;

namespace Yordamchi.Remoting.Discovery;

/// <summary>Tarmoqdagi tomonning roli.</summary>
public enum PeerRole : byte
{
    Unknown = 0,

    /// <summary>Boshqaruvchi (Yordamchi) — u agentlarni qidiradi.</summary>
    Master = 1,

    /// <summary>Boshqariladigan kompyuterdagi agent — u o'zini e'lon qiladi.</summary>
    Agent = 2
}

/// <summary>
/// Lokal tarmoqda topilish uchun UDP orqali yuboriladigan mayoq (beacon). Agent o'zini
/// e'lon qiladi, master esa bu xabarni eshitib kompyuterni ro'yxatga qo'shadi — shu tufayli
/// IP manzillarni qo'lda kiritish shart emas.
/// <para>
/// Format qasddan sodda va o'zining <b>magic</b> si bilan: begona UDP paketlari (tarmoqda
/// ular ko'p) darhol ajratiladi. Xabar shifrlanmaydi — unda maxfiy narsa yo'q, faqat
/// "shu yerda shunday rol bor, mana port" degan e'lon; haqiqiy autentifikatsiya keyin,
/// TCP handshake'da bo'ladi.
/// </para>
/// </summary>
public sealed class DiscoveryBeacon
{
    /// <summary>Discovery xabarining sarlavha belgisi ("YAD" — Yordamchi Agent Discovery).</summary>
    public static readonly byte[] Magic = [0x59, 0x41, 0x44];

    /// <summary>Discovery uchun kelishilgan UDP port.</summary>
    public const int BroadcastPort = 5405;

    /// <summary>Mashina nomi juda uzun bo'lmasligi kerak — bitta UDP paketiga sig'sin.</summary>
    private const int MaxMachineNameLength = 64;

    public DiscoveryBeacon(PeerRole role, int tcpPort, string machineName)
    {
        ArgumentNullException.ThrowIfNull(machineName);

        if (tcpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(tcpPort), tcpPort, "Port 1..65535 oralig'ida bo'lishi kerak.");

        Role = role;
        TcpPort = tcpPort;

        // Nomni chegaraga keltiramiz: uzun bo'lsa qirqamiz, aks holda paket kattalashardi.
        MachineName = machineName.Length > MaxMachineNameLength
            ? machineName[..MaxMachineNameLength]
            : machineName;
    }

    public PeerRole Role { get; }

    /// <summary>TCP boshqaruv ulanishi shu portda kutiladi.</summary>
    public int TcpPort { get; }

    public string MachineName { get; }

    /// <summary>
    /// Mayoqni UDP paketiga yoziladigan baytlarga o'giradi.
    /// Format: <c>magic(3) || version(1) || role(1) || tcpPort(2) || nameLen(1) || nameUtf8</c>.
    /// </summary>
    public byte[] ToBytes()
    {
        var name = Encoding.UTF8.GetBytes(MachineName);

        // Nom UTF-8 da 64 belgidan uzunroq bo'lishi mumkin (ko'p baytli harflar), shuning
        // uchun bayt sonini ham 255 bilan cheklaymiz — nameLen bitta baytga sig'sin.
        if (name.Length > 255)
            name = name[..255];

        var buffer = new byte[Magic.Length + 1 + 1 + 2 + 1 + name.Length];
        var span = buffer.AsSpan();

        Magic.CopyTo(span);
        span[3] = PacketCodecVersion;
        span[4] = (byte)Role;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), (ushort)TcpPort);
        span[7] = (byte)name.Length;
        name.CopyTo(span[8..]);

        return buffer;
    }

    /// <summary>Protokol versiyasi (paket ramkasi bilan bir xil raqamda yuriladi).</summary>
    private const byte PacketCodecVersion = 1;

    /// <summary>
    /// UDP paketidan mayoqni o'qishga urinadi. Begona yoki buzuq paket bo'lsa <c>null</c>
    /// (istisno emas: discovery portiga har xil paketlar tushishi odatiy hol).
    /// </summary>
    public static DiscoveryBeacon? TryParse(ReadOnlySpan<byte> data)
    {
        // Eng qisqa yaroqli xabar: magic + version + role + port + nameLen (nom bo'sh bo'lishi mumkin).
        const int minLength = 8;

        if (data.Length < minLength)
            return null;

        if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
            return null;

        if (data[3] != PacketCodecVersion)
            return null;

        var role = (PeerRole)data[4];
        if (role is not (PeerRole.Master or PeerRole.Agent))
            return null;

        var tcpPort = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        if (tcpPort == 0)
            return null;

        int nameLength = data[7];
        if (data.Length < minLength + nameLength)
            return null;

        var machineName = Encoding.UTF8.GetString(data.Slice(8, nameLength));

        return new DiscoveryBeacon(role, tcpPort, machineName);
    }
}
