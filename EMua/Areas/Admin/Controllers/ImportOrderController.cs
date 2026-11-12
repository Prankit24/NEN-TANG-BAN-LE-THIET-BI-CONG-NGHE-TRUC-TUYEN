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
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên")]
public class ImportOrderController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly ImportOrderReceivingService _receiving;

    public ImportOrderController(EMuaDbContext db, ImportOrderReceivingService receiving)
    {
        _db = db;
        _receiving = receiving;
    }

    private static DateTime GetVietnamNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vnTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return DateTime.SpecifyKind(vnTime, DateTimeKind.Unspecified);
        }
        catch
        {
            var vnTime = DateTime.UtcNow.AddHours(7);
            return DateTime.SpecifyKind(vnTime, DateTimeKind.Unspecified);
        }
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
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = await query.OrderByDescending(x => x.NgayTao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Status = status ?? "all";
        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

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
            .FirstOrDefaultAsync(x => x.MaPhieuNhap == id);

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
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Challenge();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu kiểm nhận không hợp lệ. Nhập số nguyên từ 0 đến 100.000, ghi chú tối đa 500 ký tự.";
            return RedirectToAction(nameof(Receive), new { id = model.MaPhieuNhap });
        }
        var result = await _receiving.ConfirmAsync(model, userId);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(result.Success ? nameof(Details) : nameof(Receive), new { id = model.MaPhieuNhap });
    }
}
