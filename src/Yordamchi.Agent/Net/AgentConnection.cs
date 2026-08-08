using System.IO;
using Yordamchi.Agent.Capture;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Agent.Net;

/// <summary>
/// Bitta master ulanishini boshqaradi: handshake, so'ng shifrlangan paketlar halqasi.
/// <para>
/// Yozuvlar bitta <see cref="SemaphoreSlim"/> bilan tartibga solinadi, chunki ekran kadrlari
/// fon vazifasidan, Pong esa o'quvchi halqasidan kelib, ikkalasi bir oqimga bir vaqtda
/// yozishi mumkin — bu ramkalarni bir-biriga aralashtirib yuborardi.
/// </para>
/// <para>
/// <b>Buyruq bajarish ataylab yo'q.</b> <see cref="PacketType.Command"/> hozircha e'tiborsiz
/// qoldiriladi: ixtiyoriy tizim buyrug'ini masofadan bajarish eng xavfli imkoniyat, shuning
/// uchun u keyin faqat <b>ruxsat etilgan, cheklangan</b> amallar (xabar ko'rsatish, ekranni
/// qulflash) sifatida qo'shiladi — ochiq qobiq (shell) sifatida emas.
/// </para>
/// </summary>
public sealed class AgentConnection
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);

    private readonly Stream _stream;
    private readonly IScreenSource _screen;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AgentConnection(Stream stream, IScreenSource screen)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
    }

    /// <summary>Ulanishni oxirigacha (master uzilguncha yoki bekor qilinguncha) yuritadi.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var sessionKey = await RemoteHandshake.PerformAsAgentAsync(_stream, cancellationToken).ConfigureAwait(false);

        CancellationTokenSource? streamingCts = null;
        Task? streamingTask = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Packet packet;
                try
                {
                    packet = await SecureChannel.ReceiveAsync(_stream, sessionKey, cancellationToken).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    break; // Master ulanishni yopdi.
                }

                switch (packet.Type)
                {
                    case PacketType.Ping:
                        await SendAsync(sessionKey, PacketType.Pong, [], cancellationToken).ConfigureAwait(false);
                        break;

                    case PacketType.ScreenRequest:
                        var start = packet.Payload.Length > 0 && packet.Payload[0] == 1;

                        if (start && streamingTask is null)
                        {
                            streamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            streamingTask = StreamFramesAsync(sessionKey, streamingCts.Token);
                        }
                        else if (!start && streamingTask is not null)
                        {
                            await StopStreamingAsync(streamingCts, streamingTask).ConfigureAwait(false);
                            streamingCts = null;
                            streamingTask = null;
                        }

                        break;

                    case PacketType.Command:
                        // Ataylab e'tiborsiz — yuqoridagi izohga qarang.
                        break;

                    case PacketType.Disconnect:
                        return;
                }
            }
        }
        finally
        {
            await StopStreamingAsync(streamingCts, streamingTask).ConfigureAwait(false);
        }
    }

    private async Task StreamFramesAsync(byte[] sessionKey, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = _screen.Capture();
                var payload = ScreenFrameCodec.Encode(frame.Width, frame.Height, frame.Image);

                await SendAsync(sessionKey, PacketType.ScreenFrame, payload, cancellationToken).ConfigureAwait(false);
                await Task.Delay(FrameInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ekran uzatish to'xtatildi — bu odatiy holat.
        }
    }

    private static async Task StopStreamingAsync(CancellationTokenSource? cts, Task? task)
    {
        if (cts is null || task is null)
            return;

        cts.Cancel();

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Kutilgan.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task SendAsync(byte[] sessionKey, PacketType type, byte[] payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SecureChannel.SendAsync(_stream, sessionKey, type, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
