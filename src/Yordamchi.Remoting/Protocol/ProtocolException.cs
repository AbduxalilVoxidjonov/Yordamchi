namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// Kelgan baytlar protokolga mos kelmaganda tashlanadi: noto'g'ri sarlavha belgisi (magic),
/// mumkin bo'lgandan katta yuk uzunligi yoki nazorat yig'indisi (CRC) mos kelmasligi.
/// <para>
/// Bu holatlar odatda tarmoqdagi buzilish yoki begona (protokolimizga aloqasiz) ulanishni
/// bildiradi — ikkalasida ham ulanishni yopish to'g'ri, chunki oqim endi ishonchsiz.
/// </para>
/// </summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message)
        : base(message)
    {
    }
}
