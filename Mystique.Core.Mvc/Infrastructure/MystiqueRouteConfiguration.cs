﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Mystique.Core.Mvc.Infrastructure
{
    public static class MystiqueRouteConfiguration
    {
        public static IApplicationBuilder MystiqueRoute(this IApplicationBuilder app, IHostApplicationLifetime lifetime)
        {
            lifetime.ApplicationStopped.Register(() =>
            {
                var json = JsonConvert.SerializeObject(PluginsLoadContexts.GetPlugins().Select(o =>
                {
                    o.PluginContext = null;
                    return o;
                }));
                var pluginFolder = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", "plugins_cache.json");
                File.WriteAllText(pluginFolder, json, Encoding.UTF8);
            });

            app.UseRouting();
            app.UseEndpoints(routes =>
            {
                routes.MapControllerRoute(
                    name: "Mystique",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                routes.MapControllerRoute(
                    name: "Plugins",
                    pattern: "Plugins/{area}/{controller=Home}/{action=Index}/{id?}");
            });

            return app;
        }
    }
}
