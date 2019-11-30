using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mystique.Core.Contracts;
using Mystique.Core.DomainModel;
using Mystique.Core.Interfaces;
using Mystique.Mvc.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mystique.Core.Mvc.Infrastructure
{
    public static class MystiqueStartup
    {
        private static readonly IList<string> presets = new List<string>();

        public static void MystiqueSetup(this IServiceCollection services)
        {
            services.AddOptions();
            services.AddSingleton<IMvcPluginSetup, MvcPluginSetup>();
            services.AddSingleton<IReferenceContainer, DefaultReferenceContainer>();
            services.AddSingleton<IReferenceLoader, DefaultReferenceLoader>();
            services.AddSingleton<IActionDescriptorChangeProvider>(MystiqueActionDescriptorChangeProvider.Instance);
            services.AddSingleton(MystiqueActionDescriptorChangeProvider.Instance);
            services.AddScoped<PluginPackage>();
            services.AddScoped<IPluginManager, PluginManager>();

            var mvcBuilder = services.AddMvc();

            var pluginFolder = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins");
            var plugins_cache = Path.Combine(pluginFolder, "plugins_cache.json");
            if (File.Exists(plugins_cache))
            {
                using var scope = services.BuildServiceProvider().CreateScope();
                var refsLoader = scope.ServiceProvider.GetService<IReferenceLoader>();
                var loggerFactory = scope.ServiceProvider.GetService<ILoggerFactory>();
                var plugins = JsonConvert.DeserializeObject<PluginModel[]>(File.ReadAllText(plugins_cache, System.Text.Encoding.UTF8));
                foreach (var plugin in plugins)
                {
                    var filePath = Path.Combine(pluginFolder, plugin.Name, $"{plugin.Name}.dll");
                    if (!File.Exists(filePath))
                    {
                        continue;
                    }

                    try
                    {
                        var referenceFolderPath = Path.GetDirectoryName(filePath);
                        using (var fs = new FileStream(filePath, FileMode.Open))
                        {
                            var context = plugin.PluginContext = new CollectibleAssemblyLoadContext();
                            var assembly = context.LoadFromStream(fs);
                            refsLoader.LoadStreamsIntoContext(context, referenceFolderPath, assembly);

                            var controllerAssemblyPart = new MystiqueAssemblyPart(assembly);
                            mvcBuilder.PartManager.ApplicationParts.Add(controllerAssemblyPart);
                            PluginsLoadContexts.UpsertPluginContext(plugin);
                        }
                        presets.Add(filePath);
                    }
                    catch (Exception ex)
                    {
                        loggerFactory.CreateLogger<CollectibleAssemblyLoadContext>().LogWarning(new EventId(450), $"加载插件失败 '{plugin}'", ex);
                    }
                }
            }

            mvcBuilder.AddRazorRuntimeCompilation(o =>
            {
                foreach (var item in presets)
                {
                    o.AdditionalReferencePaths.Add(item);
                }

                AdditionalReferencePathHolder.AdditionalReferencePaths = o.AdditionalReferencePaths;
            });

            services.Configure<RazorViewEngineOptions>(o =>
            {
                o.AreaViewLocationFormats.Add("/Modules/{2}/Views/{1}/{0}" + RazorViewEngine.ViewExtension);
                o.AreaViewLocationFormats.Add("/Views/Shared/{0}.cshtml");
            });
        }
    }
}
