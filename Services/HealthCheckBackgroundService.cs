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
        private readonly IHttpClientFactory httpClientFactory;
        private System.Timers.Timer timer;

        public HealthCheckBackgroundService(IMemoryCache memoryCache, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            this.memoryCache = memoryCache;
            this.configuration = configuration;
            this.httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            timer = new System.Timers.Timer { Interval = 17 * 1000.0 };
            timer.Elapsed += async (s, e) => await HealthCheck();
            await Task.Run(() => timer.Enabled = true);

            async Task HealthCheck()
            {
                var routes = configuration.GetSection("ReRoutes").Get<List<ReRoute>>().ToArray();
                foreach (var route in routes)
                {
                    var name = route.UpstreamPathTemplate.Split(new[] { '/' })[1];
                    foreach (var hp in route.DownstreamHostAndPorts)
                    {
                        memoryCache.TryGetValue<PluginInfo>(name, out var pi);
                        pi ??= new PluginInfo { Name = name, Port = hp.Port, };
                        try
                        {
                            var url = $"{route.DownstreamScheme}://{hp.Host}:{hp.Port}/hc";
                            var hrm = await httpClientFactory.CreateClient("internal-client").GetAsync(url);
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
}
