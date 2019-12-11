using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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
            services.AddHttpClient("http-client");
            services.AddHttpClient("plugin-client", options =>
            {
                var url = Configuration.GetSection("PluginBaseAddress").Get<string>();
                options.BaseAddress = new System.Uri(url);
            });
            services.AddOcelot();
            services.AddMemoryCache();
            services.AddSingleton<PluginManager>();
            services.AddHostedService<Services.HealthCheckBackgroundService>();
            services.AddHostedService<Services.ClientWebSocketBackgroundService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, PluginManager pluginManager)
        {
#if FALSE
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
            UseFileServer(app, Path.Combine(env.ContentRootPath, "log4net"), "/logs");
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                   name: "default",
                   pattern: "{controller=Home}/{action=Index}/{id?}");
            });
            app.UseOcelot().Wait();
        }

        private static void UseFileServer(IApplicationBuilder app, string folder, string url)
        {
            var fs = new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(folder),
                RequestPath = url,
                EnableDirectoryBrowsing = true,
            };
            fs.StaticFileOptions.ServeUnknownFileTypes = true;
            fs.StaticFileOptions.DefaultContentType = "text/plain; charset=utf-8";
            app.UseFileServer(fs);
        }
    }
}
