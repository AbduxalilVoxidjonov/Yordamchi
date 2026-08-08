namespace Yordamchi.Remoting.Protocol;

/// <summary>
/// Master (Yordamchi) va agent orasidagi bitta paketning turi. Qiymatlar bir baytga
/// sig'adi va <b>ataylab aniq raqamlangan</b>: protokol ikkala tomonda alohida yig'ilgani
/// uchun tartib o'zgarsa eski agent yangi masterni noto'g'ri tushunardi. Yangi turlar
/// faqat oxiriga qo'shiladi, mavjudlari qayta raqamlanmaydi.
/// </summary>
public enum PacketType : byte
{
    /// <summary>Noma'lum / yaroqsiz — nol qiymat hech qachon yuborilmaydi.</summary>
    None = 0,

    // ----- Ulanish va tiriklik -----

    /// <summary>Ulanish boshida ochiq kalitlar va identifikatsiya almashinadi.</summary>
    Handshake = 1,

    /// <summary>Handshake'ga javob: sessiya kaliti (ochiq kalit bilan o'ralgan).</summary>
    HandshakeAck = 2,

    /// <summary>Aloqa tirikligini tekshirish (keepalive).</summary>
    Ping = 3,

    /// <summary><see cref="Ping"/> ga javob.</summary>
    Pong = 4,

    /// <summary>Ulanish tartibli yopilmoqda.</summary>
    Disconnect = 5,

    // ----- Ekran (agent -> master) -----

    /// <summary>Master ekran uzatishni boshlash/to'xtatish yoki sifatni o'zgartirishni so'raydi.</summary>
    ScreenRequest = 10,

    /// <summary>Agent yuboradigan bitta ekran kadri (yoki uning o'zgargan qismi).</summary>
    ScreenFrame = 11,

    // ----- Boshqaruv (master -> agent) -----

    /// <summary>Sichqoncha yoki klaviatura hodisasi.</summary>
    InputEvent = 20,

    /// <summary>Buyruq: xabar ko'rsatish, dastur ochish va hokazo.</summary>
    Command = 21,

    /// <summary>Fayl uzatish (masterdan agentga).</summary>
    FileChunk = 22,

    // ----- Xizmatchi -----

    /// <summary>Xato haqida xabar; qabul qilingan paket rad etilganda ham yuboriladi.</summary>
    Error = 250
}
