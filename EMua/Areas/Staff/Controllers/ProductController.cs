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
            .Include(x => x.MaThuongHieuNavigation).Include(x => x.BienTheSanPhams).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.TenSanPham != null && x.TenSanPham.Contains(q.Trim()));
        if (!string.IsNullOrWhiteSpace(status) && status != "all") query = query.Where(x => x.TrangThaiSanPham == status);
        if (brandId.HasValue) query = query.Where(x => x.MaThuongHieu == brandId.Value);
        ViewBag.Search = q; ViewBag.Status = status ?? "all"; ViewBag.BrandId = brandId;
        ViewBag.Statuses = await _db.SanPhams.AsNoTracking().Select(x => x.TrangThaiSanPham ?? "Chưa xác định").Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Brands = await _db.ThuongHieus.AsNoTracking().OrderBy(x => x.TenThuongHieu).ToListAsync();
        return View(await query.OrderByDescending(x => x.MaSanPham).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadSelectionsAsync();
        return View(new ProductCreateViewModel { BienThe = [new ProductVariantQuantityViewModel()] });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateViewModel model)
    {
        ValidateImage(model.ImageFile, true);
        ValidateBrand(model);
        if (model.BienThe.Count == 0) ModelState.AddModelError(nameof(model.BienThe), "Vui lòng nhập ít nhất một biến thể.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); return View(model); }

        var product = new SanPham
        {
            TenSanPham = model.TenSanPham.Trim(), MaDanhMuc = model.MaDanhMuc,
            MaThuongHieu = model.MaThuongHieu!.Value, GiaGoc = model.GiaGoc,
            BaoHanh = model.BaoHanh?.Trim(), MoTa = model.MoTa?.Trim(),
            ThongSoKyThuat = model.ThongSoKyThuat?.Trim(), TrangThaiSanPham = "Còn hàng"
        };
        AddVariants(product, model.BienThe);
        product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh { DuongDanAnh = await SaveImageAsync(model.ImageFile!), LaAnhChinh = true, ThuTu = 0 });
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
        return View(ToEditModel(product));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductCreateViewModel model)
    {
        var product = await _db.SanPhams.Include(x => x.BienTheSanPhams).Include(x => x.SanPhamHinhAnhs).FirstOrDefaultAsync(x => x.MaSanPham == id);
        if (product == null) return NotFound();
        ValidateImage(model.ImageFile, false);
        ValidateBrand(model);
        if (model.BienThe.Count == 0) ModelState.AddModelError(nameof(model.BienThe), "Sản phẩm phải có ít nhất một biến thể.");
        if (!ModelState.IsValid) { await LoadSelectionsAsync(); model.CurrentImageUrl = CurrentImage(product); return View(model); }

        product.TenSanPham = model.TenSanPham.Trim(); product.MaDanhMuc = model.MaDanhMuc;
        product.MaThuongHieu = model.MaThuongHieu!.Value; product.GiaGoc = model.GiaGoc;
        product.BaoHanh = model.BaoHanh?.Trim(); product.MoTa = model.MoTa?.Trim(); product.ThongSoKyThuat = model.ThongSoKyThuat?.Trim();
        foreach (var variantModel in model.BienThe)
        {
            var variant = product.BienTheSanPhams.FirstOrDefault(x => x.MaBienThe == variantModel.MaBienThe);
            if (variant == null)
            {
                product.BienTheSanPhams.Add(new BienTheSanPham { MauSac = variantModel.MauSac?.Trim(), PhienBan = variantModel.PhienBan?.Trim(), Gia = variantModel.Gia, SoLuong = variantModel.SoLuong, TrangThai = variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng" });
            }
            else
            {
                variant.MauSac = variantModel.MauSac?.Trim(); variant.PhienBan = variantModel.PhienBan?.Trim();
                variant.Gia = variantModel.Gia; variant.SoLuong = variantModel.SoLuong;
                variant.TrangThai = variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng";
            }
        }
        if (model.ImageFile is { Length: > 0 })
        {
            foreach (var image in product.SanPhamHinhAnhs) image.LaAnhChinh = false;
            product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh { DuongDanAnh = await SaveImageAsync(model.ImageFile), LaAnhChinh = true, ThuTu = product.SanPhamHinhAnhs.Count });
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật sản phẩm.";
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

    private void ValidateBrand(ProductCreateViewModel model)
    {
        if (model.MaThuongHieu is null) ModelState.AddModelError(nameof(model.MaThuongHieu), "Vui lòng chọn thương hiệu.");
    }

    private static void AddVariants(SanPham product, IEnumerable<ProductVariantQuantityViewModel> variants)
    {
        foreach (var variant in variants)
            product.BienTheSanPhams.Add(new BienTheSanPham { MauSac = variant.MauSac?.Trim(), PhienBan = variant.PhienBan?.Trim(), Gia = variant.Gia, SoLuong = variant.SoLuong, TrangThai = variant.SoLuong > 0 ? "Còn hàng" : "Hết hàng" });
    }

    private ProductCreateViewModel ToEditModel(SanPham product) => new()
    {
        TenSanPham = product.TenSanPham ?? string.Empty, MaDanhMuc = product.MaDanhMuc, MaThuongHieu = product.MaThuongHieu,
        GiaGoc = product.GiaGoc, BaoHanh = product.BaoHanh, MoTa = product.MoTa, ThongSoKyThuat = product.ThongSoKyThuat,
        CurrentImageUrl = CurrentImage(product), BienThe = product.BienTheSanPhams.Select(x => new ProductVariantQuantityViewModel { MaBienThe = x.MaBienThe, MauSac = x.MauSac, PhienBan = x.PhienBan, Gia = x.Gia, SoLuong = x.SoLuong }).ToList()
    };

    private static string? CurrentImage(SanPham product) => product.SanPhamHinhAnhs.OrderByDescending(x => x.LaAnhChinh).ThenBy(x => x.ThuTu).Select(x => x.DuongDanAnh).FirstOrDefault();

    private async Task LoadSelectionsAsync()
    {
        ViewBag.Categories = new SelectList(await _db.DanhMucSanPhams.AsNoTracking().OrderBy(x => x.TenDanhMuc).ToListAsync(), "MaDanhMuc", "TenDanhMuc");
        ViewBag.Brands = new SelectList(await _db.ThuongHieus.AsNoTracking().Where(x => x.TrangThai).OrderBy(x => x.TenThuongHieu).ToListAsync(), "MaThuongHieu", "TenThuongHieu");
    }

    private void ValidateImage(IFormFile? image, bool required)
    {
        if (required && (image == null || image.Length == 0)) ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Vui lòng chọn ảnh sản phẩm.");
        if (image is { Length: > 0 })
        {
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(Path.GetExtension(image.FileName).ToLowerInvariant())) ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Ảnh phải là JPG, PNG hoặc WEBP.");
            if (image.Length > 5 * 1024 * 1024) ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Ảnh không được vượt quá 5 MB.");
        }
    }

    private async Task<string> SaveImageAsync(IFormFile image)
    {
        var folder = Path.Combine(_environment.WebRootPath, "images", "products");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(image.FileName).ToLowerInvariant()}";
        await using var stream = System.IO.File.Create(Path.Combine(folder, fileName));
        await image.CopyToAsync(stream);
        return $"/images/products/{fileName}";
    }
}
