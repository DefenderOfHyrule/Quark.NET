using System.Net;
using System.Net.Sockets;
using Quark.Cf;

namespace Quark.Net;

public static class TcpCommandLoop
{
    public static Action<string, string>? OnClientConnected;     
    public static Action<string, string>? OnClientDisconnected;  
    public static Func<string, CommandFramework.IProgressListener?>? ListenerFactory; 

    public static Task RunAsync(int port, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try { await AcceptLoop(port, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine($"[TCP] Accept loop crashed: {ex.Message}"); }
        }, ct);
    }

    private static async Task AcceptLoop(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        Console.WriteLine($"[TCP] Listening on port {port}...");
        ct.Register(() => { try { listener.Stop(); } catch { } });

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => HandleClient(client, ct), ct);
        }

        try { listener.Stop(); } catch { }
        Console.WriteLine("[TCP] Accept loop exiting.");
    }

    private static void HandleClient(TcpClient rawClient, CancellationToken ct)
    {
        using var tcpSession = new TcpClientSession(rawClient);
        string consoleId = tcpSession.RemoteAddress;
        Console.WriteLine($"[TCP] Switch connected from {tcpSession.RemoteAddress}");
        OnClientConnected?.Invoke(tcpSession.RemoteAddress, consoleId);

        var cmdSession = new CommandFramework.CommandSession
        {
            Listener  = ListenerFactory?.Invoke(consoleId),
            ConsoleId = consoleId
        };

        while (!ct.IsCancellationRequested && tcpSession.IsConnected)
        {
            try
            {
                var block = new TcpSessionCommandBlock(tcpSession);
                if (!block.IsValid())
                {
                    Console.WriteLine($"[TCP] Lost connection from {tcpSession.RemoteAddress}");
                    break;
                }
                CommandFramework.Dispatch(block, cmdSession);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP] Command error from {tcpSession.RemoteAddress}: {ex.Message}");
                break;
            }
        }

        cmdSession.Dispose();
        Console.WriteLine($"[TCP] Switch disconnected: {tcpSession.RemoteAddress}");
        OnClientDisconnected?.Invoke(tcpSession.RemoteAddress, consoleId);
    }
}
