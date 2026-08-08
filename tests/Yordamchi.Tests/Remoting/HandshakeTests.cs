using System.Text;
using Yordamchi.Remoting.Protocol;
using Yordamchi.Remoting.Security;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Kalit almashinuvi va shifrlangan kanal — haqiqiy TCP loopback ustida. Asosiy talab:
/// ikkala tomon <b>bir xil</b> sessiya kalitiga kelishi va shundan keyingi xabarlar
/// shifrlangan holda to'g'ri o'tishi.
/// </summary>
public sealed class HandshakeTests
{
    [Fact]
    public async Task Master_and_agent_derive_the_same_session_key()
    {
        var (master, agent) = await Loopback.ConnectPairAsync();

        using (master)
        using (agent)
        {
            var keys = await Task.WhenAll(
                RemoteHandshake.PerformAsMasterAsync(master.GetStream()),
                RemoteHandshake.PerformAsAgentAsync(agent.GetStream()));

            Assert.Equal(SessionCipher.KeySize, keys[0].Length);
            Assert.Equal(keys[0], keys[1]);
        }
    }

    [Fact]
    public async Task A_message_round_trips_through_the_secure_channel_after_handshake()
    {
        var (master, agent) = await Loopback.ConnectPairAsync();

        using (master)
        using (agent)
        {
            var masterStream = master.GetStream();
            var agentStream = agent.GetStream();

            var keys = await Task.WhenAll(
                RemoteHandshake.PerformAsMasterAsync(masterStream),
                RemoteHandshake.PerformAsAgentAsync(agentStream));

            var key = keys[0];

            await SecureChannel.SendAsync(masterStream, key, PacketType.Command, Encoding.UTF8.GetBytes("salom, agent"));
            var received = await SecureChannel.ReceiveAsync(agentStream, key);

            Assert.Equal(PacketType.Command, received.Type);
            Assert.Equal("salom, agent", Encoding.UTF8.GetString(received.Payload));
        }
    }
}
