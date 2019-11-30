using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace DemoPlugin1.Controllers
{
    /// <summary>
    ///     需要读取本插件配置文件的项目，需要继承于此抽象类
    /// </summary>
    public abstract class ApiBaseController : Controller
    {
        protected static readonly IConfiguration configuration;

        static ApiBaseController()
        {
            var path1 = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "host_plugins", "DemoPlugin1"));
            var path2 = new DirectoryInfo(Environment.CurrentDirectory);

            var builder = new ConfigurationBuilder()
                .SetBasePath(path1.Exists ? path1.FullName : path2.FullName)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            configuration = builder.Build();
        }
    }

    [Area("DemoPlugin1")]
    public class Plugin1Controller : ApiBaseController
    {
        private readonly IConfiguration rootConfig;
        private readonly IWebHostEnvironment webHostEnvironment;

        public Plugin1Controller(IConfiguration rootConfig, IWebHostEnvironment webHostEnvironment)
        {
            this.rootConfig = rootConfig;
            this.webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public IActionResult Ping() => Content(configuration.GetSection("Name").Get<string>() ?? "pong");

        [HttpGet]
        public IActionResult HelloWorld()
        {
            ViewBag.Content = configuration.GetSection("HelloWorld").Get<string>() ?? "undefined";
            return View();
        }

        [HttpGet]
        public IActionResult Root()
        {
            var host = rootConfig.GetSection("FtpClientOption:Host").Get<string>();
            return Content(host);
        }

        [HttpGet]
        public IActionResult dir()
        {
            return Json(new
            {
                host = webHostEnvironment.ContentRootPath,
                env = Environment.CurrentDirectory,
            });
        }

        [HttpGet]
        [HttpDelete]
        public IActionResult write()
        {
            var file = Path.Combine(webHostEnvironment.ContentRootPath, "write.txt");
            if (string.Equals("GET", Request.Method, StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.AppendAllText(file, DateTime.Now.ToString());
                return Ok(Request.Method);
            }
            if (string.Equals("DELETE", Request.Method, StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Delete(file);
                return Ok(Request.Method);
            }
            return BadRequest(Request.Method);
        }
    }
}
