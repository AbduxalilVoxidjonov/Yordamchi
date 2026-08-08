using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Net;

namespace Yordamchi.Agent;

/// <summary>
/// Agentning kirish nuqtasi. Hozircha oddiy konsol ilovasi: ochiq konsol oynasi
/// foydalanuvchi uchun "bu kompyuter kuzatilishi mumkin" degan ko'rinadigan belgi vazifasini
/// bajaradi. Keyingi bosqichda bu Windows xizmatiga va tray belgisiga aylantiriladi.
/// </summary>
internal static class Program
{
    /// <summary>Boshqaruv (TCP) porti. Discovery porti (5405) dan farqli.</summary>
    private const int ControlPort = 5406;

    private static async Task<int> Main(string[] args)
    {
        var port = ParsePort(args) ?? ControlPort;
        var machineName = Environment.MachineName;

        Console.WriteLine("======================================================");
        Console.WriteLine(" Yordamchi Agent");
        Console.WriteLine(" Bu kompyuter masofadan kuzatilishi mumkin.");
        Console.WriteLine($" Mashina: {machineName}   Port: {port}");
        Console.WriteLine(" To'xtatish uchun Ctrl+C bosing.");
        Console.WriteLine("======================================================");

        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            // Ctrl+C jarayonni birdan o'ldirmasin — tartibli to'xtatamiz.
            eventArgs.Cancel = true;
            stopping.Cancel();
        };

        var server = new AgentServer(port, CreateScreenSource, Console.WriteLine);
        var announcer = new DiscoveryAnnouncer(port, machineName);

        try
        {
            await Task.WhenAll(
                server.RunAsync(stopping.Token),
                announcer.RunAsync(stopping.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — odatiy chiqish.
        }

        Console.WriteLine("Agent to'xtatildi.");
        return 0;
    }

    /// <summary>
    /// Haqiqiy ekran manbasini beradi; olib bo'lmasa (masalan seans yo'q, CI) sintetik
    /// manbaga tushadi — shunda ulanish quvuri baribir ishlaydi.
    /// </summary>
    private static IScreenSource CreateScreenSource()
    {
        try
        {
            var source = new GdiScreenSource();
            _ = source.Capture(); // ishga tushirishda bir marta sinab ko'ramiz
            return source;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Haqiqiy ekran olinmadi ({ex.Message}) — sintetik manbaga o'tildi.");
            return new SyntheticScreenSource();
        }
    }

    private static int? ParsePort(string[] args)
    {
        // Oddiy: "--port 5406" yoki "-p 5406".
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--port" or "-p"
                && int.TryParse(args[i + 1], out var port)
                && port is > 0 and <= 65535)
            {
                return port;
            }
        }

        return null;
    }
}
