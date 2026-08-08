using System.Runtime.Versioning;
using System.ServiceProcess;
using Yordamchi.Agent.Hosting;
using Yordamchi.Agent.Service;

namespace Yordamchi.Agent;

/// <summary>
/// Agentning kirish nuqtasi. Bitta fayl to'rt xil ishga tushishni ajratadi:
/// <list type="bullet">
///   <item>oddiy jarayon (konsol + tray belgisi) — sinash va qo'lda ishlatish uchun;</item>
///   <item><c>--install</c> / <c>--uninstall</c> — Windows xizmatini o'rnatish/olib tashlash;</item>
///   <item><c>--service</c> — xizmat menejeri chaqiradigan rejim.</item>
/// </list>
/// <para>
/// Ishning o'zi <see cref="AgentHost"/> da: bu yerda faqat "qaysi rejim" tanlanadi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = AgentOptions.Parse(args);

        if (options.Mode == AgentRunMode.Help)
            return ShowHelp(options);

        // Xizmat rejimida konsol yo'q — jurnal faqat faylga yoziladi.
        var log = AgentLog.Create(hasConsole: options.Mode != AgentRunMode.Service);

        switch (options.Mode)
        {
            case AgentRunMode.Install:
                return ServiceControl.Install(options, log);

            case AgentRunMode.Uninstall:
                return ServiceControl.Uninstall(log);

            case AgentRunMode.Service:
                // Bu chaqiruv xizmat menejeri jarayonni o'zi ochganda qaytadi. Buyruq satridan
                // "--service" bilan ishga tushirilsa Windows xato beradi — bu kutilgan holat.
                ServiceBase.Run(new AgentServiceHost(options, log));
                return 0;

            default:
                return await RunInteractiveAsync(options, log).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunInteractiveAsync(AgentOptions options, AgentLog log)
    {
        PrintBanner(options);

        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            // Ctrl+C jarayonni birdan o'ldirmasin — tartibli to'xtatamiz.
            eventArgs.Cancel = true;
            stopping.Cancel();
        };

        try
        {
            await new AgentHost(options, log).RunAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — odatiy chiqish.
        }

        return 0;
    }

    /// <summary>
    /// Ishga tushganda ekranga chiqadigan matn. U ham <b>ko'rinadigan belgi</b> vazifasini
    /// bajaradi: kompyuterda o'tirgan odam nima ishlayotganini o'qib ko'radi.
    /// </summary>
    private static void PrintBanner(AgentOptions options)
    {
        Console.WriteLine("======================================================");
        Console.WriteLine(" Yordamchi Agent");
        Console.WriteLine(" Bu kompyuter masofadan kuzatilishi mumkin.");
        Console.WriteLine($" Mashina: {Environment.MachineName}   Port: {options.Port}");
        Console.WriteLine($" Boshqaruv: {(options.AllowInput ? "ruxsat" : "o'chirilgan")}"
                          + $"   Buyruqlar: {(options.AllowCommands ? "ruxsat" : "o'chirilgan")}");
        Console.WriteLine(" To'xtatish uchun Ctrl+C bosing.");
        Console.WriteLine("======================================================");
    }

    private static int ShowHelp(AgentOptions options)
    {
        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            Console.Error.WriteLine();
            Console.WriteLine(AgentOptions.HelpText);
            return 2;
        }

        Console.WriteLine(AgentOptions.HelpText);
        return 0;
    }
}
