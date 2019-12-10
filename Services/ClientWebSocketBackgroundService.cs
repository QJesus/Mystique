using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mystique.Services
{
    public class ClientWebSocketBackgroundService : BackgroundService
    {
        private readonly IConfiguration configuration;
        private readonly System.Timers.Timer keepAliveTimer;
        private ClientWebSocket clientWebSocket;
        private bool working = false;

        public ClientWebSocketBackgroundService(IConfiguration configuration)
        {
            this.configuration = configuration;
            keepAliveTimer = new System.Timers.Timer { Interval = 12 * 1000.0 };
            clientWebSocket = new ClientWebSocket();
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            keepAliveTimer.Enabled = false;
            using (clientWebSocket) { }
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var buffer = new byte[1024 * 8];
            keepAliveTimer.Elapsed += async (s, e) =>
            {
                if (working)
                {
                    return;
                }

                working = true;
                switch (clientWebSocket.State)
                {
                    case WebSocketState.None: break;
                    case WebSocketState.Connecting:
                    case WebSocketState.Open:
                    case WebSocketState.CloseSent:
                    case WebSocketState.CloseReceived: return;
                    case WebSocketState.Closed:
                    case WebSocketState.Aborted: using (clientWebSocket) { } clientWebSocket = new ClientWebSocket(); break;
                }

                try
                {
                    var addr = configuration.GetSection("WebSocketServer").Get<string>();
                    await clientWebSocket.ConnectAsync(new Uri(addr), stoppingToken);
                    while (clientWebSocket.State == WebSocketState.Open)
                    {
                        var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.Count > 0)
                        {
                            var str = Encoding.UTF8.GetString(buffer[0..result.Count]);
                            // TODO...
                            Console.WriteLine(str);
                        }
                        await Task.Delay(500);
                    }
                }
                catch { }
                finally
                {
                    working = false;
                }
            };
            await Task.Run(() => keepAliveTimer.Enabled = true);
        }

        public async Task SendAsync(string message)
        {
            while (clientWebSocket.State != WebSocketState.Open)
            {
                await Task.Delay(500);
            }
            var bytes = Encoding.UTF8.GetBytes(message);
            await clientWebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, default);
        }
    }
}
