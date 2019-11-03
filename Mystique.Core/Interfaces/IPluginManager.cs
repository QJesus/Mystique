using Mystique.Core.DomainModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mystique.Core.Interfaces
{
    public interface IPluginManager
    {
        Task<List<PluginModel>> GetAllPluginsAsync();
        Task<PluginModel> GetPluginAsync(string pluginName);
        Task AddPluginsAsync(PluginPackage pluginPackage);
        Task RemovePluginAsync(string pluginName);
        Task EnablePluginAsync(string pluginName);
        Task DisablePluginAsync(string pluginName);
    }
}
