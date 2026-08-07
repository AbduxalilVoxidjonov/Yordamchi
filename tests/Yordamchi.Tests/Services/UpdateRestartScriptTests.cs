using System.Diagnostics;
using System.IO;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="UpdateService.BuildRestartScript"/> chiqargan skriptni <b>haqiqatan ishga
/// tushiradigan</b> sinov.
/// <para>
/// Boshqa sinovlar skript matnini o'qiydi — ular "kerakli so'zlar bor" deyishdan nariga
/// o'tmaydi. Bu yerdagi savol boshqacha: <c>tasklist</c> filtri, <c>find</c> ning
/// <c>errorlevel</c> i va <c>start /wait</c> navbati cmd.exe da <b>amalda</b> shu tartibda
/// ishlaydimi. Bu mantiq noto'g'ri bo'lsa, o'rnatgich band fayl ustiga yozishga urinadi yoki
/// dastur qayta ochilmaydi — ikkalasi ham faqat foydalanuvchida ko'rinadi.
/// </para>
/// <para>
/// Haqiqiy o'rnatgich va haqiqiy <c>Yordamchi.exe</c> ishga tushirilmaydi: skriptga ularning
/// o'rniga vaqtinchalik papkadagi ikkita zararsiz <c>.cmd</c> beriladi — har biri jurnalga
/// bitta qator yozadi, boshqa hech narsa qilmaydi. Kutiladigan "dastur" esa oddiy
/// <c>cmd.exe</c> jarayoni.
/// </para>
/// </summary>
public sealed class UpdateRestartScriptTests : IDisposable
{
    private readonly TempWorkspace _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_script_waits_for_the_process_then_installs_then_reopens_the_app()
    {
        var log = _temp.At("navbat.txt");

        // "O'rnatgich" o'ziga berilgan bayroqlarni ham yozadi — shunda /passive /norestart
        // haqiqatan yetib borgani ko'rinadi, nafaqat skript matnida turgani.
        var installer = WriteCmd("soxta-ornatgich.cmd", $"echo ornatgich %*>>\"{log}\"");
        var application = WriteCmd("soxta-dastur.cmd", $"echo dastur>>\"{log}\"");

        // Kutilishi kerak bo'lgan jarayon: bir necha soniya yashaydi va chiqishdan oldin
        // jurnalga yozadi. Agar skript kutmasa, "ornatgich" qatori shundan oldin turadi.
        var victim = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c ping -n 4 127.0.0.1 >nul & echo yopildi>>\"{log}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            var scriptPath = _temp.WriteFile(
                "yordamchi-update.cmd",
                UpdateService.BuildRestartScript(victim.Id, installer, application));

            // Dasturdagi bilan aynan bir xil ishga tushirish: qobiq (shell) orqali, yashirin
            // oyna bilan. Bu shunchaki o'xshatish emas — CreateNoWindow bilan ochilgan
            // jarayonda konsol umuman bo'lmaydi, skript ichidagi "tasklist" esa hech narsa
            // chiqarmaydi va kutish halqasi darhol tugab qolardi.
            using var runner = Process.Start(new ProcessStartInfo(scriptPath)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = _temp.Root
            })!;

            Assert.True(runner.WaitForExit(60_000), "Yangilash skripti tugamadi.");

            // Oxirgi qadam "start" bilan kutilmasdan chaqiriladi, shuning uchun skript
            // tugagandan keyin ham dastur bir necha o'nlab millisoniyada yozib ulguradi.
            var lines = WaitForLines(log, 3, TimeSpan.FromSeconds(30));

            Assert.Equal(
                ["yopildi", "ornatgich /passive /norestart", "dastur"],
                lines);
        }
        finally
        {
            KillIfAlive(victim);
        }
    }

    [Fact]
    public void The_script_does_not_run_the_installer_while_the_process_is_alive()
    {
        var log = _temp.At("navbat.txt");
        var installer = WriteCmd("soxta-ornatgich.cmd", $"echo ornatgich>>\"{log}\"");
        var application = WriteCmd("soxta-dastur.cmd", $"echo dastur>>\"{log}\"");

        // Bu jarayon o'zi chiqib ketmaydi — uni sinovning o'zi to'xtatadi.
        var victim = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c ping -n 60 127.0.0.1 >nul",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        try
        {
            var scriptPath = _temp.WriteFile(
                "yordamchi-update.cmd",
                UpdateService.BuildRestartScript(victim.Id, installer, application));

            // Dasturdagi bilan aynan bir xil ishga tushirish: qobiq (shell) orqali, yashirin
            // oyna bilan. Bu shunchaki o'xshatish emas — CreateNoWindow bilan ochilgan
            // jarayonda konsol umuman bo'lmaydi, skript ichidagi "tasklist" esa hech narsa
            // chiqarmaydi va kutish halqasi darhol tugab qolardi.
            using var runner = Process.Start(new ProcessStartInfo(scriptPath)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = _temp.Root
            })!;

            // Jarayon tirik ekan, skript kutish halqasida turishi kerak.
            Assert.False(runner.WaitForExit(5_000), "Skript jarayon tirik ekan kutmadi.");
            Assert.Empty(ReadLines(log));

            KillIfAlive(victim);

            Assert.True(runner.WaitForExit(60_000), "Jarayon o'lgandan keyin skript davom etmadi.");
            Assert.Equal(["ornatgich", "dastur"], WaitForLines(log, 2, TimeSpan.FromSeconds(30)));
        }
        finally
        {
            KillIfAlive(victim);
        }
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>
    /// Bitta qator bajaradigan zararsiz <c>.cmd</c> yaratadi.
    /// <para>
    /// Oxiridagi <c>exit</c> majburiy: <c>start</c> paketli faylni <c>cmd /k</c> bilan ochadi
    /// ("start /?" da yozilgan), ya'ni buyruq tugagach ham oyna ochiq qoladi va
    /// <c>start /wait</c> abadiy kutib turadi. Haqiqiy o'rnatgich <c>.exe</c> bo'lgani uchun
    /// dasturda bunday muammo yo'q — bu faqat soxta o'rnatgichga tegishli.
    /// </para>
    /// </summary>
    private string WriteCmd(string name, string body) =>
        _temp.WriteFile(name, $"@echo off\r\n{body}\r\nexit\r\n");

    private static void KillIfAlive(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            process.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Jarayon allaqachon tugagan — sinov natijasiga aloqasi yo'q.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>Jurnalda kutilgan sondagi qator paydo bo'lguncha kutadi.</summary>
    private static string[] WaitForLines(string path, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lines = ReadLines(path);

        while (lines.Length < expected && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
            lines = ReadLines(path);
        }

        return lines;
    }

    /// <summary>
    /// Jurnalni o'qiydi. Boshqa jarayon ayni damda yozayotgan bo'lishi mumkin, shuning uchun
    /// fayl to'liq bo'lishish (<see cref="FileShare.ReadWrite"/>) bilan ochiladi.
    /// </summary>
    private static string[] ReadLines(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
