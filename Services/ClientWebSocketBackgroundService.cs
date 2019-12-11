using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mystique.Services
{
    public class ClientWebSocketBackgroundService : BackgroundService
    {
        private readonly IConfiguration configuration;
        private readonly HttpClient httpClient;
        private readonly HttpClient pluginHttpClient;
        private readonly PluginManager pluginManager;
        private readonly log4net.ILog logger;
        private readonly System.Timers.Timer keepAliveTimer;
        private ClientWebSocket clientWebSocket;
        private bool working = false;
        private readonly Subject<string> commandSubject = new Subject<string>();

        public ClientWebSocketBackgroundService(IConfiguration configuration, IHttpClientFactory httpClientFactory, PluginManager pluginManager)
        {
            this.configuration = configuration;
            this.httpClient = httpClientFactory.CreateClient("http-client");
            this.pluginHttpClient = httpClientFactory.CreateClient("plugin-client");
            this.pluginManager = pluginManager;
            this.logger = log4net.LogManager.GetLogger(".NETCoreRepository", "ClientWebSocket");
            keepAliveTimer = new System.Timers.Timer { Interval = 12 * 1000.0 };
            clientWebSocket = new ClientWebSocket();
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            commandSubject.Subscribe(async x =>
            {
                var command = x.Split(new[] { '|' });
                var message = await ExecuteServerCommandAsync(command[0], command.Skip(2).ToArray());
                if (bool.TryParse(command.ElementAtOrDefault(1), out var r) && r)
                {
                    var feedback = new string[2] { command[0], message }.Concat(command.Skip(2));
                    logger.Info($"反馈结果 {string.Join("|", feedback)}");
                    await SendAsync(string.Join("|", feedback));
                }
            });
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            keepAliveTimer.Enabled = false;
            using (clientWebSocket) { }
            using (commandSubject) { }
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var buffer = new byte[4 * 1024];
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
                    logger.Info($"WebSocket 连接成功，服务端地址 {addr}");
                    while (clientWebSocket.State == WebSocketState.Open)
                    {
                        var buffers = new List<byte>();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            buffers.AddRange(buffer[0..result.Count]);
                        } while (!result.EndOfMessage);
                        if (buffers.Count > 0)
                        {
                            // 格式：指令|是否需要应答|参数
                            // 如：AddPlugin|true|http://192.168.1.1:8080/Miao.Web.arm64.20191211.zip
                            var command = Encoding.UTF8.GetString(buffer[0..result.Count]);
                            logger.Info($"收到指令 {command}");
                            commandSubject.OnNext(command);
                        }
                        await Task.Delay(500);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("WebSocket 出现错误", ex);
                }
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
            await clientWebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task<string> ExecuteServerCommandAsync(string method, string[] arguments)
        {
            var response = $"未知指令 {method}";
            switch (method)
            {
                case "AddPlugin":
                    try
                    {
                        var name = arguments?.ElementAtOrDefault(0)?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last();
                        var stream = await httpClient.GetStreamAsync(arguments.ElementAt(0));
                        pluginManager.AddPlugin(stream, name);
                        response = "更新插件成功";
                    }
                    catch (Exception ex)
                    {
                        logger.Error("更新插件出现错误", ex);
                        response = "更新插件出现错误: " + ex.InnerException?.Message + ex.Message;
                    }
                    break;
                case "EnablePlugin":
                case "DisablePlugin":
                case "DeletePlugin":
                    try
                    {
                        var site = arguments?.ElementAtOrDefault(0);
                        var version = arguments?.ElementAtOrDefault(1);
                        typeof(PluginManager).GetMethod(method).Invoke(pluginManager, new object[2] { site, version });
                        response = "控制插件成功";
                    }
                    catch (Exception ex)
                    {
                        logger.Error("控制插件出现错误", ex);
                        response = "控制插件出现错误: " + ex.InnerException?.Message + ex.Message;
                    }
                    break;
                case "Invoke":
                    try
                    {
                        // Invoke|true|Miao.Web|GET api/invoke/device?device=C1&method=&arguments=
                        var site = arguments[0];
                        var url = arguments[1].Split(' ');
                        var request = new HttpRequestMessage
                        {
                            Method = new HttpMethod(url[0]),
                            Content = new StringContent(url.ElementAtOrDefault(2) ?? string.Empty),
                            RequestUri = new Uri(pluginHttpClient.BaseAddress.AbsoluteUri + url[1]),
                        };
                        using HttpResponseMessage hrm = await pluginHttpClient.SendAsync(request);
                        var json = await hrm.Content.ReadAsStringAsync();
                        response = $"设备指令执行结果: {json}";
                    }
                    catch (Exception ex)
                    {
                        logger.Error("设备指令出现错误", ex);
                        response = "设备指令出现错误: " + ex.InnerException?.Message + ex.Message;
                    }
                    break;
            }
            return response;
        }
    }
}
