using System.Diagnostics;
using System.Runtime.Versioning;
using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Commands;
using Yordamchi.Agent.Input;
using Yordamchi.Agent.Net;
using Yordamchi.Agent.Ui;

namespace Yordamchi.Agent.Hosting;

/// <summary>
/// Agentning barcha qismlarini bir joyga yig'adi va ularni birga yuritadi: TCP server, UDP mayoq,
/// ekran manbasi, kirish/buyruq bajaruvchilari va tray belgisi.
/// <para>
/// <b>Nega alohida sinf.</b> Bu tarkib uch xil joydan bir xil ishga tushadi — konsoldan, Windows
/// xizmati ochgan bola jarayondan va sinovlardan. Ular orasidagi farq faqat "kim to'xtatishni
/// buyuradi" da; qolgan hammasi shu yerda, bitta nusxada.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentHost
{
    private readonly AgentOptions _options;
    private readonly AgentLog _log;

    public AgentHost(AgentOptions options, AgentLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Agentni bekor qilinguncha (yoki tray'dan "Chiqish" bosilguncha) yuritadi.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var machineName = Environment.MachineName;
        var permissions = new AgentPermissions(_options.AllowInput, _options.AllowCommands);

        // Tray'dan "Chiqish" ham, tashqi bekor qilish ham bir xil to'xtatishga olib kelishi kerak.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AgentServer? server = null;
        TrayIndicator? tray = null;

        if (_options.ShowTray)
        {
            tray = new TrayIndicator(
                $"Yordamchi agent — {machineName}:{_options.Port}",
                permissions,
                () => DescribeStatus(server),
                stopping.Cancel,
                _log.FilePath);

            tray.Show();
        }

        // Buyruq xabari tray bo'lsa yumshoq bildirishnoma sifatida chiqadi, bo'lmasa modal oyna.
        Action<string>? notify = tray is null
            ? null
            : text => tray!.Notify("Masofaviy xabar", text);

        var connectionOptions = new AgentConnectionOptions
        {
            Input = new GatedInputSink(new SendInputSink(), () => permissions.AllowInput),
            Commands = new GatedCommandSink(new WindowsCommandSink(notify, _log.Write), () => permissions.AllowCommands),
            Log = _log.Write,
            FrameInterval = _options.FrameInterval
        };

        server = new AgentServer(
            _options.Port,
            () => ScreenSourceFactory.Create(_options.Capture, _options.JpegQuality, _log.Write),
            _log.Write,
            connectionOptions);

        if (tray is not null)
        {
            // Ulanish haqida foydalanuvchi darhol xabardor bo'ladi — bu tizimning asosiy shartlaridan biri.
            server.ClientConnected += endpoint => tray!.Notify("Masofaviy ulanish", $"{endpoint} bu kompyuterga ulandi.");
            server.ClientDisconnected += endpoint => tray!.Notify("Ulanish tugadi", $"{endpoint} uzildi.");
        }

        _log.Write($"Agent ishga tushdi. Mashina: {machineName}, port: {_options.Port}, "
                   + $"boshqaruv: {(permissions.AllowInput ? "ruxsat" : "o'chirilgan")}, "
                   + $"buyruqlar: {(permissions.AllowCommands ? "ruxsat" : "o'chirilgan")}.");

        var tasks = new List<Task> { server.RunAsync(stopping.Token) };

        if (_options.Announce)
            tasks.Add(new DiscoveryAnnouncer(_options.Port, machineName).RunAsync(stopping.Token));

        if (_options.ParentProcessId is { } parentId)
            tasks.Add(WatchParentAsync(parentId, stopping));

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // To'xtatish so'raldi — odatiy chiqish.
        }
        finally
        {
            tray?.Dispose();
            _log.Write("Agent to'xtatildi.");
        }
    }

    private static string DescribeStatus(AgentServer? server)
    {
        var count = server?.ActiveConnections ?? 0;
        return count == 0 ? "Hozir hech kim ulanmagan" : $"Ulangan: {count}";
    }

    /// <summary>
    /// Ota jarayon (xizmat) tugasa, agent ham chiqadi.
    /// <para>
    /// Buning sababi: xizmat faol seansda bola jarayon ochadi. Agar xizmat to'xtatilsa yoki
    /// yiqilsa, bola jarayon "yetim" qolib, hech kim boshqarmaydigan tinglovchi bo'lib qolardi —
    /// bu esa foydalanuvchi "xizmatni o'chirdim" deb o'ylab yurgan holatda ham agent ishlashini
    /// bildiradi. Shu sababli bog'liqlik ataylab qattiq.
    /// </para>
    /// </summary>
    private async Task WatchParentAsync(int parentProcessId, CancellationTokenSource stopping)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync(stopping.Token).ConfigureAwait(false);

            _log.Write("Ota jarayon (xizmat) tugadi — agent ham to'xtaydi.");
            stopping.Cancel();
        }
        catch (ArgumentException)
        {
            // Bunday jarayon yo'q (allaqachon tugagan) — darhol to'xtaymiz.
            _log.Write("Ota jarayon topilmadi — agent to'xtaydi.");
            stopping.Cancel();
        }
        catch (OperationCanceledException)
        {
            // Agent boshqa sababdan to'xtadi.
        }
    }
}
