using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels; // Dùng duy nhất ViewModel tại đây
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EMua.Areas.Staff.Models;
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
        var query = _db.SanPhams.AsNoTracking()
            .Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.TenSanPham != null && x.TenSanPham.Contains(q.Trim()));
        if (!string.IsNullOrWhiteSpace(status) && status != "all") query = query.Where(x => x.TrangThaiSanPham == status);
        if (brandId.HasValue) query = query.Where(x => x.MaThuongHieu == brandId.Value);

        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.BrandId = brandId;
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
        var allFiles = GetUploadedFiles(model);
        ValidateImages(allFiles, true);
        ValidateBrand(model);

        if (model.BienThe.Count == 0)
            ModelState.AddModelError(nameof(model.BienThe), "Vui lòng nhập ít nhất một biến thể.");

        if (!ModelState.IsValid)
        {
            await LoadSelectionsAsync();
            return View(model);
        }

        var product = new SanPham
        {
            TenSanPham = model.TenSanPham.Trim(),
            MaDanhMuc = model.MaDanhMuc,
            MaThuongHieu = model.MaThuongHieu!.Value,
            GiaGoc = model.GiaGoc,
            BaoHanh = model.BaoHanh?.Trim(),
            MoTa = model.MoTa?.Trim(),
            ThongSoKyThuat = model.ThongSoKyThuat?.Trim(),
            TrangThaiSanPham = "Còn hàng"
        };

        // 1. Lưu toàn bộ danh sách ảnh upload
        string? mainImagePath = null;
        if (allFiles.Count > 0)
        {
            for (int i = 0; i < allFiles.Count; i++)
            {
                var savedPath = await SaveImageAsync(allFiles[i]);
                bool isMain = (i == 0);
                if (isMain) mainImagePath = savedPath;

                product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
                {
                    DuongDanAnh = savedPath,
                    LaAnhChinh = isMain,
                    ThuTu = i
                });
            }
        }

        // 2. Gán đường dẫn ảnh chính vào các biến thể để tránh lỗi null
        AddVariants(product, model.BienThe, mainImagePath);

        _db.SanPhams.Add(product);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã thêm sản phẩm mới thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await _db.SanPhams
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == id);

        if (product == null) return NotFound();

        await LoadSelectionsAsync();
        return View(ToEditModel(product));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMainImage(int productId, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return Json(new { success = false, message = "Đường dẫn ảnh không hợp lệ." });

        var product = await _db.SanPhams
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == productId);

        if (product == null)
            return Json(new { success = false, message = "Không tìm thấy sản phẩm." });

        // Cập nhật lại cờ LaAnhChinh trong CSDL
        foreach (var img in product.SanPhamHinhAnhs)
        {
            img.LaAnhChinh = (img.DuongDanAnh != null && img.DuongDanAnh.Equals(imageUrl, StringComparison.OrdinalIgnoreCase));
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "Đã cập nhật ảnh chính thành công!" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductCreateViewModel model)
    {
        var product = await _db.SanPhams
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == id);

        if (product == null) return NotFound();

        var allFiles = GetUploadedFiles(model);
        ValidateImages(allFiles, false);
        ValidateBrand(model);

        if (model.BienThe == null || model.BienThe.Count == 0)
        {
            ModelState.AddModelError(nameof(model.BienThe), "Sản phẩm phải có ít nhất một biến thể.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectionsAsync();
            model.CurrentImageUrl = CurrentImage(product);
            model.CurrentImageUrls = product.SanPhamHinhAnhs.OrderBy(x => x.ThuTu).Select(x => x.DuongDanAnh!).ToList();
            return View(model);
        }

        // 1. Cập nhật thông tin chung sản phẩm
        product.TenSanPham = model.TenSanPham.Trim();
        product.MaDanhMuc = model.MaDanhMuc;
        product.MaThuongHieu = model.MaThuongHieu!.Value;
        product.GiaGoc = model.GiaGoc;
        product.BaoHanh = model.BaoHanh?.Trim();
        product.MoTa = model.MoTa?.Trim();
        product.ThongSoKyThuat = model.ThongSoKyThuat?.Trim();

        // 2. Xử lý lưu thêm ảnh mới upload (nếu có)
        string? newUploadedMainPath = null;
        if (allFiles.Count > 0)
        {
            for (int i = 0; i < allFiles.Count; i++)
            {
                var savedPath = await SaveImageAsync(allFiles[i]);
                bool isMain = (i == 0 && string.IsNullOrEmpty(model.SelectedMainImageUrl));
                if (isMain) newUploadedMainPath = savedPath;

                product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
                {
                    DuongDanAnh = savedPath,
                    LaAnhChinh = isMain,
                    ThuTu = product.SanPhamHinhAnhs.Count + i
                });
            }
        }

        // 3. CẬP NHẬT ĐẶT LẠI CỜ "Ảnh chính" (LaAnhChinh) TRONG CSDL
        string? targetMainImage = !string.IsNullOrEmpty(model.SelectedMainImageUrl)
            ? model.SelectedMainImageUrl
            : newUploadedMainPath;

        if (!string.IsNullOrEmpty(targetMainImage))
        {
            foreach (var img in product.SanPhamHinhAnhs)
            {
                img.LaAnhChinh = (img.DuongDanAnh != null && img.DuongDanAnh.Equals(targetMainImage, StringComparison.OrdinalIgnoreCase));
            }
        }

        string defaultVariantImage = targetMainImage ?? CurrentImage(product) ?? "/images/no-image.png";

        // 4. XÓA các biến thể cũ không còn nằm trong danh sách truyền lên từ View
        var submittedVariantIds = model.BienThe.Select(x => x.MaBienThe).Where(vId => vId > 0).ToList();
        var variantsToRemove = product.BienTheSanPhams.Where(x => !submittedVariantIds.Contains(x.MaBienThe)).ToList();
        if (variantsToRemove.Count > 0)
        {
            _db.BienTheSanPhams.RemoveRange(variantsToRemove);
        }

        // 5. CẬP NHẬT hoặc THÊM MỚI biến thể
        foreach (var variantModel in model.BienThe)
        {
            if (variantModel.MaBienThe > 0)
            {
                var existingVariant = product.BienTheSanPhams.FirstOrDefault(x => x.MaBienThe == variantModel.MaBienThe);
                if (existingVariant != null)
                {
                    existingVariant.MauSac = variantModel.MauSac?.Trim();
                    existingVariant.PhienBan = variantModel.PhienBan?.Trim();
                    existingVariant.Gia = variantModel.Gia;
                    existingVariant.SoLuong = variantModel.SoLuong;
                    existingVariant.TrangThai = variantModel.TrangThai ?? (variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng");
                    if (!string.IsNullOrEmpty(defaultVariantImage)) existingVariant.HinhAnh = defaultVariantImage;
                }
            }
            else
            {
                product.BienTheSanPhams.Add(new BienTheSanPham
                {
                    MauSac = variantModel.MauSac?.Trim(),
                    PhienBan = variantModel.PhienBan?.Trim(),
                    Gia = variantModel.Gia,
                    SoLuong = variantModel.SoLuong,
                    HinhAnh = defaultVariantImage,
                    TrangThai = variantModel.TrangThai ?? (variantModel.SoLuong > 0 ? "Còn hàng" : "Hết hàng")
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật sản phẩm và danh sách biến thể thành công.";
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

    private static void AddVariants(SanPham product, IEnumerable<ProductVariantQuantityViewModel> variants, string? imageUrl = null)
    {
        foreach (var variant in variants)
        {
            product.BienTheSanPhams.Add(new BienTheSanPham
            {
                MauSac = variant.MauSac?.Trim(),
                PhienBan = variant.PhienBan?.Trim(),
                Gia = variant.Gia,
                SoLuong = variant.SoLuong,
                HinhAnh = imageUrl,
                TrangThai = variant.TrangThai ?? (variant.SoLuong > 0 ? "Còn hàng" : "Hết hàng")
            });
        }
    }

    private List<IFormFile> GetUploadedFiles(ProductCreateViewModel model)
    {
        var list = new List<IFormFile>();
        if (model.ImageFile != null && model.ImageFile.Length > 0)
        {
            list.Add(model.ImageFile);
        }
        if (model.ImageFiles != null && model.ImageFiles.Count > 0)
        {
            list.AddRange(model.ImageFiles.Where(x => x.Length > 0));
        }
        return list;
    }

    private void ValidateImages(List<IFormFile> images, bool required)
    {
        if (required && images.Count == 0)
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Vui lòng chọn ít nhất một ảnh sản phẩm.");
            return;
        }

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        foreach (var img in images)
        {
            var ext = Path.GetExtension(img.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), $"File {img.FileName} không đúng định dạng JPG, PNG hoặc WEBP.");
            }
            if (img.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), $"File {img.FileName} vượt quá dung lượng 5 MB.");
            }
        }
    }

    private ProductCreateViewModel ToEditModel(SanPham product)
    {
        var mainImg = CurrentImage(product);
        return new ProductCreateViewModel
        {
            TenSanPham = product.TenSanPham ?? string.Empty,
            MaDanhMuc = product.MaDanhMuc,
            MaThuongHieu = product.MaThuongHieu,
            GiaGoc = product.GiaGoc,
            BaoHanh = product.BaoHanh,
            MoTa = product.MoTa,
            ThongSoKyThuat = product.ThongSoKyThuat,
            CurrentImageUrl = mainImg,
            SelectedMainImageUrl = mainImg,
            CurrentImageUrls = product.SanPhamHinhAnhs.OrderBy(x => x.ThuTu).Select(x => x.DuongDanAnh!).ToList(),
            BienThe = product.BienTheSanPhams.Select(x => new ProductVariantQuantityViewModel
            {
                MaBienThe = x.MaBienThe,
                MauSac = x.MauSac,
                PhienBan = x.PhienBan,
                Gia = x.Gia,
                SoLuong = x.SoLuong,
                TrangThai = string.IsNullOrEmpty(x.TrangThai) ? "Còn hàng" : x.TrangThai
            }).ToList()
        };
    }

    private static string? CurrentImage(SanPham product) => product.SanPhamHinhAnhs
        .OrderByDescending(x => x.LaAnhChinh)
        .ThenBy(x => x.ThuTu)
        .Select(x => x.DuongDanAnh)
        .FirstOrDefault();

    private async Task LoadSelectionsAsync()
    {
        ViewBag.Categories = new SelectList(await _db.DanhMucSanPhams.AsNoTracking().OrderBy(x => x.TenDanhMuc).ToListAsync(), "MaDanhMuc", "TenDanhMuc");
        ViewBag.Brands = new SelectList(await _db.ThuongHieus.AsNoTracking().Where(x => x.TrangThai).OrderBy(x => x.TenThuongHieu).ToListAsync(), "MaThuongHieu", "TenThuongHieu");
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