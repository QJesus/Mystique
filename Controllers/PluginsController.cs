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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Mystique.Controllers
{
    public class PluginsController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly PluginManager pluginManager;

        public PluginsController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment, PluginManager pluginManager)
        {
            this.configuration = configuration;
            this.webHostEnvironment = webHostEnvironment;
            this.pluginManager = pluginManager;
        }

        [HttpGet("plugins/details")]
        public IActionResult Get()
        {
            var ps = pluginManager.PluginInfos;
            return Json(ps);
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
        public List<DownstreamHostAndPort> DownstreamHostAndPorts { get; set; }
        public string UpstreamPathTemplate { get; set; }
        public List<string> UpstreamHttpMethod { get; set; }
    }

    public class DownstreamHostAndPort
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public class PluginManager
    {
        private static readonly object lock_obj = new object();

        private static string GetOrCreateDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                lock (lock_obj)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
            }
            return dir;
        }

        private const string sh_name = @"plugin_service.sh";

        private const string systemctl = @"/etc/systemd/system";


        private string Root => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "D:" : "/";
        /// <summary>
        ///     插件的运行目录
        /// </summary>
        private string Eusb => GetOrCreateDir(Path.Combine(Root, "opt", "smt", "eusb_terminal"));
        /// <summary>
        ///     上一个版本的插件，包括服务和软件
        /// </summary>
        private string Dead => GetOrCreateDir(Path.Combine(Eusb, "dead"));
        /// <summary>
        ///     缓存信息
        /// </summary>
        private string Cache => GetOrCreateDir(Path.Combine(Eusb, "caches"));
        /// <summary>
        ///     临时文件目录
        /// </summary>
        private string Temp => GetOrCreateDir(Path.Combine(Eusb, "temp"));
        /// <summary>
        ///     宿主的静态资源目录
        /// </summary>
        private string Wwwroot => Path.Combine(webHostEnvironment.ContentRootPath, "wwwroot");


        private readonly IConfiguration configuration;
        private readonly IWebHostEnvironment webHostEnvironment;
        private readonly IMemoryCache memoryCache;

        public PluginInfo[] PluginInfos
        {
            get
            {
                // https://stackoverflow.com/questions/45597057/how-to-retrieve-a-list-of-memory-cache-keys-in-asp-net-core
                var field = typeof(MemoryCache).GetProperty("EntriesCollection", BindingFlags.NonPublic | BindingFlags.Instance);
                var v = field.GetValue(memoryCache);
                var values = (v.GetType().GetProperty("Keys").GetValue(v) as System.Collections.ObjectModel.ReadOnlyCollection<object>)
                    .Where(k => k is string).Select(k => memoryCache.Get<PluginInfo>(k)).ToArray();
                return values;
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
            if (string.IsNullOrEmpty(zip?.Trim()))
            {
                return false;
            }

            // (\S{1,})\.(\S{1,})\.([\d]{8})\.zip
            var match = Regex.Match(Path.GetFileName(zip), configuration.GetSection("PluginRegularRegex").Value);
            //  TODO 校验 zip 格式正确性
            return match.Success;
        }

        public void AddPlugin(Stream zipStream, string zip, bool autoRun = false)
        {
            if (!IsValidZip(zip))
            {
                throw new ArgumentException($"{zip} 插件命名格式错误，");
            }

            var match = Regex.Match(Path.GetFileName(zip), configuration.GetSection("PluginRegularRegex").Value);
            var siteName = match.Groups[1].Value;
            var platform = match.Groups[2].Value;
            var version = match.Groups[3].Value;

            var path = Extract(zipStream, siteName, platform, version);
            var pi = new PluginInfo { Name = siteName, Version = version, Category = platform, };
            if (string.Equals(platform, "www", StringComparison.OrdinalIgnoreCase))
            {
                // 拷贝到宿主的 wwwroot 中
                var di = new DirectoryInfo(path);
                while (di.GetDirectories().Length == 1 && di.GetFiles().Length == 0)
                {
                    path = di.GetDirectories().First().FullName;
                }
                var dest = new DirectoryInfo(Path.Combine(Wwwroot, siteName));
                if (dest.Exists)
                {
                    MoveDirectory(dest.FullName, Path.Combine(Dead, $"{siteName}.www"));
                }
                MoveDirectory(path, dest.FullName);

                pi.State = "running";
                pi.Path = dest.FullName;
                memoryCache.Set(siteName, pi);
            }
            else
            {
                var target = Install(path, siteName, version);
                var portStr = File.ReadAllText(Path.Combine(webHostEnvironment.ContentRootPath, "pids", siteName));
                UpdateGateway(siteName, int.Parse(portStr));

                pi.Port = int.Parse(portStr);
                pi.State = "updated";
                pi.Path = target;
                memoryCache.Set(siteName, pi);

                if (autoRun)
                {
                    EnablePlugin(siteName, version);
                }
            }
            FlushPluginInfos();
        }

        public void FlushPluginInfos()
        {
            lock (lock_obj)
            {
                var json = JsonConvert.SerializeObject(PluginInfos, Formatting.Indented);
                File.WriteAllText(Path.Combine(Cache, "cache.pis"), json, Encoding.UTF8);
            }
        }

        public void ReloadPluginInfos()
        {
            lock (lock_obj)
            {
                var json = File.Exists(Path.Combine(Cache, "cache.pis")) ? File.ReadAllText(Path.Combine(Cache, "cache.pis"), Encoding.UTF8) : "[]";
                var pis = JsonConvert.DeserializeObject<PluginInfo[]>(json);
                foreach (var pi in pis)
                {
                    memoryCache.Set(pi.Name, pi);
                }
            }
        }

        /// <summary>
        ///     解压到临时目录
        /// </summary>
        private string Extract(Stream zipStream, string siteName, string platform, string version)
        {
            var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            zipStream.Position = 0;
            var tempPath = Path.Combine(Temp, $"{siteName}.{platform}.{version}");
            archive.ExtractToDirectory(tempPath, true);
            return tempPath;
        }

        private string Install(string path, string siteName, string version)
        {
            var dll = Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dll == null)
            {
                throw new FileNotFoundException("未找到可执行程序 *.dll");
            }

            File.Copy(sh_name, Path.Combine(Path.GetDirectoryName(dll), sh_name));

            var source = Path.GetDirectoryName(dll);
            var folder = new DirectoryInfo(source).Name;
            ExecutePluginSevice("add", siteName, version, source, folder);

            try
            {
                Directory.Delete(path, true);
            }
            catch { }

            var target = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(dll), sh_name), Encoding.UTF8)
                .Select(x =>
                {
                    var match = Regex.Match(x, @"target=([\S]+)");
                    return match.Groups[1].Value;
                })
                .FirstOrDefault();
            return Path.Combine(string.IsNullOrEmpty(target) ? "/opt/smt/eusb_terminal" : target, siteName);
        }

        private void ExecutePluginSevice(params string[] arguments)
        {
            Process process = new Process();
            process.StartInfo.WorkingDirectory = webHostEnvironment.ContentRootPath;
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = $"{sh_name} {string.Join(" ", arguments)}";
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
                route.DownstreamHostAndPorts ??= new List<DownstreamHostAndPort>();

                // 一个站点只有一个路由
                var dhap = route.DownstreamHostAndPorts.FirstOrDefault(x => x.Port == port);
                if (dhap == null)
                {
                    route.DownstreamHostAndPorts.Clear();
                    route.DownstreamHostAndPorts.Add(new DownstreamHostAndPort
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
            if (string.IsNullOrEmpty(siteName))
            {
                throw new ArgumentException("message", nameof(siteName));
            }

            if (string.IsNullOrEmpty(version))
            {
                throw new ArgumentException("message", nameof(version));
            }

            var p = memoryCache.Get<PluginInfo>(siteName);
            if (string.Equals(p.Category, "www", StringComparison.OrdinalIgnoreCase))
            {
                var from = Path.Combine(Dead, $"{siteName}.www");
                var to = Path.Combine(Wwwroot, siteName);
                if (Directory.Exists(from))
                {
                    MoveDirectory(from, to);
                }
                p.State = Directory.Exists(to) ? "running" : "deleted";
            }
            else
            {
                ExecutePluginSevice("enable", siteName, version);
                p.State = "running";
            }

            memoryCache.Set(siteName, p);
            FlushPluginInfos();
        }

        public void DisablePlugin(string siteName, string version)
        {
            if (string.IsNullOrEmpty(siteName))
            {
                throw new ArgumentException("message", nameof(siteName));
            }

            if (string.IsNullOrEmpty(version))
            {
                throw new ArgumentException("message", nameof(version));
            }

            var p = memoryCache.Get<PluginInfo>(siteName);
            if (string.Equals(p.Category, "www", StringComparison.OrdinalIgnoreCase))
            {
                var from = Path.Combine(Wwwroot, siteName);
                var to = Path.Combine(Dead, $"{siteName}.www");
                if (Directory.Exists(from))
                {
                    MoveDirectory(from, to);
                }
                p.State = Directory.Exists(from) ? "running" : "stoped";
            }
            else
            {
                ExecutePluginSevice("disable", siteName, version);
                p.State = "stoped";
            }

            memoryCache.Set(siteName, p);

            FlushPluginInfos();
        }

        public void DeletePlugin(string siteName, string version)
        {
            if (string.IsNullOrEmpty(siteName))
            {
                throw new ArgumentException("message", nameof(siteName));
            }

            if (string.IsNullOrEmpty(version))
            {
                throw new ArgumentException("message", nameof(version));
            }

            var p = memoryCache.Get<PluginInfo>(siteName);
            if (string.Equals(p.Category, "www", StringComparison.OrdinalIgnoreCase))
            {
                var from = Path.Combine(Wwwroot, siteName);
                var to = Path.Combine(Dead, $"{siteName}.www");
                if (Directory.Exists(from))
                {
                    MoveDirectory(from, to);
                }
                p.State = Directory.Exists(from) ? "running" : "deleted";
            }
            else
            {
                ExecutePluginSevice("remove", siteName, version);
            }

            p.State = "deleted";
            memoryCache.Set(siteName, p);

            FlushPluginInfos();
        }

        /// <summary>
        ///     移动或重命名一个文件夹（如果存在则合并，而不是出现异常报错）
        /// </summary>
        private static void MoveDirectory(string sourceDirectory, string targetDirectory)
        {
            MoveDirectory(sourceDirectory, targetDirectory, 0);

            void MoveDirectory(string source, string target, int depth)
            {
                if (!Directory.Exists(source))
                {
                    return;
                }

                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }

                var sourceFolder = new DirectoryInfo(source);
                foreach (var fileInfo in sourceFolder.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    var targetFile = Path.Combine(target, fileInfo.Name);

                    if (File.Exists(targetFile))
                    {
                        File.Delete(targetFile);
                    }

                    File.Move(fileInfo.FullName, targetFile);
                }

                foreach (var directoryInfo in sourceFolder.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    var back = string.Join("\\", Enumerable.Repeat("..", depth));
                    MoveDirectory(directoryInfo.FullName, Path.GetFullPath(Path.Combine(target, back, directoryInfo.Name)), depth + 1);
                }

                Directory.Delete(source);
            }
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
        public string Path { get; set; }
        public string Category { get; set; }
    }
}