namespace Yordamchi.Models;

/// <summary>
/// Dastur taniydigan arxiv formatlari.
/// <para>
/// <b>O'qish</b> hammasi uchun ishlaydi, <b>yozish</b> esa faqat <see cref="Zip"/> uchun:
/// 7z va RAR ning yozuvchisi ochiq kutubxonalarda yo'q (RAR formati umuman yopiq), TAR/GZip
/// esa Windows foydalanuvchisiga deyarli kerak emas. Shuning uchun "Arxivlash" rejimi
/// har doim <c>.zip</c> chiqaradi.
/// </para>
/// </summary>
public enum ArchiveFormat
{
    Unknown,
    Zip,
    SevenZip,
    Rar,
    Tar,
    GZip,
    BZip2
}

/// <summary>
/// Siqish darajasi. Raqamlar Deflate ning 0..9 shkalasiga tushadi
/// (<see cref="Store"/> = 0 — umuman siqmaydi, faqat bitta faylga jamlaydi).
/// </summary>
public enum ArchiveCompressionLevel
{
    /// <summary>Siqishsiz — eng tez; allaqachon siqilgan fayllar (mp4, jpg, docx) uchun ma'quli.</summary>
    Store,

    /// <summary>Tez, lekin hajm kamroq kichrayadi.</summary>
    Fast,

    /// <summary>Tezlik va hajm o'rtasidagi muvozanat — standart tanlov.</summary>
    Normal,

    /// <summary>Eng kichik hajm, lekin sezilarli sekinroq.</summary>
    Maximum
}

/// <summary>
/// Parolli ZIP uchun shifrlash usuli.
/// <para>
/// Bu tanlov ataylab foydalanuvchiga ko'rsatiladi, chunki u sof "kuchli/kuchsiz" masalasi
/// emas — mos kelish (moslik) masalasi ham.
/// </para>
/// </summary>
public enum ZipEncryption
{
    /// <summary>
    /// WinZip AES-256 — zamonaviy va kuchli. Lekin Windows Explorer ning ichki ZIP ochuvchisi
    /// uni <b>tushunmaydi</b>: qabul qiluvchida 7-Zip, WinRAR yoki shunga o'xshash dastur
    /// bo'lishi kerak.
    /// </summary>
    Aes256,

    /// <summary>
    /// Eski ZipCrypto — deyarli hamma joyda, jumladan Windows Explorer da ochiladi, lekin
    /// kriptografik jihatdan zaif (ma'lum hujumlari bor). Faqat moslik muhim bo'lganda.
    /// </summary>
    ZipCrypto
}

/// <summary>Arxiv ichidagi bitta yozuv (fayl yoki papka).</summary>
/// <param name="Path">Arxiv ichidagi nisbiy yo'l, masalan <c>hujjatlar/shartnoma.pdf</c>.</param>
/// <param name="Size">Siqishdan oldingi hajm (bayt). Noma'lum bo'lsa 0.</param>
/// <param name="CompressedSize">Arxivdagi hajm (bayt). Noma'lum bo'lsa 0.</param>
/// <param name="Modified">O'zgartirilgan sana; ba'zi formatlarda bo'lmaydi.</param>
/// <param name="IsDirectory">Papka yozuvimi.</param>
/// <param name="IsEncrypted">Shu yozuv parol bilan shifrlanganmi.</param>
public sealed record ArchiveEntryInfo(
    string Path,
    long Size,
    long CompressedSize,
    DateTime? Modified,
    bool IsDirectory,
    bool IsEncrypted)
{
    /// <summary>Ro'yxatda ko'rsatiladigan qisqa nom (yo'lning oxirgi bo'lagi).</summary>
    public string Name
    {
        get
        {
            var trimmed = Path.TrimEnd('/', '\\');
            var slash = trimmed.LastIndexOfAny(['/', '\\']);
            return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        }
    }

    /// <summary>Siqish foizi (0..100). Manba hajmi noma'lum bo'lsa <c>null</c>.</summary>
    public int? SavedPercent => Size > 0 && CompressedSize > 0 && CompressedSize <= Size
        ? (int)(100 - CompressedSize * 100 / Size)
        : null;
}

/// <summary>Arxiv ochilmasdan oldin ko'rsatiladigan umumiy ma'lumot.</summary>
/// <param name="Format">Aniqlangan format.</param>
/// <param name="Entries">Ichidagi yozuvlar.</param>
/// <param name="TotalSize">Barcha fayllarning siqilmagan umumiy hajmi.</param>
/// <param name="IsEncrypted">Kamida bitta yozuv shifrlanganmi — UI parol maydonini shunda ochadi.</param>
public sealed record ArchiveInfo(
    ArchiveFormat Format,
    IReadOnlyList<ArchiveEntryInfo> Entries,
    long TotalSize,
    bool IsEncrypted)
{
    public int FileCount => Entries.Count(entry => !entry.IsDirectory);
}

/// <summary>"Arxivlash" rejimining sozlamalari.</summary>
public sealed class CreateArchiveOptions
{
    public ArchiveCompressionLevel Level { get; init; } = ArchiveCompressionLevel.Normal;

    /// <summary>Bo'sh yoki <c>null</c> bo'lsa arxiv shifrlanmaydi.</summary>
    public string? Password { get; init; }

    /// <summary>Parol berilganda qanday shifrlanadi.</summary>
    public ZipEncryption Encryption { get; init; } = ZipEncryption.Aes256;

    /// <summary>
    /// Papka qo'shilganda ichidagi papka tuzilishi saqlansinmi. <c>false</c> bo'lsa barcha
    /// fayllar arxiv ildiziga tekis yoziladi.
    /// </summary>
    public bool KeepFolderStructure { get; init; } = true;

    public static CreateArchiveOptions Default { get; } = new();

    /// <summary>Deflate ning 0..9 shkalasiga o'girish.</summary>
    public int DeflateLevel => Level switch
    {
        ArchiveCompressionLevel.Store => 0,
        ArchiveCompressionLevel.Fast => 3,
        ArchiveCompressionLevel.Maximum => 9,
        _ => 6
    };
}
