namespace Yordamchi.Remoting.Command;

/// <summary>
/// Masterdan agentga yuborilishi mumkin bo'lgan buyruqlar. Ro'yxat <b>ataylab qisqa va
/// yopiq</b>: bu yerda ixtiyoriy dastur ishga tushirish yoki qobiq (shell) buyrug'i yo'q va
/// bo'lmaydi ham — masofadan istalgan buyruqni bajarish eng xavfli imkoniyat. Har bir yangi
/// buyruq alohida, cheklangan amal sifatida qo'shiladi.
/// <para>
/// Qiymatlar aniq raqamlangan: ikkala tomon alohida yig'ilgani uchun tartib o'zgarsa eski agent
/// yangi masterni noto'g'ri tushunardi.
/// </para>
/// </summary>
public enum RemoteCommandKind : byte
{
    /// <summary>Noma'lum / yaroqsiz — hech qachon yuborilmaydi va bajarilmaydi.</summary>
    None = 0,

    /// <summary>Agent kompyuterida foydalanuvchiga qisqa xabar ko'rsatish.</summary>
    ShowMessage = 1,

    /// <summary>Ish stolini qulflash (foydalanuvchi seansi yopilmaydi, faqat qulflanadi).</summary>
    LockScreen = 2
}

/// <summary>
/// Bitta cheklangan buyruq: turi va (kerak bo'lsa) matni.
/// </summary>
public readonly struct RemoteCommand
{
    /// <summary>
    /// Xabar matnining eng katta uzunligi. Chegara bor, chunki ekranga chiqadigan matnni
    /// cheksiz uzatish — foydalanuvchini bosib qo'yish (spam) yo'li.
    /// </summary>
    public const int MaxTextLength = 300;

    private RemoteCommand(RemoteCommandKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public RemoteCommandKind Kind { get; }

    /// <summary>Xabar matni; matn talab qilmaydigan buyruqlarda bo'sh satr.</summary>
    public string Text { get; }

    /// <summary>Foydalanuvchiga ko'rsatiladigan xabar. Uzun matn qisqartiriladi.</summary>
    public static RemoteCommand ShowMessage(string text)
    {
        text ??= string.Empty;
        text = text.Trim();

        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        return new RemoteCommand(RemoteCommandKind.ShowMessage, text);
    }

    /// <summary>Ish stolini qulflash.</summary>
    public static RemoteCommand LockScreen() => new(RemoteCommandKind.LockScreen, string.Empty);
}
