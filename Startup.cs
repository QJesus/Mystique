using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.IO;

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
            services.AddMvc();
            services.AddHttpClient("internal-client", client =>
            {
                client.BaseAddress = new System.Uri(Configuration.GetSection("Kestrel:Endpoints:Http:Url").Get<string>().Replace("*", "127.0.0.1"));
            });
            services.AddOcelot();
            services.AddMemoryCache();
            services.AddSingleton<PluginManager>();
            // services.AddHostedService<Services.DownloadPluginsBackgroundService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, PluginManager pluginManager)
        {
#if DEBUG
            lifetime.ApplicationStarted.Register(() =>
            {
                var dir = Path.Combine(env.ContentRootPath, "zips");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                foreach (var zip in Directory.EnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly))
                {
                    if (pluginManager.IsValidZip(zip))
                    {
                        using var zipStream = File.OpenRead(zip);
                        pluginManager.AddPlugin(zipStream, Path.GetFileName(zip), true);
                    }
                }
            });
#endif
            app.UseDeveloperExceptionPage();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                   name: "default",
                   pattern: "{controller=Home}/{action=Index}/{id?}");
            });
            app.UseOcelot().Wait();
        }
    }
}
