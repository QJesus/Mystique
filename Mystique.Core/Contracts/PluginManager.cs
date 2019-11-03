using Mystique.Core.DomainModel;
using Mystique.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mystique.Core.Contracts
{
    public class PluginManager : IPluginManager
    {
        private readonly IMvcPluginSetup mvcModuleSetup;

        public PluginManager(IMvcPluginSetup mvcModuleSetup)
        {
            this.mvcModuleSetup = mvcModuleSetup;
        }

        public async Task AddPluginsAsync(PluginPackage pluginPackage)
        {
            if (pluginPackage is null)
            {
                throw new ArgumentNullException(nameof(pluginPackage));
            }

            pluginPackage.SetupFolder();
            await mvcModuleSetup.EnablePluginAsync(pluginPackage.Configuration);
        }

        public async Task<List<PluginModel>> GetAllPluginsAsync() => await mvcModuleSetup.GetPluginsAsync();

        public async Task DisablePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }
            await mvcModuleSetup.DisablePluginAsync(pluginName);
        }

        public async Task EnablePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }
            await mvcModuleSetup.EnablePluginAsync(pluginName);
        }

        public Task<PluginModel> GetPluginAsync(string pluginName)
        {
            throw new NotImplementedException();
        }

        public async Task RemovePluginAsync(string pluginName)
        {
            if (pluginName is null)
            {
                throw new ArgumentNullException(nameof(pluginName));
            }

            await mvcModuleSetup.RemovePluginAsync(pluginName);
        }
    }
}
