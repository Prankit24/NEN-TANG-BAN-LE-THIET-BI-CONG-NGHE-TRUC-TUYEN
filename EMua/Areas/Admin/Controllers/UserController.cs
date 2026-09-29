using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class UserController : Controller
{
    private readonly EMuaDbContext _db;

    // ⚠️ Tên quyền được coi là "Admin" trong bảng PhanQuyen.
    private const string AdminRoleName = "Admin";

    public UserController(EMuaDbContext db)
    {
        _db = db;
    }

    // ================== Helpers ==================

    /// <summary>Mã người dùng đang đăng nhập, lấy từ claim đăng nhập.</summary>
    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("MaNguoiDung");
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    /// <summary>Số quản trị viên đang hoạt động (không tính tài khoản đã khóa).</summary>
    private Task<int> CountActiveAdminsAsync(int? excludeUserId = null)
    {
        return _db.NguoiDungs
            .Where(u => u.MaQuyenNavigation!.TenQuyen == AdminRoleName && u.TrangThai)
            .Where(u => excludeUserId == null || u.MaNguoiDung != excludeUserId)
            .CountAsync();
    }

    /// <summary>Mã hoá mật khẩu bằng SHA-256.</summary>
    private static string HashPassword(string plain)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(bytes);
    }

    private async Task<SelectList> BuildRoleSelectList(int? selected = null)
    {
        var roles = await _db.PhanQuyens.OrderBy(r => r.MaQuyen).ToListAsync();
        return new SelectList(roles, "MaQuyen", "TenQuyen", selected);
    }

    // ================== 1. Danh sách người dùng ==================

    [HttpGet]
    public async Task<IActionResult> Index(string? searchString, int? maQuyen, bool? trangThai, int page = 1)
    {
        const int pageSize = 12;

        var query = _db.NguoiDungs
            .AsNoTracking()
            .Include(u => u.MaQuyenNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            query = query.Where(u =>
                (u.TenNguoiDung != null && u.TenNguoiDung.Contains(searchString)) ||
                (u.Email != null && u.Email.Contains(searchString)) ||
                (u.SoDienThoai != null && u.SoDienThoai.Contains(searchString)) ||
                (u.TenDangNhap != null && u.TenDangNhap.Contains(searchString)));
        }

        if (maQuyen.HasValue)
        {
            query = query.Where(u => u.MaQuyen == maQuyen.Value);
        }

        if (trangThai.HasValue)
        {
            query = query.Where(u => u.TrangThai == trangThai.Value);
        }

        // Thống kê nhanh
        ViewBag.TotalUsers = await _db.NguoiDungs.CountAsync();
        ViewBag.ActiveUsers = await _db.NguoiDungs.CountAsync(u => u.TrangThai);
        ViewBag.LockedUsers = await _db.NguoiDungs.CountAsync(u => !u.TrangThai);
        ViewBag.AdminCount = await CountActiveAdminsAsync();

        int totalItems = await query.CountAsync();
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        // Lấy toàn bộ danh sách để sắp xếp theo Tên (từ cuối chuỗi họ tên) giống danh sách lớp học
        var allUsers = await query.ToListAsync();

        var users = allUsers
            .OrderBy(u =>
            {
                if (string.IsNullOrWhiteSpace(u.TenNguoiDung)) return string.Empty;
                var parts = u.TenNguoiDung.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Lấy từ cuối cùng (tên) để sắp xếp A-Z
                return parts.Length > 0 ? parts[^1] : u.TenNguoiDung;
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.SearchString = searchString;
        ViewBag.SelectedRole = maQuyen;
        ViewBag.SelectedStatus = trangThai;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.DanhSachQuyen = await _db.PhanQuyens.OrderBy(r => r.MaQuyen).ToListAsync();
        ViewBag.CurrentUserId = GetCurrentUserId();

        return View(users);
    }

    // ================== 2 & 3. Thêm tài khoản ==================

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.DanhSachQuyen = await BuildRoleSelectList();
        ViewBag.IsEdit = false;
        return View("Form", new NguoiDung { TrangThai = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NguoiDung model, string passwordPlain)
    {
        if (string.IsNullOrWhiteSpace(passwordPlain))
        {
            ModelState.AddModelError(nameof(passwordPlain), "Vui lòng nhập mật khẩu cho tài khoản mới.");
        }

        if (await _db.NguoiDungs.AnyAsync(u => u.Email == model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "Email này đã được sử dụng.");
        }

        if (await _db.NguoiDungs.AnyAsync(u => u.TenDangNhap == model.TenDangNhap))
        {
            ModelState.AddModelError(nameof(model.TenDangNhap), "Tên đăng nhập đã tồn tại.");
        }

        if (ModelState.IsValid)
        {
            model.MatKhau = HashPassword(passwordPlain);
            model.NgayTao = DateTime.Now;
            model.TrangThai = true;

            _db.NguoiDungs.Add(model);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Thêm tài khoản người dùng thành công!";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.DanhSachQuyen = await BuildRoleSelectList(model.MaQuyen);
        ViewBag.IsEdit = false;
        return View("Form", model);
    }

    // ================== 4. Sửa tài khoản ==================

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.NguoiDungs.FindAsync(id);
        if (user == null) return NotFound();

        ViewBag.DanhSachQuyen = await BuildRoleSelectList(user.MaQuyen);
        ViewBag.IsEdit = true;
        ViewBag.IsSelf = GetCurrentUserId() == id;
        return View("Form", user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, NguoiDung model, string? passwordPlain)
    {
        if (id != model.MaNguoiDung) return NotFound();

        var user = await _db.NguoiDungs.Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == id);
        if (user == null) return NotFound();

        bool isSelf = GetCurrentUserId() == id;
        bool wasAdmin = user.MaQuyenNavigation?.TenQuyen == AdminRoleName;

        if (await _db.NguoiDungs.AnyAsync(u => u.Email == model.Email && u.MaNguoiDung != id))
        {
            ModelState.AddModelError(nameof(model.Email), "Email này đã được sử dụng.");
        }

        if (await _db.NguoiDungs.AnyAsync(u => u.TenDangNhap == model.TenDangNhap && u.MaNguoiDung != id))
        {
            ModelState.AddModelError(nameof(model.TenDangNhap), "Tên đăng nhập đã tồn tại.");
        }

        if (isSelf && model.MaQuyen != user.MaQuyen)
        {
            ModelState.AddModelError(string.Empty, "Bạn không thể tự thay đổi phân quyền của chính tài khoản đang đăng nhập.");
        }

        if (wasAdmin && model.MaQuyen != user.MaQuyen)
        {
            int otherActiveAdmins = await CountActiveAdminsAsync(excludeUserId: id);
            if (otherActiveAdmins == 0)
            {
                ModelState.AddModelError(string.Empty, "Đây là quản trị viên (Admin) cuối cùng của hệ thống, không thể đổi sang quyền khác.");
            }
        }

        if (isSelf && !model.TrangThai)
        {
            ModelState.AddModelError(string.Empty, "Bạn không thể tự khóa tài khoản đang đăng nhập.");
        }

        if (ModelState.IsValid)
        {
            user.TenNguoiDung = model.TenNguoiDung;
            user.Email = model.Email;
            user.TenDangNhap = model.TenDangNhap;
            user.SoDienThoai = model.SoDienThoai;
            user.MaQuyen = model.MaQuyen;
            user.TrangThai = model.TrangThai;

            if (!string.IsNullOrWhiteSpace(passwordPlain))
            {
                user.MatKhau = HashPassword(passwordPlain);
            }

            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật tài khoản thành công!";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.DanhSachQuyen = await BuildRoleSelectList(model.MaQuyen);
        ViewBag.IsEdit = true;
        ViewBag.IsSelf = isSelf;
        return View("Form", model);
    }

    // ================== 5. Khóa / Mở khóa nhanh ==================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var user = await _db.NguoiDungs.Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == id);

        if (user == null) return NotFound();

        if (GetCurrentUserId() == id)
        {
            TempData["ErrorMessage"] = "Bạn không thể tự khóa tài khoản đang đăng nhập.";
            return RedirectToAction(nameof(Index));
        }

        bool isTryingToLock = user.TrangThai;
        bool isAdmin = user.MaQuyenNavigation?.TenQuyen == AdminRoleName;

        if (isTryingToLock && isAdmin)
        {
            int otherActiveAdmins = await CountActiveAdminsAsync(excludeUserId: id);
            if (otherActiveAdmins == 0)
            {
                TempData["ErrorMessage"] = "Đây là quản trị viên (Admin) cuối cùng, không thể khóa tài khoản này.";
                return RedirectToAction(nameof(Index));
            }
        }

        user.TrangThai = !user.TrangThai;
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Đã {(user.TrangThai ? "mở khóa" : "khóa")} tài khoản #{id} thành công!";
        return RedirectToAction(nameof(Index));
    }
}