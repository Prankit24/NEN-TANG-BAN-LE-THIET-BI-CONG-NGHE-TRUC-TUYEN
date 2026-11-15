using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Services;
using EMua.Areas.Staff.Models;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class ImportOrderController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly ImportOrderReceivingService _receiving;

    public ImportOrderController(EMuaDbContext db, ImportOrderReceivingService receiving)
    {
        _db = db;
        _receiving = receiving;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
        { context.Result = Challenge(); return; }
        var names = new[] { "Admin", "Quản trị viên", "QuanTriVien" };
        var allowed = await _db.NguoiDungs.AsNoTracking().AnyAsync(u => u.MaNguoiDung == id && u.TrangThai &&
            (u.MaQuyen == 1 || names.Contains(u.MaQuyenNavigation.TenQuyen)));
        if (!allowed) { context.Result = Forbid(); return; }
        await next();
    }

    public async Task<IActionResult> Index(string? status, string? search, int page = 1, int pageSize = 12)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.PhieuNhapHangs
            .Include(x => x.MaNguoiTaoNavigation)
            .Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(b => b.MaSanPhamNavigation)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            query = query.Where(x => x.TrangThai == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var pattern = $"%{s}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.MaChungTu, pattern) ||
                EF.Functions.ILike(x.NhaCungCap, pattern) ||
                EF.Functions.ILike(x.LyDoNhap, pattern) ||
                (x.DienGiai != null && EF.Functions.ILike(x.DienGiai, pattern)));
        }

        var totalItems = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = await query.OrderByDescending(x => x.NgayTao).ThenByDescending(x => x.MaPhieuNhap)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Status = status ?? "all";
        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        ViewBag.PageSize = pageSize;

        return View(items);
    }

    public async Task<IActionResult> Details(int id)
    {
        var phieu = await _db.PhieuNhapHangs
            .Include(x => x.MaNguoiTaoNavigation)
            .Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.MaNguoiNhapKhoNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(b => b.MaSanPhamNavigation)
            .AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(x => x.MaPhieuNhap == id);

        if (phieu == null) return NotFound();

        return View(phieu);
    }

    // Phê duyệt phiếu
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var phieu = await _db.PhieuNhapHangs.FirstOrDefaultAsync(x => x.MaPhieuNhap == id);
        if (phieu == null) return NotFound();

        if (phieu.TrangThai != "Chờ duyệt")
        {
            TempData["Error"] = "Chỉ có thể phê duyệt phiếu đang ở trạng thái 'Chờ duyệt'.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var lines = await _db.ChiTietPhieuNhapHangs.AsNoTracking().Where(x => x.MaPhieuNhap == id).ToListAsync();
        var expectedTotal = lines.Sum(x => (x.DonGiaNhapDuKien - x.ChietKhau) * x.SoLuongNhap)
            + phieu.ChiPhiVanChuyen + phieu.ChiPhiKhac - phieu.ChietKhau;
        if (lines.Count == 0 || lines.Any(x => x.SoLuongNhap <= 0 || x.DonGiaNhapDuKien < 0 ||
            x.ChietKhau < 0 || x.ChietKhau > x.DonGiaNhapDuKien) || phieu.TongTienDuKien < 0 ||
            Math.Abs(expectedTotal - phieu.TongTienDuKien) > 0.01m)
        {
            TempData["Error"] = "Chi tiết hoặc tổng tiền phiếu không hợp lệ/không khớp. Vui lòng yêu cầu nhân viên đối chiếu và lập lại phiếu trước khi duyệt.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0)
            return Challenge();
        var now = ImportOrderReceivingService.VietnamNow();
        var changed = await _db.PhieuNhapHangs
            .Where(x => x.MaPhieuNhap == id && x.TrangThai == "Chờ duyệt")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.TrangThai, "Đã duyệt")
                .SetProperty(x => x.MaNguoiDuyet, (int?)adminId)
                .SetProperty(x => x.NgayDuyet, (DateTime?)now));
        if (changed != 1)
        {
            TempData["Error"] = "Phiếu vừa được xử lý. Vui lòng tải lại trang.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"Đã phê duyệt phiếu đề nghị nhập hàng [{phieu.MaChungTu}].";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Từ chối phiếu (Bắt buộc lý do)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string lyDoTuChoi)
    {
        if (string.IsNullOrWhiteSpace(lyDoTuChoi))
        {
            TempData["Error"] = "Bắt buộc phải nhập lý do khi từ chối phê duyệt phiếu.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var phieu = await _db.PhieuNhapHangs.FirstOrDefaultAsync(x => x.MaPhieuNhap == id);
        if (phieu == null) return NotFound();

        if (phieu.TrangThai != "Chờ duyệt")
        {
            TempData["Error"] = "Chỉ có thể từ chối phiếu đang ở trạng thái 'Chờ duyệt'.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId) || adminId <= 0)
            return Challenge();
        if (lyDoTuChoi.Trim().Length > 500)
        {
            TempData["Error"] = "Lý do từ chối tối đa 500 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var now = ImportOrderReceivingService.VietnamNow();
        var reason = lyDoTuChoi.Trim();
        var changed = await _db.PhieuNhapHangs
            .Where(x => x.MaPhieuNhap == id && x.TrangThai == "Chờ duyệt")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.TrangThai, "Từ chối")
                .SetProperty(x => x.MaNguoiDuyet, (int?)adminId)
                .SetProperty(x => x.NgayDuyet, (DateTime?)now)
                .SetProperty(x => x.LyDoTuChoi, reason));
        if (changed != 1)
        {
            TempData["Error"] = "Phiếu vừa được xử lý. Vui lòng tải lại trang.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"Đã từ chối phiếu [{phieu.MaChungTu}]. Lý do: {lyDoTuChoi}";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Receive(int id)
    {
        var vm = await _receiving.LoadAsync(id);
        if (vm == null) return NotFound();
        if (vm.TrangThai != "Đã duyệt")
        {
            TempData["Error"] = "Chỉ kiểm nhận phiếu đã duyệt và chưa nhập kho.";
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewData["ReceiveArea"] = "Admin";
        ViewData["ReceiveAction"] = "ConfirmReceived";
        return View("Receive", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceived([FromForm] ImportOrderReceiveCommand model)
    {
        var result = ModelState.IsValid
            ? await _receiving.ConfirmAsync(model, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!))
            : (Success: false, Message: "Số lượng hoặc ghi chú kiểm nhận không hợp lệ.");
        if (result.Success)
        { TempData["Success"] = result.Message; return RedirectToAction(nameof(Details), new { id = model.MaPhieuNhap }); }
        var vm = await _receiving.LoadAsync(model.MaPhieuNhap);
        if (vm == null) return NotFound();
        if (vm.TrangThai != "Đã duyệt")
        { TempData["Error"] = result.Message; return RedirectToAction(nameof(Details), new { id = model.MaPhieuNhap }); }
        foreach (var line in vm.Items)
        {
            var posted = model.Items?.FirstOrDefault(x => x.MaChiTietPhieuNhap == line.MaChiTietPhieuNhap);
            if (posted != null) line.SoLuongThucNhan = posted.SoLuongThucNhan;
        }
        vm.GhiChuKiemNhan = model.GhiChuKiemNhan;
        ModelState.AddModelError("", result.Message);
        ViewData["ReceiveArea"] = "Admin"; ViewData["ReceiveAction"] = "ConfirmReceived";
        return View("Receive", vm);
    }
}
