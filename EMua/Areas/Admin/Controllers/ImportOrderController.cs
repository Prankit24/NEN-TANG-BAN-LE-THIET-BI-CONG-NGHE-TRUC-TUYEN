using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Data;
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

    public ImportOrderController(EMuaDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? status, string? search, int page = 1, int pageSize = 12)
    {
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
            var s = search.Trim().ToLower();
            query = query.Where(x => x.MaChungTu.ToLower().Contains(s) || x.NhaCungCap.ToLower().Contains(s) || x.LyDoNhap.ToLower().Contains(s));
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

        var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int adminId = 1;
        if (int.TryParse(adminIdClaim, out var parsed)) adminId = parsed;

        phieu.TrangThai = "Đã duyệt";
        phieu.MaNguoiDuyet = adminId;
        phieu.NgayDuyet = DateTime.UtcNow;

        await _db.SaveChangesAsync();

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

        var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int adminId = 1;
        if (int.TryParse(adminIdClaim, out var parsed)) adminId = parsed;

        phieu.TrangThai = "Từ chối";
        phieu.MaNguoiDuyet = adminId;
        phieu.NgayDuyet = DateTime.UtcNow;
        phieu.LyDoTuChoi = lyDoTuChoi.Trim();

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã từ chối phiếu [{phieu.MaChungTu}]. Lý do: {lyDoTuChoi}";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Kiểm nhận nhập kho & cộng tồn kho thực tế (Transaction an toàn, chống cộng trùng)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceived(int id)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var phieu = await _db.PhieuNhapHangs
                .Include(x => x.ChiTietPhieuNhapHangs)
                    .ThenInclude(ct => ct.MaBienTheNavigation)
                .FirstOrDefaultAsync(x => x.MaPhieuNhap == id);

            if (phieu == null) return NotFound();

            if (phieu.TrangThai != "Đã duyệt")
            {
                TempData["Error"] = "Chỉ có thể kiểm nhận nhập kho cho phiếu đã được Admin phê duyệt.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int adminId = 1;
            if (int.TryParse(adminIdClaim, out var parsed)) adminId = parsed;

            // Tăng tồn kho từng biến thể
            foreach (var ct in phieu.ChiTietPhieuNhapHangs)
            {
                var variant = await _db.BienTheSanPhams.FirstOrDefaultAsync(b => b.MaBienThe == ct.MaBienThe);
                if (variant != null)
                {
                    variant.SoLuong += ct.SoLuongNhap;
                    if (variant.TrangThai == "Hết hàng" && variant.SoLuong > 0)
                    {
                        variant.TrangThai = "Còn hàng";
                    }
                }
            }

            phieu.TrangThai = "Đã nhập kho";
            phieu.NgayNhapKho = DateTime.UtcNow;
            phieu.MaNguoiNhapKho = adminId;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Success"] = $"Đã kiểm nhận hàng thành công cho phiếu [{phieu.MaChungTu}]. Tồn kho các sản phẩm đã được tự động cộng tăng.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            TempData["Error"] = $"Lỗi khi xác nhận nhập kho: {ex.Message}";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
