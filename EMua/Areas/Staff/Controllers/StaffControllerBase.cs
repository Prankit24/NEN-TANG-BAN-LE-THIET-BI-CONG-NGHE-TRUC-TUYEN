using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EMua.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Roles = "NhanVien,Nhân viên,Staff,Admin,Quản trị viên,QuanTriVien")]
public abstract class StaffControllerBase : Controller
{
}
