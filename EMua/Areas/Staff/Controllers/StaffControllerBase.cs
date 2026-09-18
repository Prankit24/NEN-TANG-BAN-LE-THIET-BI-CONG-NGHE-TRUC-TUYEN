using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMua.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Roles = "NhanVien,Nhân viên,Admin,Quản trị viên")]
public abstract class StaffControllerBase : Controller
{
}
