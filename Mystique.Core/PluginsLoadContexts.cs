using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Mystique.Core
{
    public class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext() : base(isCollectible: true) { }
        protected override Assembly Load(AssemblyName name) => null;
    }

    public class PluginModel
    {
        public string Name { get; set; }    // unique Key
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public bool IsEnabled { get; set; }
        public bool AutoEnable { get; set; }
        public string ZipFileName { get; set; }
        public long Size { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime Modified { get; set; }
        public CollectibleAssemblyLoadContext PluginContext { get; set; }
    }

    // 2019/11/03 相同名称的插件，只允许存在一个实例
    public static class PluginsLoadContexts
    {
        private static readonly Dictionary<string, PluginModel> pluginContexts = new Dictionary<string, PluginModel>();

        public static bool Any(string pluginName) => pluginContexts.ContainsKey(pluginName) && !pluginContexts[pluginName].IsDeleted;

        public static void RemovePluginContext(string pluginName)
        {
            if (Any(pluginName))
            {
                var context = pluginContexts[pluginName];
                context.PluginContext.Unload();
                context.IsDeleted = true;
                pluginContexts[pluginName] = context;
            }
        }

        public static PluginModel GetPlugin(string pluginName) => Any(pluginName) ? pluginContexts[pluginName] : null;

        public static List<PluginModel> GetPlugins(bool all = false) => pluginContexts.Keys.Select(k => pluginContexts[k]).Where(k => all || !k.IsDeleted).ToList();

        public static void UpsertPluginContext(PluginModel pluginModel)
        {
            if (pluginModel is null)
            {
                throw new ArgumentNullException(nameof(pluginModel));
            }

            pluginContexts[pluginModel.Name] = pluginModel;
        }
    }
}
