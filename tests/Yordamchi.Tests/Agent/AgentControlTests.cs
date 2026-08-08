using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Commands;
using Yordamchi.Agent.Input;
using Yordamchi.Agent.Net;
using Yordamchi.Remoting.Command;
using Yordamchi.Remoting.Input;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;
using Yordamchi.Tests.Remoting;

namespace Yordamchi.Tests.Agent;

/// <summary>
/// Boshqaruvning agentdagi yo'li: haqiqiy TCP loopback ustida "master" bo'lib ulanamiz va
/// kirish hodisasi hamda buyruq shifrlangan kanal orqali <b>bajaruvchigacha</b> yetib borishini
/// tekshiramiz.
/// <para>
/// Haqiqiy <c>SendInput</c> o'rniga yozib boruvchi bajaruvchi qo'yiladi — shu tufayli butun
/// zanjir (paket → kodlash → ruxsat → bajaruvchi) apparatga tegmasdan, sichqonchani qimirlatmasdan
/// sinaladi.
/// </para>
/// </summary>
public sealed class AgentControlTests
{
    private static CancellationTokenSource TimeoutGuard() => new(TimeSpan.FromSeconds(15));

    [Fact]
    public async Task An_input_event_reaches_the_sink_with_the_captured_region()
    {
        using var guard = TimeoutGuard();
        var input = new RecordingInputSink(accept: true);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Input = input },
            guard.Token);

        await session.SendAsync(PacketType.InputEvent, InputEventCodec.Encode(InputEvent.MouseMove(0.25f, 0.5f)));

        var received = await input.WaitForOneAsync(guard.Token);

        Assert.Equal(InputEventKind.MouseMove, received.Input.Kind);
        Assert.Equal(0.25f, received.Input.X);
        Assert.Equal(0.5f, received.Input.Y);

        // Sintetik manba 32×32 kadr beradi — bajaruvchiga aynan shu to'rtburchak uzatilishi kerak,
        // aks holda normallashtirilgan o'rin boshqa joyga tushardi.
        Assert.Equal(new ScreenRegion(0, 0, 32, 32), received.Region);
    }

    [Fact]
    public async Task A_key_event_reaches_the_sink()
    {
        using var guard = TimeoutGuard();
        var input = new RecordingInputSink(accept: true);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Input = input },
            guard.Token);

        await session.SendAsync(PacketType.InputEvent, InputEventCodec.Encode(InputEvent.Key(0x41, pressed: true)));

        var received = await input.WaitForOneAsync(guard.Token);

        Assert.Equal(InputEventKind.Key, received.Input.Kind);
        Assert.Equal(0x41, received.Input.KeyCode);
        Assert.True(received.Input.Pressed);
    }

    [Fact]
    public async Task Input_is_ignored_when_permission_is_off_and_the_master_is_told_once()
    {
        using var guard = TimeoutGuard();

        // Ruxsat o'chirilgan: darvoza yopiq bo'lgani uchun haqiqiy bajaruvchiga yetib bormaydi.
        var input = new RecordingInputSink(accept: true);
        var gated = new GatedInputSink(input, () => false);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Input = gated },
            guard.Token);

        await session.SendAsync(PacketType.InputEvent, InputEventCodec.Encode(InputEvent.MouseMove(0.5f, 0.5f)));
        await session.SendAsync(PacketType.InputEvent, InputEventCodec.Encode(InputEvent.MouseMove(0.6f, 0.6f)));

        var error = await session.ReceiveAsync(guard.Token);
        Assert.Equal(PacketType.Error, error.Type);
        Assert.NotEmpty(error.Payload);

        // Ikkinchi hodisa uchun yana xabar kelmasligi kerak (sichqoncha sekundda o'nlab hodisa
        // yuboradi) — buni tekshirish uchun Ping yuboramiz: navbatdagi javob Pong bo'lishi shart.
        await session.SendAsync(PacketType.Ping, []);
        var next = await session.ReceiveAsync(guard.Token);

        Assert.Equal(PacketType.Pong, next.Type);
        Assert.Empty(input.Received);
    }

    [Fact]
    public async Task A_malformed_input_payload_does_not_break_the_connection()
    {
        using var guard = TimeoutGuard();
        var input = new RecordingInputSink(accept: true);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Input = input },
            guard.Token);

        await session.SendAsync(PacketType.InputEvent, [1, 2, 3]);
        await session.SendAsync(PacketType.Ping, []);

        var reply = await session.ReceiveAsync(guard.Token);

        Assert.Equal(PacketType.Pong, reply.Type);
        Assert.Empty(input.Received);
    }

    [Fact]
    public async Task An_allowed_command_reaches_the_command_sink()
    {
        using var guard = TimeoutGuard();
        var commands = new RecordingCommandSink(accept: true);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Commands = commands },
            guard.Token);

        await session.SendAsync(
            PacketType.Command,
            RemoteCommandCodec.Encode(RemoteCommand.ShowMessage("Dars boshlandi")));

        var received = await commands.WaitForOneAsync(guard.Token);

        Assert.Equal(RemoteCommandKind.ShowMessage, received.Kind);
        Assert.Equal("Dars boshlandi", received.Text);
    }

    [Fact]
    public async Task An_unknown_command_is_refused_and_never_reaches_the_sink()
    {
        using var guard = TimeoutGuard();
        var commands = new RecordingCommandSink(accept: true);

        await using var session = await AgentTestSession.StartAsync(
            new AgentConnectionOptions { Commands = commands },
            guard.Token);

        var payload = RemoteCommandCodec.Encode(RemoteCommand.LockScreen());
        payload[0] = 99; // ro'yxatda yo'q buyruq

        await session.SendAsync(PacketType.Command, payload);

        var error = await session.ReceiveAsync(guard.Token);

        Assert.Equal(PacketType.Error, error.Type);
        Assert.Empty(commands.Received);
    }

    // ---------------------------------------------------------------- sinov yordamchilari

    /// <summary>
    /// Ulangan agent + master juftligi. Har sinovda handshake va tozalash takrorlanmasligi uchun
    /// bitta joyda.
    /// </summary>
    private sealed class AgentTestSession : IAsyncDisposable
    {
        private readonly TcpClient _master;
        private readonly TcpClient _agent;
        private readonly Stream _masterStream;
        private readonly byte[] _key;
        private readonly Task _agentRun;

        private AgentTestSession(
            TcpClient master,
            TcpClient agent,
            Stream masterStream,
            byte[] key,
            Task agentRun)
        {
            _master = master;
            _agent = agent;
            _masterStream = masterStream;
            _key = key;
            _agentRun = agentRun;
        }

        public static async Task<AgentTestSession> StartAsync(
            AgentConnectionOptions options,
            CancellationToken cancellationToken)
        {
            var (master, agent) = await Loopback.ConnectPairAsync();

            var agentRun = new AgentConnection(agent.GetStream(), new SyntheticScreenSource(), options)
                .RunAsync(cancellationToken);

            var masterStream = master.GetStream();
            var key = await RemoteHandshake.PerformAsMasterAsync(masterStream, cancellationToken);

            return new AgentTestSession(master, agent, masterStream, key, agentRun);
        }

        public Task SendAsync(PacketType type, byte[] payload) =>
            SecureChannel.SendAsync(_masterStream, _key, type, payload);

        public Task<Packet> ReceiveAsync(CancellationToken cancellationToken) =>
            SecureChannel.ReceiveAsync(_masterStream, _key, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await SecureChannel.SendAsync(_masterStream, _key, PacketType.Disconnect, []);
                await _agentRun;
            }
            catch (Exception)
            {
                // Yopilish paytidagi xato sinov natijasiga ta'sir qilmasligi kerak.
            }

            _master.Dispose();
            _agent.Dispose();
        }
    }

    private sealed class RecordingInputSink : IInputSink
    {
        private readonly bool _accept;
        private readonly SemaphoreSlim _signal = new(0);

        public RecordingInputSink(bool accept) => _accept = accept;

        public ConcurrentQueue<(InputEvent Input, ScreenRegion Region)> Received { get; } = new();

        public bool Inject(in InputEvent input, ScreenRegion region)
        {
            Received.Enqueue((input, region));
            _signal.Release();
            return _accept;
        }

        public async Task<(InputEvent Input, ScreenRegion Region)> WaitForOneAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            Assert.True(Received.TryDequeue(out var received));
            return received;
        }
    }

    private sealed class RecordingCommandSink : ICommandSink
    {
        private readonly bool _accept;
        private readonly SemaphoreSlim _signal = new(0);

        public RecordingCommandSink(bool accept) => _accept = accept;

        public ConcurrentQueue<RemoteCommand> Received { get; } = new();

        public bool Execute(in RemoteCommand command)
        {
            Received.Enqueue(command);
            _signal.Release();
            return _accept;
        }

        public async Task<RemoteCommand> WaitForOneAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            Assert.True(Received.TryDequeue(out var received));
            return received;
        }
    }
}
