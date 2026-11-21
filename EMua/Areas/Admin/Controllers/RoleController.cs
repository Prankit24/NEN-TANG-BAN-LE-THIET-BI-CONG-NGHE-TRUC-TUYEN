using System.Text;
using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class RoleController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly ILogger<RoleController> _logger;
    private readonly IUndoDeletionManager _undoManager;
    private const int MaxRoleNameLength = 50;

    // Các tên này đang được sử dụng trong đăng nhập và kiểm tra quyền.
    private static readonly HashSet<string> SystemRoleNames = new(
        new[] { "Admin", "Quản trị viên", "QuanTriVien", "NhanVien",
                "Nhân viên", "KhachHang", "Khách hàng" },
        StringComparer.OrdinalIgnoreCase);

    public RoleController(
        EMuaDbContext db, 
        ILogger<RoleController> logger,
        IUndoDeletionManager undoManager)
    {
        _db = db;
        _logger = logger;
        _undoManager = undoManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var roles = await _db.PhanQuyens
            .AsNoTracking()
            .Include(r => r.NguoiDungs)
            .OrderBy(r => r.MaQuyen)
            .ToListAsync();

        // Lọc bỏ role đang chờ xóa trong 60s
        roles = roles.Where(r => !_undoManager.IsEntityPendingDeletion(DeletionTargetType.Role, r.MaQuyen)).ToList();

        ViewBag.TotalRoles = roles.Count;
        ViewBag.TotalUsers = roles.Sum(r => r.NguoiDungs.Count);
        ViewBag.AdminRoleName = "Admin";
        ViewBag.ProtectedRoleIds = roles.Where(IsSystemRole)
            .Select(r => r.MaQuyen).ToHashSet();
        ViewBag.RoleDescriptions = roles.ToDictionary(
            r => r.MaQuyen, GetRoleDescription);

        return View(roles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? tenQuyen)
    {
        var name = NormalizeName(tenQuyen);
        var error = ValidateName(name);
        if (error != null) return Error(error);

        if (SystemRoleNames.Contains(name))
            return Error("Đây là tên dành riêng cho nhóm quyền hệ thống. Không thể tạo nhóm khác cùng vai trò này.");

        if (await NameExistsAsync(name))
            return Error("Tên nhóm quyền đã tồn tại. Vui lòng chọn tên khác.");

        _db.PhanQuyens.Add(new PhanQuyen { TenQuyen = name });
        if (!await SaveAsync("thêm", null)) return RedirectToAction(nameof(Index));

        TempData["SuccessMessage"] = $"Đã thêm nhóm quyền '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int maQuyen, string? tenQuyen)
    {
        if (maQuyen <= 0) return Error("Mã nhóm quyền không hợp lệ.");

        var role = await _db.PhanQuyens.FindAsync(maQuyen);
        if (role == null) return Error("Không tìm thấy nhóm quyền cần sửa.");
        if (IsSystemRole(role))
            return Error("Không được đổi tên nhóm quyền hệ thống vì tên và mã quyền đang được sử dụng để phân quyền đăng nhập.");

        var name = NormalizeName(tenQuyen);
        var error = ValidateName(name);
        if (error != null) return Error(error);
        if (SystemRoleNames.Contains(name))
            return Error("Không được đổi nhóm tùy biến thành tên nhóm quyền hệ thống.");
        if (await NameExistsAsync(name, maQuyen))
            return Error("Tên nhóm quyền đã tồn tại. Vui lòng chọn tên khác.");

        role.TenQuyen = name;
        if (!await SaveAsync("sửa", maQuyen)) return RedirectToAction(nameof(Index));

        TempData["SuccessMessage"] = "Đã cập nhật tên nhóm quyền.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int maQuyen)
    {
        if (maQuyen <= 0) return Error("Mã nhóm quyền không hợp lệ.");

        var role = await _db.PhanQuyens.FindAsync(maQuyen);
        if (role == null) return Error("Không tìm thấy nhóm quyền cần xóa.");
        if (IsSystemRole(role))
            return Error("Không được xóa nhóm quyền hệ thống: Admin, Nhân viên và Khách hàng.");
        if (await _db.NguoiDungs.AnyAsync(u => u.MaQuyen == maQuyen))
            return Error("Nhóm quyền đang có tài khoản sử dụng. Hãy chuyển các tài khoản sang nhóm phù hợp trước khi xóa.");

        var name = role.TenQuyen;
        var undoItem = _undoManager.RegisterPendingDeletion(
            DeletionTargetType.Role,
            role.MaQuyen,
            name,
            role.MoTa,
            User);

        TempData["SuccessMessage"] = $"Đã xóa nhóm quyền '{name}'. Bạn có 60 giây để hoàn tác.";
        TempData["UndoToken"] = undoItem.Token;
        TempData["UndoAction"] = Url.Action("UndoDelete", "Role", new { area = "Admin" });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoDelete(string token)
    {
        if (!_undoManager.TryClaimUndo(token, User, out var item, out var errorMessage))
        {
            TempData["ErrorMessage"] = errorMessage ?? "Không thể hoàn tác.";
            return RedirectToAction(nameof(Index));
        }

        var role = await _db.PhanQuyens.FindAsync(item!.EntityId);
        if (role != null)
        {
            TempData["SuccessMessage"] = $"Đã khôi phục nhóm quyền '{role.TenQuyen}'.";
            return RedirectToAction(nameof(Index));
        }

        // Nếu bản ghi đã bị xóa vật lý, tái tạo lại theo snapshot
        var newRole = new PhanQuyen
        {
            TenQuyen = item.EntityName,
            MoTa = item.AdditionalData
        };
        _db.PhanQuyens.Add(newRole);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Đã hoàn tác và tạo lại nhóm quyền '{newRole.TenQuyen}'.";
        return RedirectToAction(nameof(Index));
    }

    private static string NormalizeName(string? value) =>
        (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Vui lòng nhập tên nhóm quyền.";
        if (name.Length > MaxRoleNameLength) return "Tên nhóm quyền tối đa 50 ký tự.";
        if (name.Any(char.IsControl) || name.Contains(','))
            return "Tên nhóm quyền không được chứa xuống dòng, ký tự điều khiển hoặc dấu phẩy.";
        return null;
    }

    private Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var normalized = name.ToLowerInvariant();
        return _db.PhanQuyens.AnyAsync(r =>
            (!excludeId.HasValue || r.MaQuyen != excludeId.Value) &&
            r.TenQuyen.Trim().ToLower() == normalized);
    }

    private static bool IsSystemRole(PhanQuyen role) =>
        role.MaQuyen is 1 or 2 or 3 || SystemRoleNames.Contains(NormalizeName(role.TenQuyen));

    private static string GetRoleDescription(PhanQuyen role)
    {
        var name = NormalizeName(role.TenQuyen);
        if (role.MaQuyen == 1 || name.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Quản trị viên", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("QuanTriVien", StringComparison.OrdinalIgnoreCase))
            return "Quản trị hệ thống, quản lý tài khoản, phân quyền và phê duyệt nhập hàng.";
        if (role.MaQuyen == 2 || name.Equals("NhanVien", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Nhân viên", StringComparison.OrdinalIgnoreCase))
            return "Vận hành cửa hàng, quản lý sản phẩm, đơn hàng, nhập kho và hóa đơn.";
        if (role.MaQuyen == 3 || name.Equals("KhachHang", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Khách hàng", StringComparison.OrdinalIgnoreCase))
            return "Mua sắm và quản lý thông tin cá nhân, giỏ hàng, đơn hàng, sản phẩm yêu thích.";
        return string.IsNullOrWhiteSpace(role.MoTa)
            ? "Nhóm tùy biến; chưa tự động có quyền truy cập các chức năng Admin hoặc Nhân viên."
            : role.MoTa;
    }

    private RedirectToActionResult Error(string message)
    {
        TempData["ErrorMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> SaveAsync(string operation, int? roleId)
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Bao gồm xung đột tên hoặc có tài khoản vừa được gán quyền trong lúc xóa.
            _logger.LogError(ex, "Không thể {Operation} nhóm quyền {RoleId}", operation, roleId);
            TempData["ErrorMessage"] = "Không lưu được thay đổi. Tên nhóm có thể vừa bị trùng hoặc nhóm đang được tài khoản khác sử dụng. Vui lòng tải lại và kiểm tra.";
            return false;
        }
    }
}
