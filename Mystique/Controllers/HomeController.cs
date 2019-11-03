using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Mystique.Models;
using System.Diagnostics;
using System.Linq;

namespace Mystique.Controllers
{
    public class HomeController : Controller
    {
        private readonly IActionDescriptorCollectionProvider actionDescriptorCollectionProvider;

        public HomeController(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
        {
            this.actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
        }

        public IActionResult Index() => View();

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

        [HttpGet]
        public IActionResult Actions()
        {
            // https://stackoverflow.com/questions/41908957/get-all-registered-routes-in-asp-net-core
            var actions = actionDescriptorCollectionProvider.ActionDescriptors.Items.Select(a => new
            {
                Area = a.RouteValues["area"],
                Action = a.RouteValues["action"],
                Controller = a.RouteValues["controller"],
                Name = a.AttributeRouteInfo?.Name,
                Templates = new string[] { a.AttributeRouteInfo?.Template },
                HttpMethods = a.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(o => o.HttpMethods),
                Parameters = a.Parameters.Select(p => new
                {
                    p.Name,
                    ParameterType = p.ParameterType.Name,
                    ParameterFullType = p.ParameterType.FullName,
                    DefaultValue = p is ControllerParameterDescriptor pd ? pd.ParameterInfo.DefaultValue : null,
                }),
            }); ;
            return Json(actions);
        }
    }
}
