using System;
using System.Collections.Generic;
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
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class ReviewController : Controller
{
    private readonly EMuaDbContext _db;

    public ReviewController(EMuaDbContext db)
    {
        _db = db;
    }

    // =========================================================================
    // 1. DANH SÁCH & TÌM KIẾM BỘ LỌC ĐA CHIỀU
    // =========================================================================
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchString, 
        int? rating, 
        string? status, 
        DateTime? fromDate, 
        DateTime? toDate, 
        int page = 1)
    {
        const int pageSize = 15;

        var query = _db.DanhGiaSanPhams
            .AsNoTracking()
            .Include(r => r.MaNguoiDungNavigation)
            .Include(r => r.MaSanPhamNavigation)
            .Include(r => r.MaDonHangNavigation)
            .Include(r => r.LichSuKiemDuyets)
            .AsQueryable();

        // 1.1 Tìm kiếm theo nội dung bình luận, tên khách hàng hoặc tên sản phẩm
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var pattern = $"%{searchString.Trim()}%";
            query = query.Where(r =>
                (r.BinhLuan != null && EF.Functions.ILike(r.BinhLuan, pattern)) ||
                (r.MaNguoiDungNavigation != null && r.MaNguoiDungNavigation.TenNguoiDung != null && EF.Functions.ILike(r.MaNguoiDungNavigation.TenNguoiDung, pattern)) ||
                (r.MaSanPhamNavigation != null && r.MaSanPhamNavigation.TenSanPham != null && EF.Functions.ILike(r.MaSanPhamNavigation.TenSanPham, pattern)));
        }

        // 1.2 Lọc theo số sao (1 đến 5)
        if (rating.HasValue && rating.Value is >= 1 and <= 5)
        {
            query = query.Where(r => r.SoLuongSao == rating.Value);
        }

        // 1.3 Lọc theo trạng thái kiểm duyệt (visible / hidden / all)
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("visible", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.TrangThai == true);
            }
            else if (status.Equals("hidden", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.TrangThai == false);
            }
        }

        // 1.4 Lọc theo thời gian đánh giá
        if (fromDate.HasValue)
        {
            var f = fromDate.Value.Date;
            query = query.Where(r => r.NgayDanhGia >= f);
        }
        if (toDate.HasValue)
        {
            var t = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(r => r.NgayDanhGia <= t);
        }

        var totalItems = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var reviews = await query
            .OrderByDescending(r => r.NgayDanhGia ?? DateTime.MinValue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.TotalReviews = await _db.DanhGiaSanPhams.CountAsync();
        ViewBag.VisibleCount = await _db.DanhGiaSanPhams.CountAsync(r => r.TrangThai == true);
        ViewBag.HiddenCount = await _db.DanhGiaSanPhams.CountAsync(r => r.TrangThai == false);
        ViewBag.AvgRating = await _db.DanhGiaSanPhams.Where(r => r.TrangThai == true).AverageAsync(r => (double?)r.SoLuongSao) ?? 5.0;
        ViewBag.FiveStarCount = await _db.DanhGiaSanPhams.CountAsync(r => r.SoLuongSao == 5);
        
        ViewBag.SearchString = searchString;
        ViewBag.SelectedRating = rating;
        ViewBag.SelectedStatus = status ?? "all";
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;

        return View(reviews);
    }

    // =========================================================================
    // 2. ẨN ĐÁNH GIÁ (BẮT BUỘC CHỌN LÝ DO VI PHẠM)
    // =========================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HideReview(int id, string lyDoViPham, string? ghiChuChiTiet)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Mã đánh giá không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        lyDoViPham = lyDoViPham?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(lyDoViPham))
        {
            TempData["ErrorMessage"] = "Vui lòng chọn hoặc cung cấp lý do vi phạm khi ẩn đánh giá.";
            return RedirectToAction(nameof(Index));
        }

        if (lyDoViPham.Equals("Khác", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(ghiChuChiTiet))
        {
            TempData["ErrorMessage"] = "Khi chọn lý do 'Khác', bắt buộc nhập mô tả chi tiết lý do vi phạm.";
            return RedirectToAction(nameof(Index));
        }

        var review = await _db.DanhGiaSanPhams.FindAsync(id);
        if (review == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy đánh giá cần kiểm duyệt.";
            return RedirectToAction(nameof(Index));
        }

        var adminIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(adminIdStr, out var adminId);
        var adminName = User.Identity?.Name ?? "Quản trị viên";

        var now = DateTime.Now;

        review.TrangThai = false; // Ẩn đánh giá
        review.LyDoAn = lyDoViPham;
        review.GhiChuKiemDuyet = ghiChuChiTiet?.Trim();
        review.MaNguoiKiemDuyet = adminId > 0 ? adminId : null;
        review.TenNguoiKiemDuyet = adminName;
        review.NgayKiemDuyet = now;

        // Lưu lịch sử kiểm duyệt
        var history = new LichSuKiemDuyetDanhGia
        {
            MaDanhGia = review.MaDanhGia,
            HanhDong = "Ẩn",
            LyDoViPham = lyDoViPham,
            GhiChuChiTiet = ghiChuChiTiet?.Trim(),
            MaNguoiKiemDuyet = adminId > 0 ? adminId : null,
            TenNguoiKiemDuyet = adminName,
            NgayThucHien = now
        };

        _db.LichSuKiemDuyetDanhGias.Add(history);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Đã ẩn đánh giá #{id} với lý do: {lyDoViPham}.";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // 3. KHÔI PHỤC ĐÁNH GIÁ (HIỂN THỊ TRỞ LẠI CÔNG KHAI)
    // =========================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreReview(int id, string? ghiChu)
    {
        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Mã đánh giá không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var review = await _db.DanhGiaSanPhams.FindAsync(id);
        if (review == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy đánh giá cần khôi phục.";
            return RedirectToAction(nameof(Index));
        }

        var adminIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(adminIdStr, out var adminId);
        var adminName = User.Identity?.Name ?? "Quản trị viên";

        var now = DateTime.Now;

        review.TrangThai = true; // Hiển thị trở lại
        review.LyDoAn = null;
        review.GhiChuKiemDuyet = null;
        review.MaNguoiKiemDuyet = adminId > 0 ? adminId : null;
        review.TenNguoiKiemDuyet = adminName;
        review.NgayKiemDuyet = now;

        // Lưu lịch sử kiểm duyệt
        var history = new LichSuKiemDuyetDanhGia
        {
            MaDanhGia = review.MaDanhGia,
            HanhDong = "Khôi phục",
            LyDoViPham = "Được mở lại hiển thị công khai",
            GhiChuChiTiet = ghiChu?.Trim() ?? "Đánh giá hợp lệ, đáp ứng quy định cộng đồng",
            MaNguoiKiemDuyet = adminId > 0 ? adminId : null,
            TenNguoiKiemDuyet = adminName,
            NgayThucHien = now
        };

        _db.LichSuKiemDuyetDanhGias.Add(history);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Đã khôi phục hiển thị đánh giá #{id} thành công.";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // 4. XEM LỊCH SỬ KIỂM DUYỆT (AJAX)
    // =========================================================================
    [HttpGet]
    public async Task<IActionResult> GetAuditHistory(int id)
    {
        if (id <= 0) return BadRequest(new { success = false, message = "Mã đánh giá không hợp lệ." });

        var histories = await _db.LichSuKiemDuyetDanhGias
            .AsNoTracking()
            .Where(h => h.MaDanhGia == id)
            .OrderByDescending(h => h.NgayThucHien)
            .Select(h => new
            {
                h.MaLichSu,
                h.HanhDong,
                h.LyDoViPham,
                h.GhiChuChiTiet,
                h.TenNguoiKiemDuyet,
                NgayThucHien = h.NgayThucHien.ToString("dd/MM/yyyy HH:mm:ss")
            })
            .ToListAsync();

        return Json(new { success = true, histories });
    }
}
