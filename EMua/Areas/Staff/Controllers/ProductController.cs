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
    private readonly IWebHostEnvironment _environment;

    public ProductController(EMuaDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

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
        ValidateImage(model.ImageFile, required: true);
        if (model.BienThe.Count == 0)
            ModelState.AddModelError(nameof(model.BienThe), "Vui lòng nhập ít nhất một biến thể sản phẩm.");
        if (model.MaThuongHieu is null || !await _db.ThuongHieus.AnyAsync(x => x.MaThuongHieu == model.MaThuongHieu))
            ModelState.AddModelError(nameof(model.MaThuongHieu), "Thương hiệu không hợp lệ.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); return View(model); }

        var imagePath = await SaveImageAsync(model.ImageFile!);
        var product = new SanPham {
            TenSanPham = model.TenSanPham.Trim(), MaDanhMuc = model.MaDanhMuc,
            MaThuongHieu = model.MaThuongHieu ?? 0, GiaGoc = model.GiaGoc,
            BaoHanh = model.BaoHanh?.Trim(), MoTa = model.MoTa?.Trim(),
            ThongSoKyThuat = model.ThongSoKyThuat?.Trim(), TrangThaiSanPham = "Còn hàng"
        };
        foreach (var variant in model.BienThe)
        {
            product.BienTheSanPhams.Add(new BienTheSanPham
            {
                MauSac = variant.MauSac?.Trim(),
                PhienBan = variant.PhienBan?.Trim(),
                Gia = variant.Gia,
                SoLuong = variant.SoLuong,
                TrangThai = variant.SoLuong > 0 ? "Còn hàng" : "Hết hàng"
            });
        }
        product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
        {
            DuongDanAnh = imagePath,
            LaAnhChinh = true,
            ThuTu = 0
        });
        _db.SanPhams.Add(product);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm sản phẩm mới.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.SanPhams.Include(x => x.BienTheSanPhams).Include(x => x.SanPhamHinhAnhs).FirstOrDefaultAsync(x => x.MaSanPham == id);
        if (product == null) return NotFound();
        await LoadSelectionsAsync();
        var variants = product.BienTheSanPhams.Select(variant => new ProductVariantQuantityViewModel
        {
            MaBienThe = variant.MaBienThe,
            MauSac = variant.MauSac,
            PhienBan = variant.PhienBan,
            Gia = variant.Gia,
            SoLuong = variant.SoLuong
        }).ToList();
        if (variants.Count == 0)
            variants.Add(new ProductVariantQuantityViewModel());

        return View(new ProductCreateViewModel
        {
            TenSanPham = product.TenSanPham ?? string.Empty,
            MaDanhMuc = product.MaDanhMuc, MaThuongHieu = product.MaThuongHieu,
            GiaGoc = product.GiaGoc, BaoHanh = product.BaoHanh,
            MoTa = product.MoTa, ThongSoKyThuat = product.ThongSoKyThuat,
            CurrentImageUrl = product.SanPhamHinhAnhs.OrderByDescending(x => x.LaAnhChinh).ThenBy(x => x.ThuTu).Select(x => x.DuongDanAnh).FirstOrDefault(),
            BienThe = variants
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductCreateViewModel model)
    {
        var product = await _db.SanPhams.Include(x => x.BienTheSanPhams).Include(x => x.SanPhamHinhAnhs).FirstOrDefaultAsync(x => x.MaSanPham == id);
        if (product == null) return NotFound();
        ValidateImage(model.ImageFile, required: false);
        if (model.BienThe.Count == 0)
            ModelState.AddModelError(nameof(model.BienThe), "Sản phẩm phải có ít nhất một biến thể.");
        if (model.MaThuongHieu is null || !await _db.ThuongHieus.AnyAsync(x => x.MaThuongHieu == model.MaThuongHieu))
            ModelState.AddModelError(nameof(model.MaThuongHieu), "Thương hiệu không hợp lệ.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); return View(model); }
        product.TenSanPham = model.TenSanPham.Trim(); product.MaDanhMuc = model.MaDanhMuc;
        product.MaThuongHieu = model.MaThuongHieu ?? product.MaThuongHieu; product.GiaGoc = model.GiaGoc;
        product.BaoHanh = model.BaoHanh?.Trim(); product.MoTa = model.MoTa?.Trim(); product.ThongSoKyThuat = model.ThongSoKyThuat?.Trim();
        foreach (var variantModel in model.BienThe)
        {
            var variant = product.BienTheSanPhams.FirstOrDefault(x => x.MaBienThe == variantModel.MaBienThe);
            if (variant != null)
            {
                variant.MauSac = variantModel.MauSac?.Trim();
                variant.PhienBan = variantModel.PhienBan?.Trim();
                variant.Gia = variantModel.Gia;
                variant.SoLuong = variantModel.SoLuong;
                variant.TrangThai = variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng";
            }
            else if (variantModel.MaBienThe == 0)
            {
                product.BienTheSanPhams.Add(new BienTheSanPham
                {
                    MauSac = variantModel.MauSac?.Trim(),
                    PhienBan = variantModel.PhienBan?.Trim(),
                    Gia = variantModel.Gia,
                    SoLuong = variantModel.SoLuong,
                    TrangThai = variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng"
                });
            }
        }
        if (model.ImageFile is { Length: > 0 })
        {
            var imagePath = await SaveImageAsync(model.ImageFile);
            foreach (var image in product.SanPhamHinhAnhs) image.LaAnhChinh = false;
            product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
            {
                DuongDanAnh = imagePath,
                LaAnhChinh = true,
                ThuTu = product.SanPhamHinhAnhs.Count
            });
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

    private void ValidateImage(IFormFile? image, bool required)
    {
        if (required && (image == null || image.Length == 0))
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Vui lòng chọn ảnh sản phẩm.");
            return;
        }
        if (image == null || image.Length == 0) return;
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension))
            ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Ảnh chỉ nhận JPG, JPEG, PNG hoặc WEBP.");
        if (image.Length > 5 * 1024 * 1024)
            ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Ảnh không được vượt quá 5 MB.");
    }

    private async Task<string> SaveImageAsync(IFormFile image)
    {
        var folder = Path.Combine(_environment.WebRootPath, "uploads", "products");
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create);
        await image.CopyToAsync(stream);
        return $"/uploads/products/{fileName}";
    }
}
