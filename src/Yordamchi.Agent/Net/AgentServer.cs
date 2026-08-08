using System.Net;
using System.Net.Sockets;
using Yordamchi.Agent.Capture;

namespace Yordamchi.Agent.Net;

/// <summary>
/// Master ulanishlarini TCP orqali qabul qiladi. Har bir ulanish uchun alohida
/// <see cref="AgentConnection"/> ochiladi va u mustaqil ishlaydi — bitta master uzilsa,
/// boshqalari ta'sirlanmaydi.
/// </summary>
public sealed class AgentServer
{
    private readonly int _port;
    private readonly Func<IScreenSource> _screenSourceFactory;
    private readonly Action<string>? _log;

    /// <param name="port">TCP boshqaruv porti.</param>
    /// <param name="screenSourceFactory">Har ulanishga alohida ekran manbasi.</param>
    /// <param name="log">Ixtiyoriy jurnal (console yoki Windows Event Log).</param>
    public AgentServer(int port, Func<IScreenSource> screenSourceFactory, Action<string>? log = null)
    {
        _port = port;
        _screenSourceFactory = screenSourceFactory ?? throw new ArgumentNullException(nameof(screenSourceFactory));
        _log = log;
    }

    /// <summary>Bekor qilinguncha ulanishlarni qabul qiladi.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        _log?.Invoke($"Agent {_port}-portda ulanish kutmoqda.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // To'xtatish so'raldi — odatiy.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "noma'lum";
        _log?.Invoke($"Ulanish: {endpoint}");

        using (client)
        using (var screen = _screenSourceFactory())
        {
            try
            {
                await using var stream = client.GetStream();
                await new AgentConnection(stream, screen).RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bitta buzuq yoki begona ulanish butun agentni yiqitmasligi kerak.
                _log?.Invoke($"Ulanish xatosi ({endpoint}): {ex.Message}");
            }
            finally
            {
                _log?.Invoke($"Uzildi: {endpoint}");
            }
        }
    }
}
