using System.Net;
using System.Net.Sockets;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Sinovlar uchun ulangan ikkita TCP uchi (127.0.0.1). Haqiqiy soket ustida sinash —
/// handshake va oqim mantiqi aynan ishlab turgan holatdagidek tekshiriladi.
/// </summary>
internal static class Loopback
{
    public static async Task<(TcpClient First, TcpClient Second)> ConnectPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var first = new TcpClient();
            var connect = first.ConnectAsync(IPAddress.Loopback, port);
            var second = await listener.AcceptTcpClientAsync();
            await connect;

            return (first, second);
        }
        finally
        {
            listener.Stop();
        }
    }
}
