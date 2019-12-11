using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mystique.Services
{
    public class ClientWebSocketBackgroundService : BackgroundService
    {
        private readonly IConfiguration configuration;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly PluginManager pluginManager;
        private readonly System.Timers.Timer keepAliveTimer;
        private ClientWebSocket clientWebSocket;
        private bool working = false;

        public ClientWebSocketBackgroundService(IConfiguration configuration, IHttpClientFactory httpClientFactory, PluginManager pluginManager)
        {
            this.configuration = configuration;
            this.httpClientFactory = httpClientFactory;
            this.pluginManager = pluginManager;
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
                            // 格式：指令|参数|是否需要应答
                            // 如：AddPlugin|http://192.168.1.1:8080/Miao.Web.arm64.20191211.zip|true
                            var command = Encoding.UTF8.GetString(buffer[0..result.Count]).Split(new[] { '|' });
                            await ExecuteServerCommandAsync(command[0], command[1..(command.Length - 1)], bool.TryParse(command.Last(), out var r) ? r : false);
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

        private async Task ExecuteServerCommandAsync(string method, string[] arguments, bool response)
        {
            switch (method)
            {
                case nameof(PluginManager.AddPlugin):
                    var client = httpClientFactory.CreateClient("internal-client");
                    var name = arguments?.ElementAtOrDefault(0)?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last();
                    if (pluginManager.IsValidZip(name))
                    {
                        var stream = await client.GetStreamAsync(arguments.ElementAt(0));
                        pluginManager.AddPlugin(stream, name);
                    }
                    else
                    {
                        await SendAsync($"{method}|插件命名格式错误({arguments?.ElementAtOrDefault(0)})");
                    }
                    break;
                case nameof(PluginManager.EnablePlugin):
                case nameof(PluginManager.DisablePlugin):
                case nameof(PluginManager.DeletePlugin):
                    var site = arguments?.ElementAtOrDefault(0);
                    var version = arguments?.ElementAtOrDefault(1);
                    if (string.IsNullOrEmpty(site) || string.IsNullOrEmpty(version))
                    {
                        await SendAsync($"{method}|site 和 version 必须同时指定");
                        return;
                    }
                    typeof(PluginManager).GetMethod(method).Invoke(pluginManager, new object[2] { site, version });
                    break;
                default: break;
            }
        }
    }
}
