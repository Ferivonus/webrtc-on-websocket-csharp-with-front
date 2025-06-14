using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    static async Task Main(string[] args)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:5000/ws/");  // Artık localhost yerine 127.0.0.1
        listener.Start();
        Console.WriteLine("WebSocket sunucusu başlatıldı: ws://127.0.0.1:5000/ws/");

        while (true)
        {
            var ctx = await listener.GetContextAsync();
            if (ctx.Request.IsWebSocketRequest)
                _ = Handle(ctx);
            else
                ctx.Response.StatusCode = 400;
        }
    }

    private static async Task Handle(HttpListenerContext ctx)
    {
        var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
        var socket = wsCtx.WebSocket;
        var id = Guid.NewGuid().ToString();
        _clients[id] = socket;
        Console.WriteLine($"Client {id} bağlandı.");

        var buf = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var res = await socket.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close) break;
                var msg = new ArraySegment<byte>(buf, 0, res.Count);

                // Aldığımız her metin mesajını diğer tüm client'lara yayınla
                foreach (var kv in _clients)
                {
                    if (kv.Key == id) continue;
                    var other = kv.Value;
                    if (other.State == WebSocketState.Open)
                    {
                        await other.SendAsync(msg, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
        catch (WebSocketException e)
        {
            Console.WriteLine($"WS hatası ({id}): {e.Message}");
        }
        finally
        {
            _clients.TryRemove(id, out _);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            Console.WriteLine($"Client {id} ayrıldı.");
        }
    }
}
