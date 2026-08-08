using System.Net;
using System.Net.Sockets;
using Yordamchi.Agent.Capture;
using Yordamchi.Agent.Net;
using Yordamchi.Remoting.Master;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Tests.Agent;

/// <summary>
/// Master klienti va agentni <b>birga</b> sinaydi: master ulanadi, handshake qiladi, ekran
/// so'raydi va agent yuborgan kadrni to'g'ri oladi — hammasi 127.0.0.1 loopback ustida
/// (brandmauer so'rovisiz).
/// </summary>
public sealed class MasterSessionTests
{
    [Fact]
    public async Task The_master_connects_handshakes_and_receives_a_frame_from_the_agent()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Agent tomoni: ulanishni qabul qilib, AgentConnection'ni yuritadi.
        var agentTask = Task.Run(async () =>
        {
            using var agentClient = await listener.AcceptTcpClientAsync(guard.Token);
            await using var stream = agentClient.GetStream();
            await new AgentConnection(stream, new SyntheticScreenSource()).RunAsync(guard.Token);
        }, guard.Token);

        try
        {
            await using var session = await MasterSession.ConnectAsync("127.0.0.1", port, guard.Token);

            var firstFrame = new TaskCompletionSource<RemoteFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.FrameReceived += frame => firstFrame.TrySetResult(frame);

            await session.StartScreenAsync(guard.Token);

            var received = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(10), guard.Token);

            Assert.Equal(32, received.Width);
            Assert.Equal(32, received.Height);
            Assert.Equal(ScreenImageFormat.RawBgra, received.Format);
            Assert.NotEmpty(received.Image);
        }
        finally
        {
            listener.Stop();
        }
    }
}
