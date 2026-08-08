using System.Net.Sockets;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Remoting.Master;

/// <summary>Masterga yetib kelgan bitta ekran kadri.</summary>
/// <param name="Width">Kenglik.</param>
/// <param name="Height">Balandlik.</param>
/// <param name="Format">Rasm baytlari kodlash turi.</param>
/// <param name="Image">Rasm baytlari.</param>
public sealed record RemoteFrame(int Width, int Height, ScreenImageFormat Format, byte[] Image);

/// <summary>
/// Master tomonidagi bitta agent ulanishi: ulanadi, handshake qiladi va fon halqasida
/// kelgan paketlarni o'qib, ekran kadrlarini <see cref="FrameReceived"/> orqali chiqaradi.
/// <para>
/// Bu qatlam UI'ni bilmaydi — hodisalar istalgan oqimdan chaqirilishi mumkin, UI'ga o'tkazishni
/// (Dispatcher) yuqori kod bajaradi. Shu tufayli u WPF'siz, loopback ustida sinaladi.
/// </para>
/// </summary>
public sealed class MasterSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly byte[] _sessionKey;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _loopCts = new();
    private Task? _receiveLoop;

    private MasterSession(TcpClient client, Stream stream, byte[] sessionKey)
    {
        _client = client;
        _stream = stream;
        _sessionKey = sessionKey;
    }

    /// <summary>Ekran kadri kelganda.</summary>
    public event Action<RemoteFrame>? FrameReceived;

    /// <summary>Ulanish uzilganda (xato yoki tomon yopganda).</summary>
    public event Action? Disconnected;

    /// <summary>Agentga ulanadi va handshake'ni bajaradi.</summary>
    public static async Task<MasterSession> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            var sessionKey = await RemoteHandshake.PerformAsMasterAsync(stream, cancellationToken).ConfigureAwait(false);

            var session = new MasterSession(client, stream, sessionKey);
            session._receiveLoop = session.ReceiveLoopAsync(session._loopCts.Token);
            return session;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Ekran uzatishni boshlashni so'raydi.</summary>
    public Task StartScreenAsync(CancellationToken cancellationToken = default) =>
        SendAsync(PacketType.ScreenRequest, [1], cancellationToken);

    /// <summary>Ekran uzatishni to'xtatishni so'raydi.</summary>
    public Task StopScreenAsync(CancellationToken cancellationToken = default) =>
        SendAsync(PacketType.ScreenRequest, [0], cancellationToken);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await SecureChannel.ReceiveAsync(_stream, _sessionKey, cancellationToken).ConfigureAwait(false);

                if (packet.Type == PacketType.ScreenFrame
                    && ScreenFrameCodec.TryParse(packet.Payload, out var width, out var height, out var format, out var image))
                {
                    FrameReceived?.Invoke(new RemoteFrame(width, height, format, image));
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Disconnected?.Invoke();
        }
    }

    private async Task SendAsync(PacketType type, byte[] payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SecureChannel.SendAsync(_stream, _sessionKey, type, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _loopCts.Cancel();

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Yopilish paytidagi xato ahamiyatsiz.
            }
        }

        _stream.Dispose();
        _client.Dispose();
        _loopCts.Dispose();
        _writeLock.Dispose();
    }
}
