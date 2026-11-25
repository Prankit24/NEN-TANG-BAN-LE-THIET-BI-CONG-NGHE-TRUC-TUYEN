using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using EMua.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _cache;
    private readonly IUndoDeletionManager _undoManager;
    private readonly IProductImageStorageService _storageService;

    public ProductController(
        EMuaDbContext db,
        IWebHostEnvironment environment,
        IMemoryCache cache,
        IUndoDeletionManager undoManager,
        IProductImageStorageService storageService)
    {
        _db = db;
        _environment = environment;
        _cache = cache;
        _undoManager = undoManager;
        _storageService = storageService;
    }

    private void InvalidateProductCache()
    {
        _cache.Remove(AppCacheKeys.Home);
        _cache.Remove(AppCacheKeys.FeaturedProducts);
    }

    public async Task<IActionResult> Index(
        string? q,
        string? status,
        int? brandId,
        int? categoryId,
        string? stockFilter,
        string? sort = "id-asc",
        int page = 1,
        int pageSize = 12,
        string? imageFilter = null)
    {
        var query = _db.SanPhams.AsNoTracking()
            .Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var searchUpper = search.ToUpperInvariant();

            int? exactProductId = null;
            if (searchUpper.StartsWith("#SP-") || searchUpper.StartsWith("SP-"))
            {
                var numPart = searchUpper.Replace("#SP-", "").Replace("SP-", "").Trim();
                if (int.TryParse(numPart, out var parsed)) exactProductId = parsed;
            }
            else if (int.TryParse(search, out var parsedNum))
            {
                exactProductId = parsedNum;
            }

            var pattern = $"%{search}%";

            query = query.Where(x =>
                (exactProductId.HasValue && x.MaSanPham == exactProductId.Value)
                || (x.TenSanPham != null && EF.Functions.ILike(x.TenSanPham, pattern))
                || (x.MoTa != null && EF.Functions.ILike(x.MoTa, pattern))
                || (x.ThongSoKyThuat != null && EF.Functions.ILike(x.ThongSoKyThuat, pattern)));
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

        var allProducts = await query.ToListAsync();

        int missingImageCount = allProducts.Count(x => CountValidImages(x) == 0);

        // Lọc theo trạng thái hình ảnh (11h tối truy cập ?imageFilter=missing để xem các SP còn sót)
        if (string.Equals(imageFilter, "missing", StringComparison.OrdinalIgnoreCase))
        {
            allProducts = allProducts.Where(x => CountValidImages(x) == 0).ToList();
        }
        else if (string.Equals(imageFilter, "has-image", StringComparison.OrdinalIgnoreCase))
        {
            allProducts = allProducts.Where(x => CountValidImages(x) > 0).ToList();
        }

        // Mặc định (id-asc): FIX CỨNG thứ tự theo Mã SP tăng dần (#SP-0001 -> hết), thêm ảnh xong vẫn đứng yên tại chỗ
        sort = string.IsNullOrWhiteSpace(sort) ? "id-asc" : sort.Trim();
        switch (sort)
        {
            case "newest":
                allProducts = allProducts.OrderByDescending(x => x.MaSanPham).ToList();
                break;
            case "name-asc":
                allProducts = allProducts.OrderBy(x => x.TenSanPham).ThenBy(x => x.MaSanPham).ToList();
                break;
            case "name-desc":
                allProducts = allProducts.OrderByDescending(x => x.TenSanPham).ThenBy(x => x.MaSanPham).ToList();
                break;
            case "price-asc":
                allProducts = allProducts.OrderBy(x => x.BienTheSanPhams.Min(b => (decimal?)b.Gia) ?? x.GiaGoc ?? 0).ThenBy(x => x.MaSanPham).ToList();
                break;
            case "price-desc":
                allProducts = allProducts.OrderByDescending(x => x.BienTheSanPhams.Max(b => (decimal?)b.Gia) ?? x.GiaGoc ?? 0).ThenBy(x => x.MaSanPham).ToList();
                break;
            case "oldest":
            case "id-asc":
            default:
                allProducts = allProducts.OrderBy(x => x.MaSanPham).ToList();
                break;
        }

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
        ViewBag.ImageFilter = imageFilter ?? "all";
        ViewBag.MissingImageCount = missingImageCount;
        ViewBag.Sort = sort;
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

        InvalidateProductCache();

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
        InvalidateProductCache();
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

        if (product.SanPhamHinhAnhs.Count <= 1)
        {
            return BadRequest(new
            {
                success = false,
                message = "Không thể xóa hình ảnh duy nhất còn lại của sản phẩm. Mỗi sản phẩm cần có ít nhất một hình ảnh."
            });
        }

        var normalizedUrl = imageUrl.Trim().Replace("\\", "/");
        var imgEntity = product.SanPhamHinhAnhs.FirstOrDefault(x =>
            x.DuongDanAnh != null && (
                x.DuongDanAnh.Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                x.DuongDanAnh.EndsWith(normalizedUrl, StringComparison.OrdinalIgnoreCase) ||
                normalizedUrl.EndsWith(x.DuongDanAnh, StringComparison.OrdinalIgnoreCase)
            ));

        if (imgEntity == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy hình ảnh tương ứng trong sản phẩm." });
        }

        bool wasMain = imgEntity.LaAnhChinh;
        int imgId = imgEntity.MaAnh;
        string savedImgUrl = imgEntity.DuongDanAnh!;
        int oldThuTu = imgEntity.ThuTu;

        _db.SanPhamHinhAnhs.Remove(imgEntity);

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

        await _db.SaveChangesAsync();
        InvalidateProductCache();

        var undoItem = _undoManager.RegisterPendingDeletion(
            DeletionTargetType.ProductImage,
            product.MaSanPham,
            product.TenSanPham,
            $"{savedImgUrl}|{wasMain}|{oldThuTu}",
            User);

        return Json(new
        {
            success = true,
            canUndo = true,
            undoToken = undoItem.Token,
            undoDurationSeconds = 60,
            message = "Đã xóa hình ảnh thành công. Bạn có 60 giây để hoàn tác.",
            newMainImageUrl = remainingImages.FirstOrDefault(x => x.LaAnhChinh)?.DuongDanAnh
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoDeleteImage([FromForm] string token)
    {
        if (!_undoManager.TryClaimUndo(token, User, out var item, out var errorMessage))
        {
            return BadRequest(new { success = false, message = errorMessage ?? "Không thể hoàn tác." });
        }

        var product = await _db.SanPhams
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == item!.EntityId);

        if (product == null)
        {
            return NotFound(new { success = false, message = "Sản phẩm không còn tồn tại." });
        }

        var parts = item.AdditionalData?.Split('|');
        if (parts == null || parts.Length < 3)
        {
            return BadRequest(new { success = false, message = "Dữ liệu phục hồi hình ảnh không hợp lệ." });
        }

        string imgUrl = parts[0];
        bool wasMain = bool.TryParse(parts[1], out var wm) && wm;
        int thuTu = int.TryParse(parts[2], out var tt) ? tt : 1;

        if (!product.SanPhamHinhAnhs.Any(x => x.DuongDanAnh == imgUrl))
        {
            if (wasMain)
            {
                foreach (var img in product.SanPhamHinhAnhs) img.LaAnhChinh = false;
            }

            product.SanPhamHinhAnhs.Add(new SanPhamHinhAnh
            {
                MaSanPham = product.MaSanPham,
                DuongDanAnh = imgUrl,
                LaAnhChinh = wasMain,
                ThuTu = thuTu
            });

            await _db.SaveChangesAsync();
            InvalidateProductCache();
        }

        return Json(new
        {
            success = true,
            message = "Đã hoàn tác và khôi phục hình ảnh sản phẩm thành công.",
            imageUrl = imgUrl,
            isMain = wasMain
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? returnUrl = null)
    {
        var product = await _db.SanPhams
            .AsNoTracking()
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == id);

        if (product == null) return NotFound();

        await LoadSelectionsAsync();

        var variantIds = product.BienTheSanPhams.Select(b => b.MaBienThe).ToList();
        var pendingImports = await _db.ChiTietPhieuNhapHangs
            .AsNoTracking()
            .Include(ct => ct.MaPhieuNhapNavigation)
            .Where(ct => variantIds.Contains(ct.MaBienThe) && ct.MaPhieuNhapNavigation.TrangThai == "Chờ duyệt")
            .Select(ct => new { ct.MaBienThe, ct.MaPhieuNhapNavigation.MaChungTu, ct.SoLuongNhap })
            .ToListAsync();

        ViewBag.PendingImports = pendingImports.GroupBy(x => x.MaBienThe).ToDictionary(
            group => group.Key,
            group => string.Join("; ", group.Select(x => $"Phiếu {x.MaChungTu} (+{x.SoLuongNhap})"))
        );

        var editModel = ToEditModel(product);
        editModel.ReturnUrl = returnUrl;
        return View(editModel);
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
            var placeholderImages = product.SanPhamHinhAnhs
                .Where(x => string.IsNullOrWhiteSpace(x.DuongDanAnh) || x.DuongDanAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (placeholderImages.Any())
            {
                _db.SanPhamHinhAnhs.RemoveRange(placeholderImages);
            }

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
        if (!string.IsNullOrEmpty(activeMainImg) && !activeMainImg.Contains("no-image", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var img in product.SanPhamHinhAnhs)
            {
                img.LaAnhChinh = (img.DuongDanAnh != null && img.DuongDanAnh.Equals(activeMainImg, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            var realImages = product.SanPhamHinhAnhs
                .Where(x => !string.IsNullOrWhiteSpace(x.DuongDanAnh) && !x.DuongDanAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.ThuTu)
                .ToList();
            if (realImages.Any() && !realImages.Any(x => x.LaAnhChinh))
            {
                realImages[0].LaAnhChinh = true;
            }
        }

        // Cập nhật biến thể
        var incomingIds = model.BienThe.Where(b => b.MaBienThe > 0).Select(b => b.MaBienThe).ToList();
        var toRemove = product.BienTheSanPhams.Where(b => !incomingIds.Contains(b.MaBienThe)).ToList();
        _db.BienTheSanPhams.RemoveRange(toRemove);

        var fallbackVariantImg = CurrentImage(product);
        if (fallbackVariantImg != null && fallbackVariantImg.Contains("no-image", StringComparison.OrdinalIgnoreCase))
        {
            fallbackVariantImg = null;
        }

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
                    if (!string.IsNullOrEmpty(existing.HinhAnh) && existing.HinhAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.HinhAnh = activeMainImg ?? fallbackVariantImg;
                    }
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
                    HinhAnh = activeMainImg ?? fallbackVariantImg
                });
            }
        }

        await _db.SaveChangesAsync();
        InvalidateProductCache();
        TempData["Success"] = "Đã cập nhật sản phẩm thành công.";

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

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
        InvalidateProductCache();

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
        var validImages = product.SanPhamHinhAnhs
            .Where(x => !string.IsNullOrWhiteSpace(x.DuongDanAnh) && !x.DuongDanAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ThuTu)
            .Select(x => x.DuongDanAnh!)
            .ToList();

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
            CurrentImageUrls = validImages,
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

    private static int CountValidImages(SanPham product)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (product.SanPhamHinhAnhs != null)
        {
            foreach (var img in product.SanPhamHinhAnhs)
            {
                if (!string.IsNullOrWhiteSpace(img.DuongDanAnh) &&
                    !img.DuongDanAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
                {
                    urls.Add(img.DuongDanAnh.Trim());
                }
            }
        }

        if (product.BienTheSanPhams != null)
        {
            foreach (var variant in product.BienTheSanPhams)
            {
                if (!string.IsNullOrWhiteSpace(variant.HinhAnh) &&
                    !variant.HinhAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
                {
                    urls.Add(variant.HinhAnh.Trim());
                }
            }
        }

        return urls.Count;
    }

    private static string? CurrentImage(SanPham product) => product.SanPhamHinhAnhs
        .Where(x => !string.IsNullOrWhiteSpace(x.DuongDanAnh) && !x.DuongDanAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.LaAnhChinh)
        .ThenBy(x => x.ThuTu)
        .Select(x => x.DuongDanAnh)
        .FirstOrDefault()
        ?? product.BienTheSanPhams
        .Where(x => !string.IsNullOrEmpty(x.HinhAnh) && !x.HinhAnh.Contains("no-image", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.HinhAnh)
        .FirstOrDefault();

    private async Task LoadSelectionsAsync()
    {
        ViewBag.Categories = new SelectList(await _db.DanhMucSanPhams.AsNoTracking().OrderBy(x => x.TenDanhMuc).ToListAsync(), "MaDanhMuc", "TenDanhMuc");
        ViewBag.Brands = new SelectList(await _db.ThuongHieus.AsNoTracking().Where(x => x.TrangThai).OrderBy(x => x.TenThuongHieu).ToListAsync(), "MaThuongHieu", "TenThuongHieu");
    }

    private async Task<string> SaveImageAsync(IFormFile image)
    {
        return await _storageService.SaveProductImageAsync(image);
    }
}