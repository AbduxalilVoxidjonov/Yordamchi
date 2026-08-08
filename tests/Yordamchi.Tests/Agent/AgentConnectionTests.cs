using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Net;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;
using Yordamchi.Tests.Remoting;

namespace Yordamchi.Tests.Agent;

/// <summary>
/// Agent ulanishining to'liq yo'li — haqiqiy TCP loopback ustida "master" bo'lib ulanamiz,
/// handshake qilamiz va agent kutilgan javoblarni berishini tekshiramiz. Har bir sinov
/// muhlat bilan himoyalangan: biror joyda osilib qolsa, test qotib qolmasdan yiqiladi.
/// </summary>
public sealed class AgentConnectionTests
{
    private static CancellationTokenSource TimeoutGuard() => new(TimeSpan.FromSeconds(15));

    [Fact]
    public async Task The_agent_completes_handshake_and_answers_a_ping()
    {
        using var guard = TimeoutGuard();
        var (master, agent) = await Loopback.ConnectPairAsync();

        using (master)
        using (agent)
        {
            var agentRun = new AgentConnection(agent.GetStream(), new SyntheticScreenSource())
                .RunAsync(guard.Token);

            var masterStream = master.GetStream();
            var key = await RemoteHandshake.PerformAsMasterAsync(masterStream, guard.Token);

            await SecureChannel.SendAsync(masterStream, key, PacketType.Ping, [], guard.Token);
            var reply = await SecureChannel.ReceiveAsync(masterStream, key, guard.Token);

            Assert.Equal(PacketType.Pong, reply.Type);

            await SecureChannel.SendAsync(masterStream, key, PacketType.Disconnect, [], guard.Token);
            await agentRun; // Disconnect'dan keyin tartibli tugashi kerak
        }
    }

    [Fact]
    public async Task The_agent_streams_a_frame_after_a_screen_request()
    {
        using var guard = TimeoutGuard();
        var (master, agent) = await Loopback.ConnectPairAsync();

        using (master)
        using (agent)
        {
            var agentRun = new AgentConnection(agent.GetStream(), new SyntheticScreenSource())
                .RunAsync(guard.Token);

            var masterStream = master.GetStream();
            var key = await RemoteHandshake.PerformAsMasterAsync(masterStream, guard.Token);

            // 1 = boshlash
            await SecureChannel.SendAsync(masterStream, key, PacketType.ScreenRequest, [1], guard.Token);

            var frame = await SecureChannel.ReceiveAsync(masterStream, key, guard.Token);

            Assert.Equal(PacketType.ScreenFrame, frame.Type);
            Assert.True(ScreenFrameCodec.TryParse(frame.Payload, out var width, out var height, out var format, out var image));
            Assert.Equal(32, width);
            Assert.Equal(32, height);
            Assert.Equal(ScreenImageFormat.RawBgra, format);
            Assert.NotEmpty(image);

            await SecureChannel.SendAsync(masterStream, key, PacketType.Disconnect, [], guard.Token);
            await agentRun;
        }
    }

    [Fact]
    public async Task A_client_that_speaks_the_wrong_protocol_is_dropped_without_crashing()
    {
        using var guard = TimeoutGuard();
        var (master, agent) = await Loopback.ConnectPairAsync();

        using (master)
        using (agent)
        {
            var agentRun = new AgentConnection(agent.GetStream(), new SyntheticScreenSource())
                .RunAsync(guard.Token);

            // Handshake o'rniga axlat yuboramiz — agent buni ProtocolException bilan rad etishi kerak.
            var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            await master.GetStream().WriteAsync(garbage, guard.Token);
            await master.GetStream().FlushAsync(guard.Token);

            await Assert.ThrowsAnyAsync<Exception>(() => agentRun);
        }
    }
}
