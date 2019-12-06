using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Mystique.Controllers
{
    public class PluginsController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly PluginManager pluginManager;

        public PluginsController(IConfiguration configuration, PluginManager pluginManager)
        {
            this.configuration = configuration;
            this.pluginManager = pluginManager;
        }

        [HttpGet("Index")]
        public IActionResult Index()
        {
            var ps = pluginManager.PluginInfos;
            return View(ps);
        }

        [HttpGet]
        public IActionResult Add() => View();

        [HttpPost("Upload")]
        [DisableRequestSizeLimit]
        public IActionResult Upload()
        {
            var zipPackage = Request?.Form?.Files.FirstOrDefault();
            if (zipPackage == null)
            {
                throw new Exception("The plugin package is missing.");
            }

            using var zipStream = zipPackage.OpenReadStream();
            pluginManager.AddPlugin(zipStream, zipPackage.FileName);

            return RedirectToAction("Index");
        }

        [HttpGet("Enable")]
        public IActionResult Enable(string name, string version)
        {
            pluginManager.EnablePlugin(name, version);
            return RedirectToAction("Index");
        }

        [HttpGet("Disable")]
        public IActionResult Disable(string name, string version)
        {
            pluginManager.DisablePlugin(name, version);
            return RedirectToAction("Index");
        }

        [HttpGet("Delete")]
        public IActionResult Delete(string name, string version)
        {
            pluginManager.DeletePlugin(name, version);
            return RedirectToAction("Index");
        }
    }

}

namespace Mystique
{
    public class OcelotRootObject
    {
        public List<ReRoute> ReRoutes { get; set; }
    }

    public class ReRoute
    {
        public string DownstreamPathTemplate { get; set; }
        public string DownstreamScheme { get; set; }
        public List<Downstreamhostandport> DownstreamHostAndPorts { get; set; }
        public string UpstreamPathTemplate { get; set; }
        public List<string> UpstreamHttpMethod { get; set; }
    }

    public class Downstreamhostandport
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public class PluginManager
    {
        private static readonly object lock_obj = new object();

        private readonly IConfiguration configuration;
        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly IMemoryCache memoryCache;

        public PluginInfo[] PluginInfos
        {
            get
            {
                // https://stackoverflow.com/questions/45597057/how-to-retrieve-a-list-of-memory-cache-keys-in-asp-net-core
                var field = typeof(MemoryCache).GetProperty("EntriesCollection", BindingFlags.NonPublic | BindingFlags.Instance);
                var items = new List<object>();
                if (field.GetValue(memoryCache) is ICollection collection)
                {
                    foreach (var item in collection)
                    {
                        var methodInfo = item.GetType().GetProperty("Key");
                        var val = methodInfo.GetValue(item);
                        items.Add(val);
                    }
                }
                return items.Select(key => memoryCache.Get<PluginInfo>(key)).ToArray();
            }
        }

        public PluginManager(IConfiguration configuration, IWebHostEnvironment webHostEnvironment, IMemoryCache memoryCache)
        {
            this.configuration = configuration;
            this.webHostEnvironment = webHostEnvironment;
            this.memoryCache = memoryCache;
        }

        public bool IsValidZip(string zip)
        {
            var match = Regex.Match(Path.GetFileName(zip), configuration.GetSection("PluginRegularRegex").Value);
            //  TODO 校验 zip 格式正确性
            return match.Success;
        }

        public void AddPlugin(Stream zipStream, string zip, bool autoRun = false)
        {
            var match = Regex.Match(Path.GetFileName(zip), configuration.GetSection("PluginRegularRegex").Value);
            if (!match.Success)
            {
                throw new ArgumentException($"{zip} 插件命名格式错误，");
            }

            var siteName = match.Groups[1].Value;
            var version = match.Groups[2].Value;
            var path = Extract(zipStream, siteName);
            Install(path, siteName, version);
            var portStr = File.ReadAllText(Path.Combine(webHostEnvironment.ContentRootPath, "pids", siteName));
            UpdateGateway(siteName, int.Parse(portStr));

            memoryCache.Set(siteName, new PluginInfo
            {
                Name = siteName,
                Port = int.Parse(portStr),
                Version = version,
                State = "updated",
            });

            if (autoRun)
            {
                EnablePlugin(siteName, version);
            }
        }

        /// <summary>
        ///     解压到临时目录
        /// </summary>
        private string Extract(Stream zipStream, string siteName)
        {
            var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            zipStream.Position = 0;
            var tempPath = Path.Combine(webHostEnvironment.ContentRootPath, "host_plugins", $"{siteName}_{DateTime.Now.Ticks}");
            archive.ExtractToDirectory(tempPath, true);
            return tempPath;
        }

        private void Install(string path, string siteName, string version)
        {
            var dll = Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dll == null)
            {
                throw new FileNotFoundException("未找到可执行程序 *.dll");
            }

            File.Copy("plugin_service.sh", Path.Combine(Path.GetDirectoryName(dll), "plugin_service.sh"));

            var source = Path.GetDirectoryName(dll);
            var folder = new DirectoryInfo(source).Name;
            ExecutePluginSevice("add", siteName, version, source, folder);

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {

            }
        }

