using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EMua.Areas.Staff.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

    public async Task<IActionResult> Index(string? q, string? status, int? brandId, int? categoryId, string? stockFilter, int page = 1, int pageSize = 12)
    {
        var query = _db.SanPhams.AsNoTracking()
            .Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim().ToLower();
            query = query.Where(x => (x.TenSanPham != null && x.TenSanPham.ToLower().Contains(search))
                || x.MaSanPham.ToString().Contains(search)
                || (x.MoTa != null && x.MoTa.ToLower().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            query = query.Where(x => x.TrangThaiSanPham == status);
        }

        if (brandId.HasValue && brandId.Value > 0)
        {
            query = query.Where(x => x.MaThuongHieu == brandId.Value);
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(x => x.MaDanhMuc == categoryId.Value);
        }

        var allProducts = await query.OrderByDescending(x => x.MaSanPham).ToListAsync();

        // Lọc tồn kho ở cấp độ sản phẩm
        if (!string.IsNullOrWhiteSpace(stockFilter) && stockFilter != "all")
        {
            if (stockFilter == "out")
            {
                allProducts = allProducts.Where(p => p.BienTheSanPhams.Sum(b => b.SoLuong) == 0).ToList();
            }
            else if (stockFilter == "low")
            {
                allProducts = allProducts.Where(p => {
                    var s = p.BienTheSanPhams.Sum(b => b.SoLuong);
                    return s > 0 && s <= 10;
                }).ToList();
            }
            else if (stockFilter == "in")
            {
                allProducts = allProducts.Where(p => p.BienTheSanPhams.Sum(b => b.SoLuong) > 10).ToList();
            }
        }

        int totalItems = allProducts.Count;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var pagedItems = allProducts
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.BrandId = brandId;
        ViewBag.CategoryId = categoryId;
        ViewBag.StockFilter = stockFilter ?? "all";
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        ViewBag.PageSize = pageSize;

        ViewBag.Statuses = await _db.SanPhams.AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.TrangThaiSanPham))
            .Select(x => x.TrangThaiSanPham!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        ViewBag.Brands = await _db.ThuongHieus.AsNoTracking().OrderBy(x => x.TenThuongHieu).ToListAsync();
        ViewBag.Categories = await _db.DanhMucSanPhams.AsNoTracking().OrderBy(x => x.TenDanhMuc).ToListAsync();

        return View(pagedItems);
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
                    ThuTu = i + 1
                });
            }
        }

        AddVariants(product, model.BienThe, mainImagePath);

        _db.SanPhams.Add(product);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã tạo sản phẩm thành công.";
        return RedirectToAction(nameof(Index));
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

        foreach (var img in product.SanPhamHinhAnhs)
        {
            img.LaAnhChinh = (img.DuongDanAnh != null && img.DuongDanAnh.Equals(imageUrl, StringComparison.OrdinalIgnoreCase));
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "Đã cập nhật ảnh chính thành công." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int productId, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return Json(new { success = false, message = "Đường dẫn hình ảnh không hợp lệ." });

        var product = await _db.SanPhams
            .Include(x => x.SanPhamHinhAnhs)
            .Include(x => x.BienTheSanPhams)
            .FirstOrDefaultAsync(x => x.MaSanPham == productId);

        if (product == null)
            return Json(new { success = false, message = "Không tìm thấy sản phẩm." });

        var normalizedUrl = imageUrl.Trim().Replace("\\", "/");
        var imgEntity = product.SanPhamHinhAnhs.FirstOrDefault(x =>
            x.DuongDanAnh != null && (
                x.DuongDanAnh.Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                x.DuongDanAnh.EndsWith(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.EndsWith(x.DuongDanAnh, StringComparison.OrdinalIgnoreCase)
            ));

        bool wasMain = false;
        if (imgEntity != null)
        {
            wasMain = imgEntity.LaAnhChinh;
            _db.SanPhamHinhAnhs.Remove(imgEntity);
        }

        foreach (var variant in product.BienTheSanPhams)
        {
            if (!string.IsNullOrEmpty(variant.HinhAnh) && (
                variant.HinhAnh.Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                variant.HinhAnh.EndsWith(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.EndsWith(variant.HinhAnh, StringComparison.OrdinalIgnoreCase)))
            {
                variant.HinhAnh = null;
            }
        }

        var remainingImages = product.SanPhamHinhAnhs.Where(x => x != imgEntity).OrderBy(x => x.ThuTu).ToList();
        if (remainingImages.Count > 0 && (wasMain || !remainingImages.Any(x => x.LaAnhChinh)))
        {
            foreach (var r in remainingImages)
            {
                r.LaAnhChinh = false;
            }
            remainingImages[0].LaAnhChinh = true;
        }

        try
        {
            var relativePath = normalizedUrl.TrimStart('/');
            var physicalPath = Path.Combine(_environment.WebRootPath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
            }
        }
        catch
        {
            // Bỏ qua lỗi xóa file vật lý
        }

        await _db.SaveChangesAsync();
        return Json(new
        {
            success = true,
            message = "Đã xóa hình ảnh thành công.",
            newMainImageUrl = remainingImages.FirstOrDefault(x => x.LaAnhChinh)?.DuongDanAnh
        });
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

        // Lấy danh sách biến thể đang có phiếu đề nghị nhập hàng "Chờ duyệt"
        var variantIds = product.BienTheSanPhams.Select(b => b.MaBienThe).ToList();
        var pendingImports = await _db.ChiTietPhieuNhapHangs
            .Include(ct => ct.MaPhieuNhapNavigation)
            .Where(ct => variantIds.Contains(ct.MaBienThe) && ct.MaPhieuNhapNavigation.TrangThai == "Chờ duyệt")
            .Select(ct => new { ct.MaBienThe, ct.MaPhieuNhapNavigation.MaChungTu, ct.SoLuongNhap })
            .ToListAsync();

        ViewBag.PendingImports = pendingImports.ToDictionary(
            x => x.MaBienThe,
            x => $"Phiếu {x.MaChungTu} (+{x.SoLuongNhap})"
        );

        return View(ToEditModel(product));
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

        if (!ModelState.IsValid)
        {
            await LoadSelectionsAsync();
            model.CurrentImageUrl = CurrentImage(product);
            model.CurrentImageUrls = product.SanPhamHinhAnhs.OrderBy(x => x.ThuTu).Select(x => x.DuongDanAnh!).ToList();
            return View(model);
        }

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
            int currentMaxOrder = product.SanPhamHinhAnhs.Any() ? product.SanPhamHinhAnhs.Max(x => x.ThuTu) : 0;
            for (int i = 0; i < allFiles.Count; i++)
            {
                var savedPath = await SaveImageAsync(allFiles[i]);
                bool isMain = (i == 0 && string.IsNullOrEmpty(model.SelectedMainImageUrl));
                if (isMain) newUploadedMainPath = savedPath;

                product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
                {
                    DuongDanAnh = savedPath,
                    LaAnhChinh = isMain,
                    ThuTu = currentMaxOrder + i + 1
                });
            }
        }

        // 3. Cập nhật cờ LaAnhChinh theo ảnh mà người dùng đã chọn
        string? activeMainImg = model.SelectedMainImageUrl ?? newUploadedMainPath;
        if (!string.IsNullOrEmpty(activeMainImg))
        {
            foreach (var img in product.SanPhamHinhAnhs)
            {
                img.LaAnhChinh = (img.DuongDanAnh != null && img.DuongDanAnh.Equals(activeMainImg, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Cập nhật biến thể
        var incomingIds = model.BienThe.Where(b => b.MaBienThe > 0).Select(b => b.MaBienThe).ToList();
        var toRemove = product.BienTheSanPhams.Where(b => !incomingIds.Contains(b.MaBienThe)).ToList();
        _db.BienTheSanPhams.RemoveRange(toRemove);

        foreach (var vm in model.BienThe)
        {
            if (vm.MaBienThe > 0)
            {
                var existing = product.BienTheSanPhams.FirstOrDefault(b => b.MaBienThe == vm.MaBienThe);
                if (existing != null)
                {
                    existing.MauSac = vm.MauSac?.Trim();
                    existing.PhienBan = vm.PhienBan?.Trim();
                    existing.Gia = vm.Gia;
                    // Không cho Staff sửa trực tiếp tồn kho của biến thể đã có để đảm bảo quy trình nhập hàng
                    existing.TrangThai = string.IsNullOrEmpty(vm.TrangThai) ? (existing.SoLuong > 0 ? "Còn hàng" : "Hết hàng") : vm.TrangThai;
                }
            }
            else
            {
                product.BienTheSanPhams.Add(new BienTheSanPham
                {
                    MauSac = vm.MauSac?.Trim(),
                    PhienBan = vm.PhienBan?.Trim(),
                    Gia = vm.Gia,
                    SoLuong = vm.SoLuong,
                    TrangThai = string.IsNullOrEmpty(vm.TrangThai) ? (vm.SoLuong > 0 ? "Còn hàng" : "Hết hàng") : vm.TrangThai,
                    HinhAnh = activeMainImg ?? CurrentImage(product)
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật sản phẩm thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVisibility(int id)
    {
        var product = await _db.SanPhams.FindAsync(id);
        if (product == null) return NotFound();

        product.TrangThaiSanPham = (product.TrangThaiSanPham == "Ẩn") ? "Còn hàng" : "Ẩn";
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã {(product.TrangThaiSanPham == "Ẩn" ? "ẩn" : "hiển thị")} sản phẩm #SP-{id:D4}.";
        return RedirectToAction(nameof(Index));
    }

    private static List<IFormFile> GetUploadedFiles(ProductCreateViewModel model)
    {
        var list = new List<IFormFile>();
        if (model.ImageFile != null && model.ImageFile.Length > 0)
            list.Add(model.ImageFile);
        if (model.ImageFiles != null)
        {
            foreach (var f in model.ImageFiles)
            {
                if (f != null && f.Length > 0 && !list.Any(x => x.FileName == f.FileName && x.Length == f.Length))
                    list.Add(f);
            }
        }
        return list;
    }

    private void ValidateBrand(ProductCreateViewModel model)
    {
        if (!model.MaThuongHieu.HasValue)
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.MaThuongHieu), "Vui lòng chọn thương hiệu.");
        }
    }

    private static void AddVariants(SanPham product, List<ProductVariantQuantityViewModel> variants, string? mainImagePath)
    {
        foreach (var item in variants)
        {
            product.BienTheSanPhams.Add(new BienTheSanPham
            {
                MauSac = item.MauSac?.Trim(),
                PhienBan = item.PhienBan?.Trim(),
                Gia = item.Gia,
                SoLuong = item.SoLuong,
                TrangThai = item.SoLuong > 0 ? "Còn hàng" : "Hết hàng",
                HinhAnh = mainImagePath
            });
        }
    }

    private void ValidateImages(List<IFormFile> files, bool isCreate)
    {
        if (isCreate && files.Count == 0)
        {
            ModelState.AddModelError(nameof(ProductCreateViewModel.ImageFile), "Vui lòng chọn ít nhất một ảnh cho sản phẩm.");
            return;
        }

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        foreach (var img in files)
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
        .FirstOrDefault()
        ?? product.BienTheSanPhams
        .Where(x => !string.IsNullOrEmpty(x.HinhAnh))
        .Select(x => x.HinhAnh)
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
