using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class SystemController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var assembly = typeof(SystemController).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Không hiển thị mã commit dài trong giao diện.
        var version = informationalVersion?
            .Split('+')[0];

        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString(3)
                ?? "1.0.0";
        }

        ViewData["ApplicationVersion"] = version;

        return View();
    }
}