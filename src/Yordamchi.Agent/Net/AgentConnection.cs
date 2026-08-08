using System.IO;
using System.Text;
using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Commands;
using Yordamchi.Agent.Input;
using Yordamchi.Remoting.Command;
using Yordamchi.Remoting.Input;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Agent.Net;

/// <summary>
/// Bitta ulanishning sozlamalari. Alohida ob'ekt bo'lishi maqsadli: ulanishga qo'shiladigan
/// imkoniyatlar (kirish, buyruqlar, jurnal, kadr tezligi) o'sib boradi va ularni konstruktor
/// parametrlari sifatida ketma-ket qo'shish har safar chaqiruv joylarini buzardi.
/// <para>
/// Standart qiymatlar <b>eng cheklangan</b> holat: kirish ham, buyruqlar ham o'chirilgan.
/// Ular faqat host ataylab yoqqanda ishlaydi.
/// </para>
/// </summary>
public sealed class AgentConnectionOptions
{
    /// <summary>Kirish hodisalarini bajaruvchi (standart: o'chirilgan).</summary>
    public IInputSink Input { get; init; } = DisabledInputSink.Instance;

    /// <summary>Cheklangan buyruqlarni bajaruvchi (standart: o'chirilgan).</summary>
    public ICommandSink Commands { get; init; } = DisabledCommandSink.Instance;

    /// <summary>Ixtiyoriy jurnal.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Kadrlar orasidagi kutish — 100 ms ≈ 10 kadr/sek.</summary>
    public TimeSpan FrameInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Bitta master ulanishini boshqaradi: handshake, so'ng shifrlangan paketlar halqasi.
/// <para>
/// Yozuvlar bitta <see cref="SemaphoreSlim"/> bilan tartibga solinadi, chunki ekran kadrlari
/// fon vazifasidan, Pong esa o'quvchi halqasidan kelib, ikkalasi bir oqimga bir vaqtda
/// yozishi mumkin — bu ramkalarni bir-biriga aralashtirib yuborardi.
/// </para>
/// <para>
/// <b>Buyruqlar cheklangan.</b> <see cref="PacketType.Command"/> faqat
/// <see cref="RemoteCommandKind"/> dagi yopiq ro'yxatni (xabar ko'rsatish, ekranni qulflash)
/// qabul qiladi va uni <see cref="AgentConnectionOptions.Commands"/> bajaradi. Ixtiyoriy tizim
/// buyrug'ini masofadan bajarish imkoniyati yo'q va qo'shilmaydi.
/// </para>
/// </summary>
public sealed class AgentConnection
{
    private readonly Stream _stream;
    private readonly IScreenSource _screen;
    private readonly AgentConnectionOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Rad etish haqida master faqat bir marta ogohlantiriladi: sichqoncha harakati sekundda
    /// o'nlab hodisa yuboradi va har biriga xabar qaytarish kanalni ham, jurnalni ham
    /// ko'mib tashlardi.
    /// </summary>
    private bool _refusalReported;

    public AgentConnection(Stream stream, IScreenSource screen, AgentConnectionOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _options = options ?? new AgentConnectionOptions();
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

                    case PacketType.InputEvent:
                        await HandleInputAsync(sessionKey, packet.Payload, cancellationToken).ConfigureAwait(false);
                        break;

                    case PacketType.Command:
                        await HandleCommandAsync(sessionKey, packet.Payload, cancellationToken).ConfigureAwait(false);
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

    private async Task HandleInputAsync(byte[] sessionKey, byte[] payload, CancellationToken cancellationToken)
    {
        // Buzuq yoki noto'g'ri o'lchamli hodisa — jimgina rad etiladi: bu tarmoqdan kelgan
        // ishonchsiz ma'lumot, u ulanishni yiqitmasligi kerak.
        if (!InputEventCodec.TryParse(payload, out var input))
            return;

        if (_options.Input.Inject(input, _screen.Bounds))
            return;

        // Sabab ikki xil bo'lishi mumkin: ruxsat yo'q, yoki tizim hodisani rad etdi (masalan
        // UIPI — administrator huquqi bilan ishlayotgan oynaga kirish yuborilmaydi). Ikkalasi
        // ham masterga bir xil ko'rinadi, shuning uchun xabar ham ikkisini qamraydi.
        await ReportRefusalAsync(
            sessionKey,
            "Kirish hodisasi bajarilmadi: bu kompyuterda masofadan boshqarish o'chirilgan "
            + "yoki tizim hodisani rad etdi.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCommandAsync(byte[] sessionKey, byte[] payload, CancellationToken cancellationToken)
    {
        if (!RemoteCommandCodec.TryParse(payload, out var command))
        {
            // Noma'lum buyruq — ro'yxat yopiq, shuning uchun bajarilmaydi.
            await ReportRefusalAsync(sessionKey, "Buyruq qo'llab-quvvatlanmaydi.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_options.Commands.Execute(command))
            return;

        await ReportRefusalAsync(
            sessionKey,
            "Buyruq bajarilmadi: bu kompyuterda masofaviy buyruqlar o'chirilgan yoki amal rad etildi.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportRefusalAsync(byte[] sessionKey, string reason, CancellationToken cancellationToken)
    {
        if (_refusalReported)
            return;

        _refusalReported = true;
        _options.Log?.Invoke($"Rad etildi: {reason}");

        await SendAsync(sessionKey, PacketType.Error, Encoding.UTF8.GetBytes(reason), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StreamFramesAsync(byte[] sessionKey, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = _screen.Capture();
                var payload = ScreenFrameCodec.Encode(frame.Width, frame.Height, frame.Format, frame.Image);

                await SendAsync(sessionKey, PacketType.ScreenFrame, payload, cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.FrameInterval, cancellationToken).ConfigureAwait(false);
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
