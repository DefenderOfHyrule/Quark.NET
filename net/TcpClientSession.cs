using System.Net;
using System.Net.Sockets;

namespace Quark.Net;

public sealed class TcpClientSession : IDisposable
{
    private readonly TcpClient     _client;
    private readonly NetworkStream _stream;

    public string RemoteAddress { get; }

    public TcpClientSession(TcpClient client)
    {
        _client       = client;
        _stream       = client.GetStream();
        RemoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
    }

    public bool IsConnected => _client.Connected;

    public byte[]? ReadBytes(int length)
    {
        byte[] buf = new byte[length];
        int received = 0;
        try
        {
            while (received < length)
            {
                int r = _stream.Read(buf, received, length - received);
                if (r == 0) return null;
                received += r;
            }
            return buf;
        }
        catch { return null; }
    }

    public bool WriteBytes(byte[] data, int length = -1)
    {
        try { _stream.Write(data, 0, length < 0 ? data.Length : length); _stream.Flush(); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _stream.Close(); } catch { }
        try { _client.Close(); } catch { }
    }
}
