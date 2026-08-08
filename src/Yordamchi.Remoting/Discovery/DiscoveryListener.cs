using System.Net;
using System.Net.Sockets;

namespace Yordamchi.Remoting.Discovery;

/// <summary>Topilgan bitta kompyuter: qayerdan va qanday rol bilan e'lon qilgani.</summary>
/// <param name="Address">IP manzil (mayoq kelgan manba).</param>
/// <param name="Beacon">Mayoqning o'zi (rol, port, mashina nomi).</param>
public sealed record DiscoveredPeer(IPAddress Address, DiscoveryBeacon Beacon);

/// <summary>
/// UDP discovery portini (5405) tinglaydi va kelgan mayoqlarni <see cref="PeerDiscovered"/>
/// orqali chiqaradi. Master shu ro'yxatga qarab kompyuterlarni ko'rsatadi — IP'larni qo'lda
/// kiritish shart emas. Begona/buzuq paketlar jimgina tashlab yuboriladi.
/// </summary>
public sealed class DiscoveryListener
{
    /// <summary>Yaroqli mayoq kelganda.</summary>
    public event Action<DiscoveredPeer>? PeerDiscovered;

    /// <summary>Bekor qilinguncha discovery portini tinglaydi.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient();

        // Bir nechta dastur bir portni tinglashi mumkin (masalan bir kompyuterda agent ham,
        // master ham) — shuning uchun manzilni qayta ishlatishga ruxsat beramiz.
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryBeacon.BroadcastPort));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var beacon = DiscoveryBeacon.TryParse(result.Buffer);

                if (beacon is not null)
                    PeerDiscovered?.Invoke(new DiscoveredPeer(result.RemoteEndPoint.Address, beacon));
            }
        }
        catch (OperationCanceledException)
        {
            // To'xtatildi — odatiy.
        }
    }
}
