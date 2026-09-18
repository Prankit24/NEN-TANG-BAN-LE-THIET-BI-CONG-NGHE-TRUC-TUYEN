using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class ProductController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public ProductController(EMuaDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q, string? status, int? brandId)
    {
        var query = _db.SanPhams.AsNoTracking().Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.TenSanPham != null && x.TenSanPham.Contains(q.Trim()));
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            query = query.Where(x => x.TrangThaiSanPham == status);
        if (brandId.HasValue)
            query = query.Where(x => x.MaThuongHieu == brandId.Value);
        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.BrandId = brandId;
        ViewBag.Statuses = await _db.SanPhams.AsNoTracking()
            .Select(x => x.TrangThaiSanPham ?? "Chưa xác định").Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Brands = await _db.ThuongHieus.AsNoTracking().OrderBy(x => x.TenThuongHieu).ToListAsync();
        return View(await query.OrderByDescending(x => x.MaSanPham).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadSelectionsAsync();
        return View(new ProductCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateViewModel model)
    {
        if (model.MaThuongHieu is null || !await _db.ThuongHieus.AnyAsync(x => x.MaThuongHieu == model.MaThuongHieu))
            ModelState.AddModelError(nameof(model.MaThuongHieu), "Thương hiệu không hợp lệ.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); return View(model); }

        _db.SanPhams.Add(new SanPham {
            TenSanPham = model.TenSanPham.Trim(), MaDanhMuc = model.MaDanhMuc,
            MaThuongHieu = model.MaThuongHieu ?? 0, GiaGoc = model.GiaGoc,
            BaoHanh = model.BaoHanh?.Trim(), MoTa = model.MoTa?.Trim(),
            ThongSoKyThuat = model.ThongSoKyThuat?.Trim(), TrangThaiSanPham = "Còn hàng"
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm sản phẩm mới.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.SanPhams.Include(x => x.BienTheSanPhams).FirstOrDefaultAsync(x => x.MaSanPham == id);
        if (product == null) return NotFound();
        await LoadSelectionsAsync();
        return View(new ProductCreateViewModel
        {
            TenSanPham = product.TenSanPham ?? string.Empty,
            MaDanhMuc = product.MaDanhMuc, MaThuongHieu = product.MaThuongHieu,
            GiaGoc = product.GiaGoc, BaoHanh = product.BaoHanh,
            MoTa = product.MoTa, ThongSoKyThuat = product.ThongSoKyThuat,
            BienThe = product.BienTheSanPhams.Select(variant => new ProductVariantQuantityViewModel
            {
                MaBienThe = variant.MaBienThe, MauSac = variant.MauSac,
                PhienBan = variant.PhienBan, SoLuong = variant.SoLuong
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductCreateViewModel model)
    {
        var product = await _db.SanPhams.Include(x => x.BienTheSanPhams).FirstOrDefaultAsync(x => x.MaSanPham == id);
        if (product == null) return NotFound();
        if (model.MaThuongHieu is null || !await _db.ThuongHieus.AnyAsync(x => x.MaThuongHieu == model.MaThuongHieu))
            ModelState.AddModelError(nameof(model.MaThuongHieu), "Thương hiệu không hợp lệ.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); return View(model); }
        product.TenSanPham = model.TenSanPham.Trim(); product.MaDanhMuc = model.MaDanhMuc;
        product.MaThuongHieu = model.MaThuongHieu ?? product.MaThuongHieu; product.GiaGoc = model.GiaGoc;
        product.BaoHanh = model.BaoHanh?.Trim(); product.MoTa = model.MoTa?.Trim(); product.ThongSoKyThuat = model.ThongSoKyThuat?.Trim();
        foreach (var variantModel in model.BienThe)
        {
            var variant = product.BienTheSanPhams.FirstOrDefault(x => x.MaBienThe == variantModel.MaBienThe);
            if (variant != null) variant.SoLuong = variantModel.SoLuong;
        }
        await _db.SaveChangesAsync(); TempData["Success"] = "Đã cập nhật sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVisibility(int id)
    {
        var product = await _db.SanPhams.FindAsync(id);
        if (product == null) return NotFound();
        product.TrangThaiSanPham = product.TrangThaiSanPham == "Ẩn" ? "Còn hàng" : "Ẩn";
        await _db.SaveChangesAsync();
        TempData["Success"] = product.TrangThaiSanPham == "Ẩn" ? "Đã ẩn sản phẩm khỏi cửa hàng." : "Đã hiển thị lại sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadSelectionsAsync()
    {
        ViewBag.Categories = new SelectList(await _db.DanhMucSanPhams.AsNoTracking().OrderBy(x => x.TenDanhMuc).ToListAsync(), "MaDanhMuc", "TenDanhMuc");
        ViewBag.Brands = new SelectList(await _db.ThuongHieus.AsNoTracking().Where(x => x.TrangThai).OrderBy(x => x.TenThuongHieu).ToListAsync(), "MaThuongHieu", "TenThuongHieu");
    }
}
