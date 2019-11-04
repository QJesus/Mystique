using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mystique.Core.Interfaces
{
    public interface IMvcPluginSetup
    {
        Task<List<PluginModel>> GetPluginsAsync(bool all = false);
        Task EnablePluginAsync(PluginModel pluginModel);
        Task EnablePluginAsync(string pluginName);
        Task DisablePluginAsync(string pluginName);
        Task RemovePluginAsync(string pluginName);
    }
}
