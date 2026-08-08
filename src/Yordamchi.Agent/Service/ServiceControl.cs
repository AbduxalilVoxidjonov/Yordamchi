using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Yordamchi.Agent.Hosting;

namespace Yordamchi.Agent.Service;

/// <summary>
/// Xizmatni ro'yxatga qo'shadi va olib tashlaydi.
/// <para>
/// <b>Nega <c>sc.exe</c>.</b> Xizmatni ro'yxatga qo'shish uchun .NET da tayyor API yo'q
/// (<c>ServiceController</c> faqat boshqaradi, yaratmaydi), qolgan yo'l esa <c>advapi32</c> ning
/// <c>CreateService</c> funksiyasini qo'lda chaqirish. <c>sc.exe</c> Windows tarkibida, ayni shu
/// vazifa uchun mo'ljallangan va uning natijasi jurnalga tushadigan tushunarli matn beradi —
/// o'rnatish bir martalik amal bo'lgani uchun bu yerda soddalik ustun turadi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class ServiceControl
{
    /// <summary>Xizmatning tizimdagi nomi.</summary>
    public const string ServiceName = "YordamchiAgent";

    /// <summary>Xizmatlar oynasida ko'rinadigan nom.</summary>
    private const string DisplayName = "Yordamchi Agent (masofaviy boshqaruv)";

    private const string Description =
        "Yordamchi dasturi uchun masofaviy boshqaruv agenti. Faol foydalanuvchi seansida "
        + "ekranni uzatadi va ruxsat berilgan bo'lsa boshqaruvni qabul qiladi. "
        + "Agent ishlayotgani tizim majmuasidagi belgidan ko'rinadi.";

    /// <summary>
    /// Xizmatni yaratadi va ishga tushiradi. Sozlamalar xizmatning buyruq satriga yoziladi, ya'ni
    /// o'rnatishda tanlangan port va ruxsatlar keyin ham amal qiladi.
    /// </summary>
    /// <returns>Chiqish kodi: 0 — muvaffaqiyat.</returns>
    public static int Install(AgentOptions options, AgentLog log)
    {
        if (!IsAdministrator())
        {
            log.Write("Xizmatni o'rnatish uchun administrator huquqi kerak. "
                      + "Buyruq satrini \"Administrator sifatida ishga tushirish\" bilan oching.");
            return 5; // ERROR_ACCESS_DENIED
        }

        var executable = Environment.ProcessPath;

        if (executable is null)
        {
            log.Write("Agent fayli yo'li aniqlanmadi — o'rnatish bekor qilindi.");
            return 1;
        }

        var binPath = $"\"{executable}\" {options.ToArgumentString(AgentRunMode.Service)}";

        // Avvalgi nusxa qolgan bo'lsa, uni olib tashlaymiz: aks holda "xizmat allaqachon bor"
        // degan xato chiqadi va yangi sozlamalar amal qilmaydi.
        if (Exists(log))
        {
            log.Write("Avvalgi xizmat topildi — u olib tashlanadi.");
            Uninstall(log, quiet: true);
        }

        if (Sc(log, "create", ServiceName, "binPath=", binPath, "start=", "auto", "DisplayName=", DisplayName) != 0)
        {
            log.Write("Xizmat yaratilmadi.");
            return 1;
        }

        // Tavsif va nosozlikdan keyin qayta ishga tushish — ikkisi ham majburiy emas, shuning
        // uchun xatosi o'rnatishni bekor qilmaydi.
        Sc(log, "description", ServiceName, Description);
        Sc(log, "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/10000/restart/30000");

        // Brandmauer qoidasi xizmatdan alohida: qoida bo'lmasa xizmat ishlaydi, lekin masterdan
        // ulanish o'tmaydi — shuning uchun u ham o'rnatishning bir qismi.
        FirewallRules.Add(options.Port, log.Write);

        if (Sc(log, "start", ServiceName) != 0)
        {
            log.Write("Xizmat yaratildi, lekin ishga tushmadi. Jurnalni tekshiring.");
            return 1;
        }

        log.Write($"Xizmat o'rnatildi va ishga tushdi: {ServiceName} (port {options.Port}).");
        return 0;
    }

    /// <summary>Xizmatni to'xtatadi va ro'yxatdan olib tashlaydi.</summary>
    /// <returns>Chiqish kodi: 0 — muvaffaqiyat.</returns>
    public static int Uninstall(AgentLog log, bool quiet = false)
    {
        if (!IsAdministrator())
        {
            log.Write("Xizmatni olib tashlash uchun administrator huquqi kerak.");
            return 5;
        }

        // To'xtatish xatosi ahamiyatsiz: xizmat allaqachon to'xtagan bo'lishi mumkin.
        Sc(log, "stop", ServiceName);

        var result = Sc(log, "delete", ServiceName);

        if (!quiet)
        {
            FirewallRules.Remove(log.Write);

            log.Write(result == 0
                ? $"Xizmat olib tashlandi: {ServiceName}."
                : "Xizmatni olib tashlab bo'lmadi.");
        }

        return result;
    }

    private static bool Exists(AgentLog log) => Sc(log, quiet: true, "query", ServiceName) == 0;

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Sc(AgentLog log, params string[] arguments) => Sc(log, quiet: false, arguments);

    /// <summary>
    /// <c>sc.exe</c> ni chaqiradi va uning chiqishini jurnalga yozadi. Har bir argument alohida
    /// beriladi — shunda bo'shliqli yo'llar .NET tomonidan to'g'ri qavslanadi va qo'lda qavs
    /// qo'yish bilan bog'liq xatolar bo'lmaydi.
    /// </summary>
    private static int Sc(AgentLog log, bool quiet, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                log.Write("sc.exe ishga tushmadi.");
                return 1;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();

            if (!quiet && output.Length > 0)
                log.Write(output);

            if (!quiet && error.Length > 0)
                log.Write(error);

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Write($"sc.exe chaqirilmadi: {ex.Message}");
            return 1;
        }
    }
}
