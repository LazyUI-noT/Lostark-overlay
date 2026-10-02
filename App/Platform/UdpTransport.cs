using System.Net;
using System.Net.Sockets;

namespace GaugeOverlay.App.Platform;

/// <summary>One IPv4 UDP socket with a background receive loop. No logic: bytes in, bytes out.</summary>
public sealed class UdpTransport : IDisposable
{
    readonly UdpClient _udp;
    readonly CancellationTokenSource _stop = new();

    /// <summary>Raised on the receive task (a background thread).</summary>
    public event Action<byte[], IPEndPoint>? Received;

    /// <summary>Binds <paramref name="port"/> on all interfaces (0 = any free port). Throws SocketException if taken.</summary>
    public UdpTransport(int port)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        const int SioUdpConnReset = -1744830452;   // don't fail receives on ICMP "port unreachable" from a closed peer
        _udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        _ = Task.Run(Loop);
    }

    public void Send(byte[] data, IPEndPoint to)
    {
        try { _udp.Send(data, data.Length, to); }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
    }

    async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var r = await _udp.ReceiveAsync(_stop.Token);
                Received?.Invoke(r.Buffer, r.RemoteEndPoint);
            }
            catch (SocketException) { }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { return; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
    }
}
