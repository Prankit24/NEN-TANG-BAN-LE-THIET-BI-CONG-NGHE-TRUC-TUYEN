using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class ImportOrderController : StaffControllerBase
{
    private readonly EMuaDbContext _db;

    public ImportOrderController(EMuaDbContext db)
    {
        _db = db;
    }

    // Danh sách phiếu nhập hàng
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

    // Chi tiết phiếu
    public async Task<IActionResult> Details(int id)
    {
        var phieu = await _db.PhieuNhapHangs
            .Include(x => x.MaNguoiTaoNavigation)
            .Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.MaNguoiNhapKhoNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(b => b.MaSanPhamNavigation)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaPhieuNhap == id);

        if (phieu == null) return NotFound();

        return View(phieu);
    }

    // Tạo phiếu đề nghị nhập hàng (có thể chọn sẵn productId, variantId)
    [HttpGet]
    public async Task<IActionResult> Create(int? productId, int? variantId)
    {
        var model = new ImportOrderCreateViewModel();

        if (variantId.HasValue && variantId.Value > 0)
        {
            var variant = await _db.BienTheSanPhams
                .Include(b => b.MaSanPhamNavigation)
                .FirstOrDefaultAsync(b => b.MaBienThe == variantId.Value);

            if (variant != null)
            {
                // Kiểm tra xem biến thể này đã có phiếu chờ duyệt chưa
                var pendingPhieu = await _db.ChiTietPhieuNhapHangs
                    .Include(ct => ct.MaPhieuNhapNavigation)
                    .Where(ct => ct.MaBienThe == variant.MaBienThe && ct.MaPhieuNhapNavigation.TrangThai == "Chờ duyệt")
                    .Select(ct => ct.MaPhieuNhapNavigation.MaChungTu)
                    .FirstOrDefaultAsync();

                ViewBag.PendingWarning = pendingPhieu != null
                    ? $"Biến thể này đang nằm trong phiếu [{pendingPhieu}] đang chờ phê duyệt. Vui lòng cân nhắc trước khi lập thêm yêu cầu."
                    : null;

                model.Items.Add(new ImportOrderItemViewModel
                {
                    MaBienThe = variant.MaBienThe,
                    TenSanPham = variant.MaSanPhamNavigation?.TenSanPham,
                    MauSac = variant.MauSac,
                    PhienBan = variant.PhienBan,
                    HinhAnh = variant.HinhAnh,
                    TonKhoHienTai = variant.SoLuong,
                    SoLuongNhap = 10,
                    DonGiaNhapDuKien = variant.Gia * 0.7m // Gợi ý giá vốn ~70% giá bán
                });
            }
        }
        else if (productId.HasValue && productId.Value > 0)
        {
            var variants = await _db.BienTheSanPhams
                .Include(b => b.MaSanPhamNavigation)
                .Where(b => b.MaSanPham == productId.Value)
                .ToListAsync();

            foreach (var variant in variants)
            {
                model.Items.Add(new ImportOrderItemViewModel
                {
                    MaBienThe = variant.MaBienThe,
                    TenSanPham = variant.MaSanPhamNavigation?.TenSanPham,
                    MauSac = variant.MauSac,
                    PhienBan = variant.PhienBan,
                    HinhAnh = variant.HinhAnh,
                    TonKhoHienTai = variant.SoLuong,
                    SoLuongNhap = 10,
                    DonGiaNhapDuKien = variant.Gia * 0.7m
                });
            }
        }

        // Danh sách biến thể gợi ý để thêm nhiều dòng
        ViewBag.AllVariants = await _db.BienTheSanPhams
            .Include(b => b.MaSanPhamNavigation)
            .OrderBy(b => b.MaSanPhamNavigation.TenSanPham)
            .Select(b => new
            {
                b.MaBienThe,
                Display = $"{b.MaSanPhamNavigation.TenSanPham} ({b.MauSac} - {b.PhienBan}) - Tồn: {b.SoLuong}",
                b.Gia,
                b.SoLuong
            })
            .ToListAsync();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportOrderCreateViewModel model)
    {
        if (model.Items == null || !model.Items.Any(x => x.MaBienThe > 0 && x.SoLuongNhap > 0))
        {
            ModelState.AddModelError("", "Vui lòng chọn ít nhất một biến thể sản phẩm cần nhập hàng với số lượng hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.AllVariants = await _db.BienTheSanPhams
                .Include(b => b.MaSanPhamNavigation)
                .OrderBy(b => b.MaSanPhamNavigation.TenSanPham)
                .Select(b => new
                {
                    b.MaBienThe,
                    Display = $"{b.MaSanPhamNavigation.TenSanPham} ({b.MauSac} - {b.PhienBan}) - Tồn: {b.SoLuong}",
                    b.Gia,
                    b.SoLuong
                })
                .ToListAsync();
            return View(model);
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int currentUserId = 1; // Default fallback
        if (int.TryParse(userIdClaim, out var parsedId))
        {
            currentUserId = parsedId;
        }

        var maChungTu = $"PNN-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";

        var phieu = new PhieuNhapHang
        {
            MaChungTu = maChungTu,
            NhaCungCap = model.NhaCungCap.Trim(),
            LyDoNhap = model.LyDoNhap.Trim(),
            DienGiai = model.DienGiai?.Trim(),
            GhiChu = model.GhiChu?.Trim(),
            TrangThai = "Chờ duyệt",
            MaNguoiTao = currentUserId,
            NgayTao = DateTime.UtcNow,
            TongTienDuKien = model.Items.Where(x => x.MaBienThe > 0 && x.SoLuongNhap > 0).Sum(x => x.SoLuongNhap * x.DonGiaNhapDuKien)
        };

        foreach (var item in model.Items.Where(x => x.MaBienThe > 0 && x.SoLuongNhap > 0))
        {
            phieu.ChiTietPhieuNhapHangs.Add(new ChiTietPhieuNhapHang
            {
                MaBienThe = item.MaBienThe,
                SoLuongNhap = item.SoLuongNhap,
                DonGiaNhapDuKien = item.DonGiaNhapDuKien,
                GhiChu = item.GhiChu?.Trim()
            });
        }

        _db.PhieuNhapHangs.Add(phieu);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã tạo phiếu đề nghị nhập hàng [{maChungTu}] thành công và gửi Admin phê duyệt.";
        return RedirectToAction(nameof(Index));
    }
}
