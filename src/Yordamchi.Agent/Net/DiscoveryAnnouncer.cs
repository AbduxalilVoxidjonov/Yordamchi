using System.Net;
using System.Net.Sockets;
using Yordamchi.Remoting.Discovery;

namespace Yordamchi.Agent.Net;

/// <summary>
/// Agent o'zini lokal tarmoqda muntazam e'lon qiladi (UDP broadcast). Master bu mayoqni
/// eshitib kompyuterni ro'yxatga qo'shadi, shu tufayli IP manzillarni qo'lda kiritish
/// shart emas. Mayoqda maxfiy narsa yo'q — faqat rol, port va mashina nomi.
/// </summary>
public sealed class DiscoveryAnnouncer
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly int _tcpPort;
    private readonly string _machineName;

    public DiscoveryAnnouncer(int tcpPort, string machineName)
    {
        _tcpPort = tcpPort;
        _machineName = machineName;
    }

    /// <summary>Bekor qilinguncha har necha soniyada mayoq yuboradi.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var beacon = new DiscoveryBeacon(PeerRole.Agent, _tcpPort, _machineName).ToBytes();
        var target = new IPEndPoint(IPAddress.Broadcast, DiscoveryBeacon.BroadcastPort);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await udp.SendAsync(beacon, beacon.Length, target).ConfigureAwait(false);
                await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // To'xtatildi — odatiy.
        }
    }
}
