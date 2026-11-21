using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class SystemController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IPasswordHasher<NguoiDung> _passwordHasher;

    public SystemController(EMuaDbContext db, IPasswordHasher<NguoiDung> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

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

    // =========================================================
    // XÁC THỰC MẬT KHẨU & CHUYỂN SANG GIAO DIỆN NHÂN VIÊN
    // =========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchToStaffMode(string? password)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập mật khẩu để xác thực chuyển đổi giao diện.";
            return Redirect(Request.Headers.Referer.ToString() ?? Url.Action("Index", "Dashboard", new { area = "Admin" })!);
        }

        var user = await _db.NguoiDungs.AsNoTracking().FirstOrDefaultAsync(u => u.MaNguoiDung == userId);
        if (user == null || string.IsNullOrWhiteSpace(user.MatKhau))
        {
            TempData["ErrorMessage"] = "Không tìm thấy thông tin tài khoản hoặc tài khoản chưa thiết lập mật khẩu.";
            return Redirect(Request.Headers.Referer.ToString() ?? Url.Action("Index", "Dashboard", new { area = "Admin" })!);
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.MatKhau, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            TempData["ErrorMessage"] = "Mật khẩu xác thực không chính xác. Chuyển đổi giao diện bị từ chối.";
            return Redirect(Request.Headers.Referer.ToString() ?? Url.Action("Index", "Dashboard", new { area = "Admin" })!);
        }

        // Lưu trạng thái xem giao diện Nhân viên vào Session máy chủ
        HttpContext.Session.SetString("Admin_ViewMode", "Staff");
        TempData["SuccessMessage"] = "Xác thực thành công. Bạn đang ở chế độ xem giao diện Nhân viên.";

        return RedirectToAction("Index", "Dashboard", new { area = "Staff" });
    }

    // =========================================================
    // QUAY LẠI GIAO DIỆN QUẢN TRỊ VIÊN (ADMIN)
    // =========================================================
    [HttpGet]
    [HttpPost]
    public IActionResult SwitchToAdminMode()
    {
        HttpContext.Session.Remove("Admin_ViewMode");
        TempData["SuccessMessage"] = "Đã quay trở lại giao diện Quản trị viên.";
        return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
    }
}
