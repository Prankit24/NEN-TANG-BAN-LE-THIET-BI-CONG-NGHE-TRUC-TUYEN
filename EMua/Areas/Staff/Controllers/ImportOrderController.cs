using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class ImportOrderController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    private readonly ISupplierQuotationService _quotationService;

    public ImportOrderController(EMuaDbContext db, ISupplierQuotationService quotationService)
    {
        _db = db;
        _quotationService = quotationService;
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

    // Danh sách phiếu đề nghị nhập hàng
    public async Task<IActionResult> Index(string? status, string? search, int page = 1, int pageSize = 12)
    {
        var query = _db.PhieuNhapHangs
            .Include(x => x.MaNguoiTaoNavigation)
            .Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.MaNhaCungCapNavigation)
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
            .Include(x => x.MaNhaCungCapNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(b => b.MaSanPhamNavigation)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaPhieuNhap == id);

        if (phieu == null) return NotFound();

        return View(phieu);
    }

    // API lấy danh sách nhà cung cấp từ CSDL (kết hợp bảng NhaCungCap và PhieuNhapHang)
    [HttpGet]
    public async Task<IActionResult> GetSuppliers()
    {
        var listFromTable = await _db.NhaCungCaps
            .AsNoTracking()
            .Where(x => x.TrangThai == "Đang hợp tác")
            .OrderBy(x => x.TenNhaCungCap)
            .Select(x => new { id = x.MaNhaCungCap, name = x.TenNhaCungCap, tax = x.MaSoThue, phone = x.SoDienThoai })
            .ToListAsync();

        return Json(listFromTable);
    }

    // API so sánh báo giá các nhà cung cấp cho 1 biến thể
    [HttpGet]
    public async Task<IActionResult> CompareQuotations(int variantId, int qty = 1)
    {
        if (variantId <= 0) return BadRequest("Mã biến thể không hợp lệ.");

        var quotations = await _quotationService.GetQuotationsForVariantAsync(variantId, qty);
        return Json(new { success = true, data = quotations });
    }

    // API thêm nhanh nhà cung cấp mới lưu trực tiếp vào bảng NhaCungCap
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSupplierAjax([FromBody] SupplierQuickCreateModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.TenNhaCungCap))
        {
            return Json(new { success = false, message = "Tên nhà cung cấp không được để trống." });
        }

        var supplierName = model.TenNhaCungCap.Trim();

        // Kiểm tra xem đã tồn tại chưa
        var existing = await _db.NhaCungCaps
            .FirstOrDefaultAsync(x => EF.Functions.ILike(x.TenNhaCungCap, supplierName));

        if (existing != null)
        {
            return Json(new { success = true, id = existing.MaNhaCungCap, name = existing.TenNhaCungCap, message = "Nhà cung cấp đã có trong hệ thống." });
        }

        var newNcc = new NhaCungCap
        {
            TenNhaCungCap = supplierName,
            SoDienThoai = model.SoDienThoai?.Trim(),
            MaSoThue = model.MaSoThue?.Trim(),
            Email = model.Email?.Trim(),
            DiaChi = model.DiaChi?.Trim(),
            GhiChu = model.GhiChu?.Trim(),
            TrangThai = "Đang hợp tác",
            NgayTao = GetVietnamNow()
        };

        _db.NhaCungCaps.Add(newNcc);
        await _db.SaveChangesAsync();

        return Json(new { success = true, id = newNcc.MaNhaCungCap, name = newNcc.TenNhaCungCap, message = $"Đã thêm nhà cung cấp [{supplierName}] thành công." });
    }

    // Tạo phiếu đề nghị nhập hàng (GET)
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
                var pendingPhieu = await _db.ChiTietPhieuNhapHangs
                    .Include(ct => ct.MaPhieuNhapNavigation)
                    .Where(ct => ct.MaBienThe == variant.MaBienThe && ct.MaPhieuNhapNavigation.TrangThai == "Chờ duyệt")
                    .Select(ct => ct.MaPhieuNhapNavigation.MaChungTu)
                    .FirstOrDefaultAsync();

                ViewBag.PendingWarning = pendingPhieu != null
                    ? $"Biến thể này đang nằm trong phiếu [{pendingPhieu}] đang chờ phê duyệt. Vui lòng cân nhắc trước khi lập thêm yêu cầu."
                    : null;

                // Thử tìm báo giá tốt nhất cho biến thể này
                var bestQuotation = await _quotationService.GetBestQuotationAsync(variant.MaBienThe, 10);
                decimal netPrice = bestQuotation != null ? bestQuotation.DonGiaChuaThue : Math.Round((variant.Gia * 0.7m) / 1.10m, 2);
                decimal vatRate = bestQuotation != null ? bestQuotation.ThueSuatVat : 10.00m;
                decimal vatAmount = bestQuotation != null ? bestQuotation.TienThueVat : Math.Round(netPrice * (vatRate / 100m), 2);
                decimal grossPrice = bestQuotation != null ? bestQuotation.DonGiaSauThue : (netPrice + vatAmount);

                model.Items.Add(new ImportOrderItemViewModel
                {
                    MaBienThe = variant.MaBienThe,
                    TenSanPham = variant.MaSanPhamNavigation?.TenSanPham,
                    MauSac = variant.MauSac,
                    PhienBan = variant.PhienBan,
                    HinhAnh = variant.HinhAnh,
                    TonKhoHienTai = variant.SoLuong,
                    SoLuongNhap = 10,
                    DonGiaChuaThue = netPrice,
                    ThueSuatVat = vatRate,
                    TienThueVat = vatAmount,
                    DonGiaNhapDuKien = grossPrice,
                    GiaBanHienTai = variant.Gia
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
                var netPrice = Math.Round((variant.Gia * 0.7m) / 1.10m, 2);
                var vatAmount = Math.Round(netPrice * 0.10m, 2);
                var grossPrice = netPrice + vatAmount;

                model.Items.Add(new ImportOrderItemViewModel
                {
                    MaBienThe = variant.MaBienThe,
                    TenSanPham = variant.MaSanPhamNavigation?.TenSanPham,
                    MauSac = variant.MauSac,
                    PhienBan = variant.PhienBan,
                    HinhAnh = variant.HinhAnh,
                    TonKhoHienTai = variant.SoLuong,
                    SoLuongNhap = 10,
                    DonGiaChuaThue = netPrice,
                    ThueSuatVat = 10.00m,
                    TienThueVat = vatAmount,
                    DonGiaNhapDuKien = grossPrice,
                    GiaBanHienTai = variant.Gia
                });
            }
        }

        // Danh sách nhà cung cấp từ bảng độc lập NhaCungCap
        var suppliersFromDb = await _db.NhaCungCaps
            .AsNoTracking()
            .Where(x => x.TrangThai == "Đang hợp tác")
            .OrderBy(x => x.TenNhaCungCap)
            .ToListAsync();

        ViewBag.Suppliers = suppliersFromDb;

        // Danh sách biến thể gợi ý để thêm nhiều dòng
        ViewBag.AllVariants = await _db.BienTheSanPhams
            .Include(b => b.MaSanPhamNavigation)
            .OrderBy(b => b.MaSanPhamNavigation.TenSanPham)
            .Select(b => new
            {
                b.MaBienThe,
                Display = $"{b.MaSanPhamNavigation.TenSanPham} ({b.MauSac} - {b.PhienBan}) - Tồn: {b.SoLuong} - Giá bán: {b.Gia:N0}₫",
                b.Gia,
                b.SoLuong
            })
            .ToListAsync();

        return View(model);
    }

    // Xác nhận lập đề nghị nhập hàng (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportOrderCreateViewModel model)
    {
        if (model.Items == null || !model.Items.Any(x => x.MaBienThe > 0 && x.SoLuongNhap > 0))
        {
            ModelState.AddModelError("", "Vui lòng chọn ít nhất một biến thể sản phẩm cần nhập hàng với số lượng hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(model.NhaCungCap))
        {
            ModelState.AddModelError(nameof(model.NhaCungCap), "Vui lòng chọn hoặc nhập tên nhà cung cấp.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Suppliers = await _db.NhaCungCaps
                .AsNoTracking()
                .Where(x => x.TrangThai == "Đang hợp tác")
                .OrderBy(x => x.TenNhaCungCap)
                .ToListAsync();

            ViewBag.AllVariants = await _db.BienTheSanPhams
                .Include(b => b.MaSanPhamNavigation)
                .OrderBy(b => b.MaSanPhamNavigation.TenSanPham)
                .Select(b => new
                {
                    b.MaBienThe,
                    Display = $"{b.MaSanPhamNavigation.TenSanPham} ({b.MauSac} - {b.PhienBan}) - Tồn: {b.SoLuong} - Giá bán: {b.Gia:N0}₫",
                    b.Gia,
                    b.SoLuong
                })
                .ToListAsync();

            return View(model);
        }

        // Kiểm tra và lấy danh tính nhân viên lập phiếu
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int currentUserId = 1;
        if (int.TryParse(userIdClaim, out var parsedId))
        {
            currentUserId = parsedId;
        }

        // Sinh mã chứng từ chuẩn hệ thống
        var nowVn = GetVietnamNow();
        var maChungTu = $"PNN-{nowVn:yyyyMMdd}-{new Random().Next(1000, 9999)}";

        // Đồng bộ hoặc tìm MaNhaCungCap
        int? nccId = model.MaNhaCungCap;
        var supplierName = model.NhaCungCap.Trim();
        if (!nccId.HasValue || nccId.Value <= 0)
        {
            var existingNcc = await _db.NhaCungCaps
                .FirstOrDefaultAsync(x => EF.Functions.ILike(x.TenNhaCungCap, supplierName));
            if (existingNcc != null)
            {
                nccId = existingNcc.MaNhaCungCap;
            }
            else
            {
                // Tự động tạo bản ghi mới trong bảng NhaCungCap
                var newNcc = new NhaCungCap
                {
                    TenNhaCungCap = supplierName,
                    TrangThai = "Đang hợp tác",
                    NgayTao = nowVn
                };
                _db.NhaCungCaps.Add(newNcc);
                await _db.SaveChangesAsync();
                nccId = newNcc.MaNhaCungCap;
            }
        }

        // Tính toán các đại lượng tài chính chính xác ở backend
        decimal totalNet = 0;
        decimal totalVat = 0;
        decimal totalGross = 0;

        var phieu = new PhieuNhapHang
        {
            MaChungTu = maChungTu,
            MaNhaCungCap = nccId,
            NhaCungCap = supplierName,
            LyDoNhap = model.LyDoNhap.Trim(),
            DienGiai = model.DienGiai?.Trim(),
            GhiChu = model.GhiChu?.Trim(),
            TrangThai = "Chờ duyệt",
            MaNguoiTao = currentUserId,
            NgayTao = nowVn,
            ChiPhiVanChuyen = model.ChiPhiVanChuyen,
            ChiPhiKhac = model.ChiPhiKhac,
            ChietKhau = model.ChietKhauTong
        };

        foreach (var item in model.Items.Where(x => x.MaBienThe > 0 && x.SoLuongNhap > 0))
        {
            // Nếu DonGiaChuaThue chưa có, tính ngược từ DonGiaNhapDuKien theo ThueSuatVat
            var vatRate = item.ThueSuatVat > 0 ? item.ThueSuatVat : 10.00m;
            var grossUnitPrice = item.DonGiaNhapDuKien;
            var netUnitPrice = item.DonGiaChuaThue > 0 
                ? item.DonGiaChuaThue 
                : Math.Round(grossUnitPrice / (1 + (vatRate / 100m)), 2);
            var itemVatUnit = Math.Round(grossUnitPrice - netUnitPrice, 2);
            var lineSubtotal = grossUnitPrice * item.SoLuongNhap;
            var lineVat = itemVatUnit * item.SoLuongNhap;
            var lineNet = lineSubtotal - lineVat;

            totalNet += lineNet;
            totalVat += lineVat;
            totalGross += lineSubtotal;

            phieu.ChiTietPhieuNhapHangs.Add(new ChiTietPhieuNhapHang
            {
                MaBienThe = item.MaBienThe,
                SoLuongNhap = item.SoLuongNhap,
                DonGiaNhapDuKien = grossUnitPrice,
                DonGiaChuaThue = netUnitPrice,
                ThueSuatVat = vatRate,
                TienThueVat = lineVat,
                DonGiaSauThue = grossUnitPrice,
                ChietKhau = item.ChietKhau,
                ThanhTien = lineSubtotal,
                GhiChu = item.GhiChu?.Trim()
            });
        }

        phieu.TienHangChuaThue = totalNet;
        phieu.TienThueVat = totalVat;
        phieu.TongTienDuKien = totalGross + phieu.ChiPhiVanChuyen + phieu.ChiPhiKhac - phieu.ChietKhau;

        _db.PhieuNhapHangs.Add(phieu);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã tạo phiếu đề nghị nhập hàng [{maChungTu}] thành công (Tổng tiền: {phieu.TongTienDuKien:N0}₫) và gửi Admin phê duyệt.";
        return RedirectToAction(nameof(Index));
    }
}
