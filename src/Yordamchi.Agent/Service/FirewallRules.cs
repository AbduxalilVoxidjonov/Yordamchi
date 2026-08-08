using System.Diagnostics;
using System.Runtime.Versioning;

namespace Yordamchi.Agent.Service;

/// <summary>
/// Agent uchun Windows brandmauerida kiruvchi qoida yaratadi va olib tashlaydi.
/// <para>
/// <b>Nega kerak.</b> Brandmauer standart holatda kiruvchi ulanishlarni to'sadi. Qoida
/// bo'lmasa, agent ishlab turgan bo'lsa ham master unga ulana olmaydi yoki har ishga tushirishda
/// foydalanuvchiga "ruxsat berasizmi?" oynasi chiqadi — o'nlab kompyuterda buni qo'lda bosib
/// chiqishning ma'nosi yo'q.
/// </para>
/// <para>
/// <b>Faqat bitta kiruvchi qoida.</b> UDP mayoq <b>chiqadigan</b> paket (broadcast), u standart
/// sozlamada ruxsat etilgan — shuning uchun qoida faqat boshqaruv (TCP) porti uchun ochiladi va
/// u ham <b>aynan shu dastur fayliga</b> bog'lanadi: port boshqa jarayonlar uchun ochilib
/// qolmasin.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirewallRules
{
    /// <summary>Qoidaning nomi — olib tashlashda ham shu nom bo'yicha topiladi.</summary>
    private const string RuleName = "Yordamchi Agent (kiruvchi TCP)";

    /// <summary>Qoidani yaratadi (avvalgi bir xil nomdagisini almashtirib).</summary>
    public static void Add(int port, Action<string> log)
    {
        var executable = Environment.ProcessPath;

        if (executable is null)
        {
            log("Brandmauer qoidasi qo'shilmadi: dastur fayli yo'li aniqlanmadi.");
            return;
        }

        // Avval eski qoidani olib tashlaymiz: port yoki fayl yo'li o'zgargan bo'lsa, ikkita
        // qoida qolib ketmasligi kerak.
        Netsh(log, quiet: true, "advfirewall", "firewall", "delete", "rule", $"name={RuleName}");

        var added = Netsh(
            log,
            quiet: false,
            "advfirewall", "firewall", "add", "rule",
            $"name={RuleName}",
            "dir=in",
            "action=allow",
            "protocol=TCP",
            $"localport={port}",
            $"program={executable}",
            "profile=any",
            "description=Yordamchi masofaviy boshqaruv agentiga kiruvchi ulanishlar.");

        log(added == 0
            ? $"Brandmauer qoidasi qo'shildi ({port}-port)."
            : "Brandmauer qoidasi qo'shilmadi — masterdan ulanish uchun uni qo'lda ochish kerak bo'lishi mumkin.");
    }

    /// <summary>Qoidani olib tashlaydi. Qoida bo'lmasa — bu xato emas.</summary>
    public static void Remove(Action<string> log)
    {
        Netsh(log, quiet: true, "advfirewall", "firewall", "delete", "rule", $"name={RuleName}");
        log("Brandmauer qoidasi olib tashlandi.");
    }

    /// <summary>
    /// <c>netsh</c> ni chaqiradi. Argumentlar alohida beriladi — <c>name=</c> qiymatida bo'shliq
    /// bor va uni .NET o'zi to'g'ri qavslaydi.
    /// </summary>
    private static int Netsh(Action<string> log, bool quiet, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("netsh.exe")
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
                return 1;

            var error = process.StandardError.ReadToEnd().Trim();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (!quiet && error.Length > 0)
                log(error);

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!quiet)
                log($"netsh chaqirilmadi: {ex.Message}");

            return 1;
        }
    }
}
