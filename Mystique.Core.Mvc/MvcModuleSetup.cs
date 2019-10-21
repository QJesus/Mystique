﻿using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Mystique.Core.Contracts;
using Mystique.Core.Helpers;
using Mystique.Core.Mvc.Infrastructure;
using Mystique.Mvc.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mystique.Core.Mvc
{
    public class MvcModuleSetup : IMvcModuleSetup
    {
        private readonly ApplicationPartManager partManager;
        private readonly IReferenceLoader referenceLoader;

        public MvcModuleSetup(ApplicationPartManager partManager, IReferenceLoader referenceLoader)
        {
            this.partManager = partManager;
            this.referenceLoader = referenceLoader;
        }

        public async Task EnableModuleAsync(string moduleName)
        {
            if (PluginsLoadContexts.Any(moduleName))
            {
                var context = PluginsLoadContexts.GetContext(moduleName);
                var controllerAssemblyPart = new MystiqueAssemblyPart(context.Assemblies.First());
                partManager.ApplicationParts.Add(controllerAssemblyPart);
            }
            else
            {
                var context = new CollectibleAssemblyLoadContext();

                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", moduleName, $"{moduleName}.dll");
                var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", moduleName, $"{moduleName}.deps.json");
                var referenceFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", moduleName);
                using var fs = new FileStream(filePath, FileMode.Open);
                var assembly = context.LoadFromStream(fs);

                referenceLoader.LoadStreamsIntoContext(context, referenceFolderPath, assembly, jsonPath);

                var controllerAssemblyPart = new MystiqueAssemblyPart(assembly);

                AdditionalReferencePathHolder.AdditionalReferencePaths.Add(filePath);
                partManager.ApplicationParts.Add(controllerAssemblyPart);
                PluginsLoadContexts.AddPluginContext(moduleName, context);
            }

            await ResetControllerActionsAsync();
        }

        public async Task DisableModuleAsync(string moduleName)
        {
            var find = partManager.ApplicationParts.First(p => p.Name == moduleName);
            if (find != null)
            {
                partManager.ApplicationParts.Remove(find);
                await ResetControllerActionsAsync();
            }
        }

        public async Task DeleteModuleAsync(string moduleName)
        {
            PluginsLoadContexts.RemovePluginContext(moduleName);

            await Task.Run(() =>
            {
                var directory = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", moduleName));
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
