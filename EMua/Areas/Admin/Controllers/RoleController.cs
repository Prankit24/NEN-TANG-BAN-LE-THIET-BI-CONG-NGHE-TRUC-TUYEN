using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class RoleController : Controller
{
    private readonly EMuaDbContext _db;
    private const string AdminRoleName = "Admin"; // Tên nhóm quyền tối cao được bảo vệ

    public RoleController(EMuaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var roles = await _db.PhanQuyens
            .AsNoTracking()
            .Include(r => r.NguoiDungs)
            .OrderBy(r => r.MaQuyen)
            .ToListAsync();

        ViewBag.TotalRoles = roles.Count;
        ViewBag.TotalUsers = roles.Sum(r => r.NguoiDungs?.Count ?? 0);
        ViewBag.AdminRoleName = AdminRoleName;

        return View(roles);
    }

    // THÊM NHÓM QUYỀN
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string tenQuyen)
    {
        if (string.IsNullOrWhiteSpace(tenQuyen))
        {
            TempData["ErrorMessage"] = "Tên nhóm quyền không được để trống!";
            return RedirectToAction(nameof(Index));
        }

        bool exists = await _db.PhanQuyens.AnyAsync(r => r.TenQuyen.ToLower() == tenQuyen.Trim().ToLower());
        if (exists)
        {
            TempData["ErrorMessage"] = $"Nhóm quyền '{tenQuyen}' đã tồn tại trong hệ thống!";
            return RedirectToAction(nameof(Index));
        }

        var newRole = new PhanQuyen { TenQuyen = tenQuyen.Trim() };
        _db.PhanQuyens.Add(newRole);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Đã thêm nhóm quyền '{tenQuyen}' thành công!";
        return RedirectToAction(nameof(Index));
    }

    // SỬA NHÓM QUYỀN
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int maQuyen, string tenQuyen)
    {
        var role = await _db.PhanQuyens.FindAsync(maQuyen);
        if (role == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy nhóm quyền cần sửa!";
            return RedirectToAction(nameof(Index));
        }

        // Chặn không cho sửa quyền Admin hệ thống
        if (role.TenQuyen.Equals(AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = $"Không được phép chỉnh sửa nhóm quyền bảo vệ '{AdminRoleName}'!";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(tenQuyen))
        {
            TempData["ErrorMessage"] = "Tên nhóm quyền mới không hợp lệ!";
            return RedirectToAction(nameof(Index));
        }

        role.TenQuyen = tenQuyen.Trim();
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Cập nhật tên nhóm quyền thành công!";
        return RedirectToAction(nameof(Index));
    }

    // XÓA NHÓM QUYỀN
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int maQuyen)
    {
        var role = await _db.PhanQuyens
            .Include(r => r.NguoiDungs)
            .FirstOrDefaultAsync(r => r.MaQuyen == maQuyen);

        if (role == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy nhóm quyền!";
            return RedirectToAction(nameof(Index));
        }

        // Chặn không cho xóa quyền Admin
        if (role.TenQuyen.Equals(AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = $"Không thể xóa nhóm quyền hệ thống '{AdminRoleName}'!";
            return RedirectToAction(nameof(Index));
        }

        // Kiểm tra xem còn tài khoản nào đang gắn với quyền này không
        if (role.NguoiDungs != null && role.NguoiDungs.Any())
        {
            TempData["ErrorMessage"] = $"Không thể xóa nhóm quyền '{role.TenQuyen}' vì vẫn còn tài khoản đang sử dụng!";
            return RedirectToAction(nameof(Index));
        }

        _db.PhanQuyens.Remove(role);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Đã xóa nhóm quyền '{role.TenQuyen}' thành công!";
        return RedirectToAction(nameof(Index));
    }
}