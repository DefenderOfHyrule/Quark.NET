using System.Net;
using System.Net.Sockets;

namespace Quark.Net;

public sealed class TcpInterface : IDisposable
{
    public const int DefaultPort = 2313;

    private readonly int      _port;
    private TcpListener?      _listener;
    private TcpClient?        _client;
    private NetworkStream?    _stream;

    public TcpInterface(int port = DefaultPort) => _port = port;

    

    
    
    
    
    public bool WaitForConnection()
    {
        CloseServer();
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            Console.WriteLine($"[TCP] Listening on port {_port}...");
            _client  = _listener.AcceptTcpClient();
            _stream  = _client.GetStream();
            Console.WriteLine($"[TCP] Switch connected from {((IPEndPoint)_client.Client.RemoteEndPoint!).Address}");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[TCP] WaitForConnection failed: {e.Message}");
            CloseServer();
            return false;
        }
    }

    public bool IsConnected =>
        _client is { Connected: true } && _stream != null;

    public string? RemoteAddress =>
        (_client?.Client?.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString();

    

    
    public byte[]? ReadBytes(int length)
    {
        if (_stream is null) return null;
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
        catch
        {
            return null;
        }
    }

    
    public bool WriteBytes(byte[] data, int length = -1)
    {
        if (_stream is null) return false;
        try { _stream.Write(data, 0, length < 0 ? data.Length : length); _stream.Flush(); return true; }
        catch { return false; }
    }

    

    public void CloseClient()
    {
        _stream?.Close();  _stream  = null;
        _client?.Close();  _client  = null;
    }

    public void CloseServer()
    {
        CloseClient();
        _listener?.Stop(); _listener = null;
    }

    public void Dispose() => CloseServer();
    public int Port => _port;
}
