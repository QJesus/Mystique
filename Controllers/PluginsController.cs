using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Mystique.Controllers
{
    public class PluginsController : Controller
    {

        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly PluginManager pluginManager;

        public PluginsController(IWebHostEnvironment webHostEnvironment, PluginManager pluginManager)
        {
            this.webHostEnvironment = webHostEnvironment;
            this.pluginManager = pluginManager;
        }

        [HttpGet]
        public IActionResult Assemblies()
        {
            return View(new List<object>());
        }

        [HttpGet("Index")]
        public IActionResult Index()
        {
            return View(new List<object>());
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
            var siteName = Path.GetFileNameWithoutExtension(zipPackage.FileName);
            pluginManager.AddPlugin(zipStream, siteName, "20191205");

            return RedirectToAction("Index");
        }

        [HttpGet("Enable")]
        public IActionResult Enable(string name)
        {
            // ExecuteShell(sitePath, "dotnet", $"{siteName}.dll");
            return RedirectToAction("Index");
        }

        [HttpGet("Disable")]
        public IActionResult Disable(string name)
        {
            return RedirectToAction("Index");
        }

        [HttpGet("Delete")]
        public IActionResult Delete(string name)
        {
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

        public PluginManager(IConfiguration configuration, IWebHostEnvironment webHostEnvironment, IMemoryCache memoryCache)
        {
            this.configuration = configuration;
            this.webHostEnvironment = webHostEnvironment;
            this.memoryCache = memoryCache;
        }

        public void AddPlugin(Stream zipStream, string siteName, string version)
        {
            var path = Extract(zipStream, siteName);
            Install(path, siteName, version);
            var portStr = File.ReadAllText(Path.Combine(webHostEnvironment.ContentRootPath, "pids", siteName));
            UpdateGateway(siteName, int.Parse(portStr));
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

            File.Copy("plugin_sevice.sh", Path.Combine(Path.GetDirectoryName(dll), "plugin_sevice.sh"));

            var source = Path.GetDirectoryName(dll);
            var folder = new DirectoryInfo(source).Name;
            Process process = new Process();
            process.StartInfo.WorkingDirectory = webHostEnvironment.ContentRootPath;
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = $"plugin_sevice.sh add {siteName} {version} {source} {folder}";
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

                var dhap = route.DownstreamHostAndPorts.FirstOrDefault(x => x.Port == port);
                if (dhap == null)
                {
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
    }

    public class PluginInfo
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public string Version { get; set; }
    }
}