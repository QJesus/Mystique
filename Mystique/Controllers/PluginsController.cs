using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Mystique.Controllers
{
    public class PluginsController : Controller
    {
        private static readonly object lock_obj = new object();

        private readonly IWebHostEnvironment webHostEnvironment;

        public PluginsController(IWebHostEnvironment webHostEnvironment)
        {
            this.webHostEnvironment = webHostEnvironment;
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
            var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            zipStream.Position = 0;

            var siteName = Path.GetFileNameWithoutExtension(zipPackage.FileName);

            // 1. 解压到临时文件夹
            var tempSitePath = Path.Combine(webHostEnvironment.ContentRootPath, "host_plugins", $"{siteName}_{DateTime.Now.Ticks}");
            archive.ExtractToDirectory(tempSitePath, true);

            // 2. 检查文件是否有缺失
            if (!System.IO.File.Exists(Path.Combine(tempSitePath, $"{siteName}.dll")))
            {
                throw new FileNotFoundException($"未从 {zipPackage.FileName} 中找到可执行程序 {siteName}.dll");
            }
            if (!System.IO.File.Exists(Path.Combine(tempSitePath, "appsettings.json")))
            {
                throw new FileNotFoundException($"未从 {zipPackage.FileName} 中找到配置文件 appsettings.json");
            }

            // 3. 检查配置是否有缺失
            var builder = new ConfigurationBuilder()
                .SetBasePath(tempSitePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            var configuration = builder.Build();
            var kestrel = configuration.GetSection("Kestrel:Endpoints:Http:Url").Get<string>().Replace("*", "127.0.0.1");
            if (!Uri.TryCreate(kestrel, UriKind.RelativeOrAbsolute, out var uri))
            {
                throw new ArgumentException($"{zipPackage.FileName} 未在 appsettings.json 中指定端口号。节点 Kestrel:Endpoints:Http:Url");
            }

            // 3. 结束旧站点
            var sitePath = Path.Combine(webHostEnvironment.ContentRootPath, "host_plugins", siteName);
            var pIdPath = Path.Combine(webHostEnvironment.ContentRootPath, "host_pids", $"{siteName}.{uri.Port}");
            if (!Directory.Exists(Path.GetDirectoryName(pIdPath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pIdPath));
            }
            var pIdStr = (System.IO.File.Exists(pIdPath) ? System.IO.File.ReadAllText(pIdPath, System.Text.Encoding.UTF8) : string.Empty).Trim();
            if (int.TryParse(pIdStr, out var pId))
            {
                try
                {
                    var ps = Process.GetProcessById(pId);
                    if (ps?.HasExited == false)
                    {
                        ps.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            // 4. 覆盖文件
            if (Directory.Exists(sitePath))
            {
                Directory.Delete(sitePath, true);
            }
            Directory.Move(tempSitePath, sitePath);

            // 5. 启动新站点 
            Process process = new Process();
            process.StartInfo.WorkingDirectory = sitePath;
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = $"{siteName}.dll";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ErrorDialog = true;
            process.StartInfo.UseShellExecute = true;
            //process.StartInfo.RedirectStandardError = true;
            //process.StartInfo.RedirectStandardInput = true;
            //process.StartInfo.RedirectStandardOutput = true;
            process.Start();

            System.IO.File.WriteAllText(pIdPath, process.Id.ToString(), System.Text.Encoding.UTF8);

            // 6. 更新 API 网关
            lock (lock_obj)
            {
                var modified = false;
                var ocelot = Path.Combine(webHostEnvironment.ContentRootPath, "ocelot.json");
                var oro = JObject.Parse(System.IO.File.ReadAllText(ocelot)).ToObject<OcelotRootObject>();
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

                var dhap = route.DownstreamHostAndPorts.FirstOrDefault(x => x.Port == uri.Port);
                if (dhap == null)
                {
                    route.DownstreamHostAndPorts.Add(new Downstreamhostandport
                    {
                        Host = "127.0.0.1",
                        Port = uri.Port,
                    });
                    modified = true;
                }

                if (modified)
                {
                    System.IO.File.WriteAllText(ocelot, JsonConvert.SerializeObject(oro, Formatting.Indented));
                }
            }

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
