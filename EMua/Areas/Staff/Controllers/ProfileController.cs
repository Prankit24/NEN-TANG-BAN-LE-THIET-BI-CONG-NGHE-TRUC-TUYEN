using Microsoft.AspNetCore.Mvc;

namespace EMua.Areas.Staff.Controllers;

public class ProfileController : StaffControllerBase
{
    public IActionResult Index() => RedirectToAction("Profile", "Account", new { area = "" });
}