        private void ExecutePluginSevice(params string[] arguments)
        {
            Process process = new Process();
            process.StartInfo.WorkingDirectory = webHostEnvironment.ContentRootPath;
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = $"plugin_service.sh {string.Join(" ", arguments)}";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ErrorDialog = true;
            process.StartInfo.UseShellExecute = true;
            //process.StartInfo.RedirectStandardError = true;
            //process.StartInfo.RedirectStandardInput = true;
            //process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            process.WaitForExit();
        }

        private void UpdateGateway(string siteName, int port)
        {
            lock (lock_obj)
            {
                var modified = false;
                var ocelot = Path.Combine(webHostEnvironment.ContentRootPath, "ocelot.json");
                var oro = JObject.Parse(File.Exists(ocelot) ? File.ReadAllText(ocelot, Encoding.UTF8) : "{}").ToObject<OcelotRootObject>();
                var route = oro.ReRoutes.FirstOrDefault(f => f.UpstreamPathTemplate == $"/{siteName}/{{url}}");
                if (route == null)
                {
                    oro.ReRoutes.Add(route = new ReRoute
                    {
                        DownstreamPathTemplate = "/{url}",
                        DownstreamScheme = "http",
                        UpstreamHttpMethod = new List<string> { "GET", "HEAD", "POST", "OPTIONS", "PUT", "DELETE", "TRACE", "CONNECT", },
                        UpstreamPathTemplate = $"/{siteName}/{{url}}"
                    });
                    modified = true;
                }
                route.DownstreamHostAndPorts ??= new List<Downstreamhostandport>();

                // 一个站点只有一个路由
                var dhap = route.DownstreamHostAndPorts.FirstOrDefault(x => x.Port == port);
                if (dhap == null)
                {
                    route.DownstreamHostAndPorts.Clear();
                    route.DownstreamHostAndPorts.Add(new Downstreamhostandport
                    {
                        Host = "127.0.0.1",
                        Port = port,
                    });
                    modified = true;
                }

                if (modified)
                {
                    File.WriteAllText(ocelot, JsonConvert.SerializeObject(oro, Formatting.Indented), Encoding.UTF8);
                }
            }
        }

        public void EnablePlugin(string siteName, string version)
        {
            ExecutePluginSevice("enable", siteName, version);
            var p = memoryCache.Get<PluginInfo>(siteName);
            p.State = "running";
            memoryCache.Set(siteName, p);
        }

        public void DisablePlugin(string siteName, string version)
        {
            ExecutePluginSevice("disable", siteName, version);
            var p = memoryCache.Get<PluginInfo>(siteName);
            p.State = "stoped";
            memoryCache.Set(siteName, p);
        }

        public void DeletePlugin(string siteName, string version)
        {
            ExecutePluginSevice("remove", siteName, version);
            var p = memoryCache.Get<PluginInfo>(siteName);
            p.State = "deleted";
            memoryCache.Set(siteName, p);
        }
    }

    public class PluginInfo
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public string Version { get; set; }
        /// <summary>
        ///     running, stoped, updated, deleted
        /// </summary>
        public string State { get; set; }
    }
}