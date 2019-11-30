using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mystique.Core.Mvc.Infrastructure;

namespace Mystique
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.MystiqueSetup();
            services.AddHttpClient("internal-client", client =>
            {
                client.BaseAddress = new System.Uri(Configuration.GetSection("Kestrel:Endpoints:Http:Url").Get<string>().Replace("*", "127.0.0.1"));
            });
            services.AddHttpClient("http-client");
            services.AddHostedService<Services.DownloadPluginsBackgroundService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
            }
            else
            {
            }

            app.UseDeveloperExceptionPage();

            app.UseStaticFiles();

            app.MystiqueRoute(lifetime);
        }
    }
}
