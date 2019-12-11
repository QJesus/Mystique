using log4net;
using log4net.Config;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Text;

namespace Mystique
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var xml = new FileInfo(Path.Combine(Environment.CurrentDirectory, "log4net.conf.xml"));
            XmlConfigurator.Configure(LogManager.CreateRepository(".NETCoreRepository"), xml);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);  // 支持 GB2312 和 GBK 编码
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, builder) => builder
                        .SetBasePath(hostingContext.HostingEnvironment.ContentRootPath)
                        .AddJsonFile("ocelot.json", optional: true, reloadOnChange: true))
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }
}
