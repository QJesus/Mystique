using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Mystique.Core.Interfaces;
using Mystique.Core.Mvc.Infrastructure;
using Mystique.Mvc.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mystique.Core.Mvc
{
    public class MvcPluginSetup : IMvcPluginSetup
    {
        private readonly ApplicationPartManager applicationPartManager;
        private readonly IReferenceLoader referenceLoader;

        public MvcPluginSetup(ApplicationPartManager applicationPartManager, IReferenceLoader referenceLoader)
        {
            this.applicationPartManager = applicationPartManager;
            this.referenceLoader = referenceLoader;
        }

        public async Task<List<PluginModel>> GetPluginsAsync() => await Task.FromResult(PluginsLoadContexts.GetPlugins());

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
                foreach (var controllerAssemblyPart in context.Assemblies.Select(o => new MystiqueAssemblyPart(o)))
                {
                    applicationPartManager.ApplicationParts.Add(controllerAssemblyPart);
                }
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
        }

        public async Task DisablePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }

            var parts = applicationPartManager.ApplicationParts.Where(o => o.Name == pluginName).ToArray();
            foreach (var part in parts)
            {
                applicationPartManager.ApplicationParts.Remove(part);
            }
            if (parts.Any())
            {
                await ResetControllerActionsAsync();

                var pluginModel = PluginsLoadContexts.GetPlugin(pluginName);
                pluginModel.IsEnabled = false;
                PluginsLoadContexts.UpsertPluginContext(pluginModel);
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
