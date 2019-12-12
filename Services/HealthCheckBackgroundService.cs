using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mystique.Services
{
    public class HealthCheckBackgroundService : BackgroundService
    {
        private readonly IMemoryCache memoryCache;
        private readonly IConfiguration configuration;
        private readonly PluginManager pluginManager;
        private readonly HttpClient httpClient;
        private System.Timers.Timer timer;

        public HealthCheckBackgroundService(IMemoryCache memoryCache, IConfiguration configuration, IHttpClientFactory httpClientFactory, PluginManager pluginManager)
        {
            this.memoryCache = memoryCache;
            this.configuration = configuration;
            this.pluginManager = pluginManager;
            this.httpClient = httpClientFactory.CreateClient("plugin-client");
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            pluginManager.ReloadPluginInfos();
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            timer = new System.Timers.Timer { Interval = 17 * 1000.0 };
            timer.Elapsed += async (s, e) => await HealthCheck();
            await Task.Run(() => timer.Enabled = true);

            async Task HealthCheck()
            {
                var pis = pluginManager.PluginInfos;
                foreach (var pi in pis)
                {
                    if (pi.Port <= 0 || pi.State.Contains("deleted"))
                    {
                        continue;
                    }
                    var name = pi.Name;
                    try
                    {
                        var hrm = await httpClient.GetAsync($"{name}/hc");
                        pi.State = $"running{(hrm.IsSuccessStatusCode ? "" : $"({hrm.StatusCode.ToString()})")}";
                    }
                    catch
                    {
                        pi.State = "stoped";
                    }
                    memoryCache.Set(name, pi);
                }
            }
        }
    }
}
