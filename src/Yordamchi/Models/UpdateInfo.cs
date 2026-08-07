namespace Yordamchi.Models;

/// <summary>
/// Relizdan olingan, foydalanuvchiga e'lon qilinadigan yangilanish haqidagi ma'lumot.
/// <para>
/// Bu tur faqat <b>joriy versiyadan yangi</b> va <b>tekshiruvdan o'tgan</b> reliz uchun
/// yaratiladi: nomi kutilgan naqshga mos aktiv va ishonchli xostdagi https havola.
/// Shu sababli UI qatlami hech qanday qo'shimcha tekshiruvsiz uni ko'rsatishi mumkin.
/// Dastur o'zi hech narsa yuklab olmaydi — o'rnatgichni foydalanuvchi brauzerda oladi.
/// </para>
/// </summary>
/// <param name="Version">Teg (tag) dan olingan versiya, masalan <c>2.2.0</c>.</param>
/// <param name="TagName">Relizning asl tegi (<c>v2.2.0</c> yoki <c>2.2.0</c>).</param>
/// <param name="ReleaseName">Reliz sarlavhasi; bo'sh bo'lishi mumkin.</param>
/// <param name="ReleaseNotes">Reliz izohi (markdown matni); bo'sh bo'lishi mumkin.</param>
/// <param name="DownloadUrl">O'rnatgichning to'g'ridan-to'g'ri yuklab olish havolasi.</param>
/// <param name="AssetName">Aktiv fayl nomi, masalan <c>YordamchiSetup-2.2.0.exe</c>.</param>
/// <param name="SizeBytes">API aytgan hajm — foydalanuvchi nima yuklashini oldindan biladi.</param>
/// <param name="PublishedAt">Reliz e'lon qilingan vaqt.</param>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    string ReleaseNotes,
    string DownloadUrl,
    string AssetName,
    long SizeBytes,
    DateTimeOffset PublishedAt)
{
    /// <summary>UI da ko'rsatiladigan qisqa versiya matni (<c>2.2.0</c>).</summary>
    public string VersionText => Version.ToString(3);

    /// <summary>Reliz sarlavhasi bo'sh bo'lsa teg nomiga qaytadi — kartochka hech qachon bo'sh qolmaydi.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(ReleaseName) ? TagName : ReleaseName;

    /// <summary>Kartochkada ko'rsatiladigan hajm (Explorer bilan bir xil o'lchov — MiB).</summary>
    public string SizeText => SizeBytes <= 0
        ? "hajmi noma'lum"
        : $"{SizeBytes / (1024d * 1024d):0.#} MB";
}
