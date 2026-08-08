using System.IO;
using System.Text;

namespace Yordamchi.Agent.Hosting;

/// <summary>
/// Agentning jurnali: xabarni konsolga (bo'lsa) va faylga yozadi.
/// <para>
/// <b>Nega fayl kerak.</b> Xizmat rejimida konsol yo'q — nosozlikni tekshirishning boshqa yo'li
/// qolmaydi. Jurnal shaffoflik uchun ham muhim: kim ulandi, qachon boshqaruv ishlatildi — bularning
/// hammasi kompyuter egasi ko'ra oladigan joyda yozilib turadi.
/// </para>
/// <para>
/// Yozuv <b>hech qachon istisno tashlamaydi</b>: jurnalga yozib bo'lmagani (disk to'la, huquq yo'q)
/// agentni to'xtatish uchun sabab emas.
/// </para>
/// </summary>
public sealed class AgentLog
{
    /// <summary>Fayl shu o'lchamdan oshsa, u <c>.old</c> ga ko'chiriladi va yangisi boshlanadi.</summary>
    private const long MaxFileBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly bool _hasConsole;

    private AgentLog(string? filePath, bool hasConsole)
    {
        FilePath = filePath;
        _hasConsole = hasConsole;
    }

    /// <summary>Jurnal fayli yo'li; yozib bo'lmasa <c>null</c>.</summary>
    public string? FilePath { get; }

    /// <summary>
    /// Jurnalni ochadi. Avval <c>%ProgramData%</c> ga urinadi (xizmat va foydalanuvchi bir joyga
    /// yozsin), yozib bo'lmasa foydalanuvchining <c>%LocalAppData%</c> papkasiga tushadi — oddiy
    /// huquq bilan ishlayotgan jarayon <c>ProgramData</c> ga yoza olmasligi mumkin.
    /// </summary>
    public static AgentLog Create(bool hasConsole = true)
    {
        foreach (var folder in CandidateFolders())
        {
            var path = TryPrepare(folder);
            if (path is not null)
                return new AgentLog(path, hasConsole);
        }

        return new AgentLog(null, hasConsole);
    }

    /// <summary>Bitta satr yozadi (vaqt belgisi bilan).</summary>
    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";

        if (_hasConsole)
        {
            try
            {
                Console.WriteLine(line);
            }
            catch (IOException)
            {
                // Konsol yopilgan (masalan jarayon xizmat sifatida ishlayapti) — muhim emas.
            }
        }

        if (FilePath is null)
            return;

        lock (_gate)
        {
            try
            {
                RollIfTooLarge();
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Jurnalga yozilmadi — agent ishini davom ettiradi.
            }
        }
    }

    private void RollIfTooLarge()
    {
        var file = new FileInfo(FilePath!);

        if (!file.Exists || file.Length < MaxFileBytes)
            return;

        var archive = FilePath + ".old";

        if (File.Exists(archive))
            File.Delete(archive);

        File.Move(FilePath!, archive);
    }

    private static IEnumerable<string> CandidateFolders()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Yordamchi", "Agent");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yordamchi", "Agent");
    }

    /// <summary>Papkani yaratib, unga yozib ko'radi — huquqni faqat shu yo'l bilan bilib olish mumkin.</summary>
    private static string? TryPrepare(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "agent.log");
            File.AppendAllText(path, string.Empty);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
