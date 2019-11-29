using DemoReferenceLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace DemoPlugin1.Controllers
{
    public abstract class ApiBaseController : Controller
    {
        protected readonly IConfiguration configuration;
        public ApiBaseController()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", nameof(DemoPlugin1)))
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
            configuration = builder.Build();
        }
    }

    [Area("DemoPlugin1")]
    public class Plugin1Controller : ApiBaseController
    {
        private readonly IConfiguration rootConfig;

        public Plugin1Controller(IConfiguration rootConfig)
        {
            this.rootConfig = rootConfig;
        }

        [HttpGet]
        public IActionResult Ping() => Content(configuration.GetSection("Name").Get<string>() ?? "pong");

        [HttpGet]
        public IActionResult HelloWorld()
        {
            var content = new Demo().SayHello();
            ViewBag.Content = content;
            return View();
        }

        [HttpGet]
        public IActionResult Root()
        {
            var host = rootConfig.GetSection("FtpClientOption:Host").Get<string>();
            return Content(host);
        }
    }
}
