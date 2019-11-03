using Microsoft.AspNetCore.Mvc;
using Mystique.Core.DomainModel;
using Mystique.Core.Interfaces;
using Mystique.Core.Mvc.Extensions;
using Mystique.Mvc.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Mystique.Controllers
{
    public class PluginsController : Controller
    {
        private readonly IReferenceContainer referenceContainer;
        private readonly PluginPackage pluginPackage;
        private readonly IPluginManager pluginManager;

        public PluginsController(IReferenceContainer referenceContainer, PluginPackage pluginPackage, IPluginManager pluginManager)
        {
            this.referenceContainer = referenceContainer;
            this.pluginPackage = pluginPackage;
            this.pluginManager = pluginManager;
        }

        [HttpGet]
        public IActionResult RefreshControllerAction()
        {
            MystiqueActionDescriptorChangeProvider.Instance.HasChanged = true;
            MystiqueActionDescriptorChangeProvider.Instance.TokenSource.Cancel();
            return Ok();
        }

        [HttpGet]
        public IActionResult Assemblies()
        {
            var items = referenceContainer.GetAll();
            return View(items);
        }

        [HttpGet("Index")]
        public async Task<IActionResult> IndexAsync()
        {
            var plugins = await pluginManager.GetAllPluginsAsync();
            return View(plugins);
        }

        [HttpGet]
        public IActionResult Add() => View();

        [HttpPost("Upload")]
        public async Task<IActionResult> UploadAsync()
        {
            using var stream = Request.GetPluginStream();
            await pluginPackage.InitializeAsync(stream);
            await pluginManager.AddPluginsAsync(pluginPackage);
            return RedirectToAction("Index");
        }

        [HttpGet("Enable")]
        public async Task<IActionResult> EnableAsync(string name)
        {
            await pluginManager.EnablePluginAsync(name);
            return RedirectToAction("Index");
        }

        [HttpGet("Disable")]
        public async Task<IActionResult> DisableAsync(string name)
        {
            await pluginManager.DisablePluginAsync(name);
            return RedirectToAction("Index");
        }

        [HttpGet("Delete")]
        public async Task<IActionResult> DeleteAsync(string name)
        {
            await pluginManager.RemovePluginAsync(name);
            return RedirectToAction("Index");
        }
    }
}
