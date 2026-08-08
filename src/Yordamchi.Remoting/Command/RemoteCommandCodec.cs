using System.Buffers.Binary;
using System.Text;

namespace Yordamchi.Remoting.Command;

/// <summary>
/// <see cref="RemoteCommand"/> ni <see cref="Protocol.PacketType.Command"/> yukiga o'giradi va
/// qaytaradi (little-endian):
/// <code>
///   kind(1) reserved(1) textLength(2) text(UTF-8, textLength bayt)
/// </code>
/// <para>
/// Tahlil qilish <b>ishonchsiz ma'lumot</b> ustida ishlaydi: noto'g'ri uzunlik, noma'lum tur yoki
/// yetib kelmagan baytlar istisno tashlamaydi — <c>false</c> qaytaradi va agent bunday buyruqni
/// jimgina rad etadi. Buzuq paket agentni yiqitmasligi kerak.
/// </para>
/// </summary>
public static class RemoteCommandCodec
{
    private const int HeaderSize = 4;

    public static byte[] Encode(in RemoteCommand command)
    {
        var text = Encoding.UTF8.GetBytes(command.Text ?? string.Empty);

        // Belgilar soni cheklangan bo'lsa ham, UTF-8 da bir belgi bir necha bayt bo'ladi —
        // shuning uchun chegarani baytlarda ham tekshiramiz.
        if (text.Length > MaxTextBytes)
            text = text[..MaxTextBytes];

        var buffer = new byte[HeaderSize + text.Length];

        buffer[0] = (byte)command.Kind;
        buffer[1] = 0; // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), (ushort)text.Length);
        text.CopyTo(buffer, HeaderSize);

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out RemoteCommand command)
    {
        command = default;

        if (payload.Length < HeaderSize)
            return false;

        var kind = (RemoteCommandKind)payload[0];
        var textLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2));

        if (textLength > MaxTextBytes || payload.Length != HeaderSize + textLength)
            return false;

        string text;
        try
        {
            text = Encoding.UTF8.GetString(payload.Slice(HeaderSize, textLength));
        }
        catch (ArgumentException)
        {
            return false; // Yaroqsiz UTF-8 ketma-ketligi.
        }

        switch (kind)
        {
            case RemoteCommandKind.ShowMessage:
                command = RemoteCommand.ShowMessage(text);
                return true;

            case RemoteCommandKind.LockScreen:
                command = RemoteCommand.LockScreen();
                return true;

            default:
                return false; // Noma'lum buyruq — bajarilmaydi.
        }
    }

    /// <summary>Matnning UTF-8 dagi eng katta o'lchami (har belgi eng ko'pi 4 bayt).</summary>
    private const int MaxTextBytes = RemoteCommand.MaxTextLength * 4;
}
