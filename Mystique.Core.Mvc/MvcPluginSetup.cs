using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Mystique.Core.Interfaces;
using Mystique.Core.Mvc.Infrastructure;
using Mystique.Mvc.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Mystique.Core.Mvc
{
    public class MvcPluginSetup : IMvcPluginSetup
    {
        private readonly ApplicationPartManager applicationPartManager;
        private readonly IReferenceLoader referenceLoader;
        private readonly HttpClient httpClient;

        public MvcPluginSetup(ApplicationPartManager applicationPartManager, IReferenceLoader referenceLoader, IHttpClientFactory httpClientFactory)
        {
            this.applicationPartManager = applicationPartManager;
            this.referenceLoader = referenceLoader;
            httpClient = httpClientFactory.CreateClient("internal-client");
        }

        public async Task<List<PluginModel>> GetPluginsAsync(bool all = false) => await Task.FromResult(PluginsLoadContexts.GetPlugins(all));

        public async Task EnablePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }

            var pluginModel = PluginsLoadContexts.GetPlugin(pluginName);
            await EnablePluginAsync(pluginModel);
        }

        public async Task EnablePluginAsync(PluginModel pluginModel)
        {
            if (pluginModel is null)
            {
                throw new ArgumentNullException(nameof(pluginModel));
            }

            var pluginName = pluginModel.Name;
            if (PluginsLoadContexts.Any(pluginName))
            {
                pluginModel = PluginsLoadContexts.GetPlugin(pluginName);
                var context = pluginModel.PluginContext;
                var controllerAssemblyPart = new MystiqueAssemblyPart(context.Assemblies.First());
                applicationPartManager.ApplicationParts.Add(controllerAssemblyPart);
            }
            else
            {
                var context = pluginModel.PluginContext = new CollectibleAssemblyLoadContext();

                var filePath = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", pluginName, $"{pluginName}.dll");
                var referenceFolderPath = Path.GetDirectoryName(filePath);
                using (var fs = new FileStream(filePath, FileMode.Open))
                {
                    var assembly = context.LoadFromStream(fs);
                    referenceLoader.LoadStreamsIntoContext(context, referenceFolderPath, assembly);

                    var controllerAssemblyPart = new MystiqueAssemblyPart(assembly);

                    AdditionalReferencePathHolder.AdditionalReferencePaths.Add(filePath);
                    applicationPartManager.ApplicationParts.Add(controllerAssemblyPart);
                }
            }
            pluginModel.IsEnabled = true;
            PluginsLoadContexts.UpsertPluginContext(pluginModel);
            await ResetControllerActionsAsync();

            await RunConnectMethods(pluginName);
            Wwwroot(pluginName, true);
        }

        private async Task RunConnectMethods(string pluginName)
        {
            var filePath = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", pluginName, "appsettings.json");
            if (!File.Exists(filePath))
            {
                return;
            }
            var methods = JObject.Parse(File.ReadAllText(filePath, Encoding.UTF8))["GET_Connect"] as JArray;
            foreach (var method in methods)
            {
                await httpClient.GetAsync(method.ToObject<string>());
            }
        }

        private void Wwwroot(string pluginName, bool enable)
        {
            var srcFolder = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", pluginName, "wwwroot"));
            var destFolder = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "wwwroot", pluginName));
            if (enable)
            {
                // 启用，从插件拷贝到宿主
                if (destFolder.Exists)
                {
                    destFolder.Delete(true);
                }
                srcFolder.MoveTo(destFolder.FullName);
            }
            else
            {
                // 禁用，从宿主拷贝到插件
                if (srcFolder.Exists)
                {
                    srcFolder.Delete(true);
                }
                destFolder.MoveTo(srcFolder.FullName);
            }
        }

        public async Task DisablePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }

            await RunDisconnectMehods(pluginName);
            Wwwroot(pluginName, false);

            var parts = applicationPartManager.ApplicationParts.Where(o => o.Name == pluginName).ToArray();
            foreach (var part in parts)
            {
                applicationPartManager.ApplicationParts.Remove(part);
            }
            if (parts.Any())
            {
                await ResetControllerActionsAsync();
            }

            var pluginModel = PluginsLoadContexts.GetPlugin(pluginName);
            if (pluginModel != null)
            {
                pluginModel.IsEnabled = false;
                PluginsLoadContexts.UpsertPluginContext(pluginModel);
            }
        }

        public async Task RunDisconnectMehods(string pluginName)
        {
            var filePath = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", pluginName, "appsettings.json");
            if (!File.Exists(filePath))
            {
                return;
            }
            var methods = JObject.Parse(File.ReadAllText(filePath, Encoding.UTF8))["DELETE_Disconnect"] as JArray;
            foreach (var method in methods)
            {
                await httpClient.DeleteAsync(method.ToObject<string>());
            }
        }

        public async Task RemovePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }

            await DisablePluginAsync(pluginName);
            PluginsLoadContexts.RemovePluginContext(pluginName);

            await Task.Run(() =>
            {
                var directory = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", pluginName));
                if (directory.Exists)
                {
                    directory.Delete(true);
                }
            });
        }

        private Task ResetControllerActionsAsync()
        {
            return Task.Run(() =>
            {
                MystiqueActionDescriptorChangeProvider.Instance.HasChanged = true;
                MystiqueActionDescriptorChangeProvider.Instance.TokenSource.Cancel();
            });
        }
    }
}
