
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using EMua.Data;
using EMua.Helpers;
using EMua.Models.Database;
using EMua.Services;
using EMua.ViewModels;

namespace EMua.Controllers
{
    public class ProductController : Controller
    {
        private readonly EMuaDbContext _db;
        private readonly IMemoryCache _cache;

        public const string CategoriesCacheKey = AppCacheKeys.Categories;
        public const string BrandsCacheKey = AppCacheKeys.Brands;

        public ProductController(EMuaDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        // =====================================================
        // YÊU THÍCH SẢN PHẨM
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Favorite(int productId)
        {
            if (User.Identity?.IsAuthenticated != true ||
                !int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                var returnUrl = Url.Action(
                    nameof(Details),
                    "Product",
                    new { id = productId });

                return RedirectToAction(
                    "Login", "Auth", new { returnUrl });
            }

            bool productExists = await _db.SanPhams
                .AnyAsync(x =>
                    x.MaSanPham == productId &&
                    (x.TrangThaiSanPham == null ||
                     x.TrangThaiSanPham != "Ẩn"));

            if (!productExists)
                return NotFound();

            int exists = await _db.Database
                .SqlQuery<int>($@"
                    SELECT COUNT(*)::int AS ""Value""
                    FROM public.yeuthich
                    WHERE manguoidung = {userId}
                      AND masanpham = {productId}")
                .FirstOrDefaultAsync();

            if (exists > 0)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    DELETE FROM public.yeuthich
                    WHERE manguoidung = {userId}
                      AND masanpham = {productId}");

                TempData["Success"] =
                    "Đã bỏ sản phẩm khỏi danh sách yêu thích.";
            }
            else
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO public.yeuthich
                        (manguoidung, masanpham, ngaythem)
                    VALUES
                        ({userId}, {productId}, LOCALTIMESTAMP)
                    ON CONFLICT (manguoidung, masanpham)
                    DO NOTHING");

                TempData["Success"] =
                    "Đã lưu sản phẩm vào danh sách yêu thích.";
            }

            return RedirectToAction(
                nameof(Details), new { id = productId });
        }

        // =====================================================
        // DANH SÁCH - TÌM KIẾM - LỌC - SẮP XẾP
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? category,
            int? brandId,
            string? keyword,
            string? sort,
            int? categoryId,
            string? priceRange,
            decimal? minPrice,
            decimal? maxPrice,
            bool? sale = null,
            int page = 1,
            int pageSize = 20)
        {
            category = category?.Trim();
            keyword = keyword?.Trim();
            sort = sort?.Trim();
            priceRange = priceRange?.Trim();

            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? 20 : Math.Clamp(pageSize, 8, 100);

            var query = _db.SanPhams
                .AsNoTracking()
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn")
                .AsQueryable();

            if (sale == true || sort == "sale")
            {
                query = query.Where(x =>
                    x.GiaGoc.HasValue && x.GiaGoc.Value > 0 &&
                    x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia < x.GiaGoc.Value));
            }

            // Tìm kiếm không phân biệt chữ hoa/thường.
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x =>
                    x.TenSanPham != null &&
                    EF.Functions.ILike(
                        x.TenSanPham,
                        "%" + keyword + "%"));
            }

            // Đọc danh mục từ IMemoryCache, đồng bộ liên kết Header với dropdown.
            if (!_cache.TryGetValue(CategoriesCacheKey, out List<DanhMucSanPham>? categories) || categories == null)
            {
                categories = await _db.DanhMucSanPhams
                    .AsNoTracking()
                    .Where(x => x.TrangThaiDanhMuc == null || x.TrangThaiDanhMuc != "Ẩn")
                    .OrderBy(x => x.TenDanhMuc)
                    .ToListAsync();

                _cache.Set(CategoriesCacheKey, categories, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                });
            }

            if (!categoryId.HasValue && !string.IsNullOrWhiteSpace(category))
            {
                var key = category.ToLowerInvariant();
                string[] aliases = key switch
                {
                    "laptop" => new[] { "laptop" },
                    "dienthoai" => new[] { "điện thoại", "smartphone", "phone" },
                    "gaming" => new[] { "gaming", "game" },
                    "phukien" => new[] { "phụ kiện", "accessory" },
                    "amthanh" => new[] { "âm thanh", "tai nghe", "loa", "audio" },
                    "thietbithongminh" => new[] { "thiết bị thông minh", "smart", "đồng hồ" },
                    _ => new[] { category }
                };

                var matchedCategory = categories.FirstOrDefault(x =>
                    x.TenDanhMuc != null && aliases.Any(a =>
                        x.TenDanhMuc.Contains(a, StringComparison.OrdinalIgnoreCase)));

                if (matchedCategory != null)
                    categoryId = matchedCategory.MaDanhMuc;
                else
                    query = query.Where(x => false);
            }

            if (categoryId.HasValue)
                query = query.Where(x => x.MaDanhMuc == categoryId.Value);

            // Lọc thương hiệu.
            if (brandId.HasValue)
            {
                query = query.Where(x =>
                    x.MaThuongHieu == brandId.Value);
            }

            // Lọc theo khoảng giá linh hoạt
            if (!string.IsNullOrWhiteSpace(priceRange))
            {
                switch (priceRange.ToLowerInvariant())
                {
                    case "under-5m":
                        query = query.Where(x =>
                            x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia < 5000000) ||
                            (x.GiaGoc.HasValue && x.GiaGoc.Value < 5000000));
                        break;
                    case "5m-15m":
                        query = query.Where(x =>
                            x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia >= 5000000 && v.Gia <= 15000000) ||
                            (x.GiaGoc.HasValue && x.GiaGoc.Value >= 5000000 && x.GiaGoc.Value <= 15000000));
                        break;
                    case "15m-30m":
                        query = query.Where(x =>
                            x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia > 15000000 && v.Gia <= 30000000) ||
                            (x.GiaGoc.HasValue && x.GiaGoc.Value > 15000000 && x.GiaGoc.Value <= 30000000));
                        break;
                    case "over-30m":
                        query = query.Where(x =>
                            x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia > 30000000) ||
                            (x.GiaGoc.HasValue && x.GiaGoc.Value > 30000000));
                        break;
                }
            }
            else
            {
                if (minPrice.HasValue && minPrice.Value > 0)
                {
                    query = query.Where(x =>
                        x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia >= minPrice.Value) ||
                        (x.GiaGoc.HasValue && x.GiaGoc.Value >= minPrice.Value));
                }
                if (maxPrice.HasValue && maxPrice.Value > 0)
                {
                    query = query.Where(x =>
                        x.BienTheSanPhams.Any(v => v.SoLuong > 0 && v.Gia <= maxPrice.Value) ||
                        (x.GiaGoc.HasValue && x.GiaGoc.Value <= maxPrice.Value));
                }
            }

            // Sắp xếp theo giá của biến thể còn hàng.
            switch (sort)
            {
                case "price-asc":
                    query = query.OrderBy(x =>
                        x.BienTheSanPhams
                            .Where(v => v.SoLuong > 0)
                            .Select(v => (decimal?)v.Gia)
                            .Min()
                        ?? x.GiaGoc
                        ?? 0);
                    break;

                case "price-desc":
                    query = query.OrderByDescending(x =>
                        x.BienTheSanPhams
                            .Where(v => v.SoLuong > 0)
                            .Select(v => (decimal?)v.Gia)
                            .Min()
                        ?? x.GiaGoc
                        ?? 0);
                    break;

                case "name-asc":
                    query = query.OrderBy(x => x.TenSanPham);
                    break;

                case "name-desc":
                    query = query.OrderByDescending(
                        x => x.TenSanPham);
                    break;

                case "sale":
                    query = query.OrderByDescending(x =>
                        x.GiaGoc.HasValue
                            ? (x.GiaGoc.Value - x.BienTheSanPhams.Where(v => v.SoLuong > 0).Min(v => v.Gia))
                            : 0);
                    break;

                case "popular":
                case "featured":
                    query = query.OrderByDescending(x =>
                        x.BienTheSanPhams.SelectMany(v => v.ChiTietDonHangs).Count() * 5 +
                        x.DanhGiaSanPhams.Count(r => r.TrangThai == true) * 3 +
                        x.BienTheSanPhams.Sum(v => v.SoLuong))
                        .ThenByDescending(x => x.MaSanPham);
                    break;

                case "newest":
                default:
                    query = query.OrderByDescending(
                        x => x.MaSanPham);
                    break;
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages > 0 && page > totalPages) page = totalPages;

            ViewBag.Category = category;
            ViewBag.CategoryId = categoryId;
            ViewBag.BrandId = brandId;
            ViewBag.Keyword = keyword;
            ViewBag.Sort = sort;
            ViewBag.Sale = sale;
            ViewBag.PriceRange = priceRange;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.PageSize = pageSize;

            ViewBag.Categories = categories;

            if (!_cache.TryGetValue(BrandsCacheKey, out List<ThuongHieu>? brands) || brands == null)
            {
                brands = await _db.ThuongHieus
                    .AsNoTracking()
                    .Where(x => x.TrangThai)
                    .OrderBy(x => x.TenThuongHieu)
                    .ToListAsync();

                _cache.Set(BrandsCacheKey, brands, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                });
            }

            ViewBag.Brands = brands;

            var products = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .AsSplitQuery()
                .ToListAsync();

            // AJAX chỉ trả HTML danh sách, không render Layout/Header/Footer.
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_ProductList", products);

            return View(products);
        }

        // =====================================================
        // TÌM KIẾM TỪ HEADER
        // =====================================================
        [HttpGet]
        public IActionResult Search(string? keyword)
        {
            return RedirectToAction(
                nameof(Index), new { keyword });
        }

        // =====================================================
        // CHI TIẾT SẢN PHẨM
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var product = await _db.SanPhams
                    .AsNoTracking()
                    .Include(x => x.MaDanhMucNavigation)
                    .Include(x => x.MaThuongHieuNavigation)
                    .Include(x => x.BienTheSanPhams)
                    .Include(x => x.SanPhamHinhAnhs)
                    .Include(x => x.DanhGiaSanPhams)
                        .ThenInclude(r => r.MaNguoiDungNavigation)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(x => x.MaSanPham == id);

                if (product == null ||
                    string.Equals(product.TrangThaiSanPham, "Ẩn", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound();
                }

                var productVariants = product.BienTheSanPhams ?? new List<BienTheSanPham>();

                var variants = productVariants
                    .OrderByDescending(v =>
                        v.SoLuong > 0 &&
                        v.Gia > 0 &&
                        (string.IsNullOrWhiteSpace(v.TrangThai) ||
                         string.Equals(v.TrangThai, "Còn hàng", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(v.TrangThai, "Active", StringComparison.OrdinalIgnoreCase)))
                    .ThenBy(v => v.Gia)
                    .ThenBy(v => v.MaBienThe)
                    .ToList();

                var defaultVariant = variants.FirstOrDefault() ?? productVariants.FirstOrDefault();

                var resolvedMainImage = ImageUrlHelper.ResolveProductImage(product, defaultVariant, checkPhysicalFile: true);

                var allImages = new List<string>();

                var productImages = product.SanPhamHinhAnhs ?? new List<SanPhamHinhAnh>();
                foreach (var image in productImages
                    .OrderByDescending(x => x.LaAnhChinh == true)
                    .ThenBy(x => x.ThuTu))
                {
                    if (string.IsNullOrWhiteSpace(image?.DuongDanAnh)) continue;
                    var url = ImageUrlHelper.FixImageUrl(image.DuongDanAnh, checkPhysicalFile: true);

                    if (!string.Equals(url, "/images/no-image.png", StringComparison.OrdinalIgnoreCase) &&
                        !allImages.Contains(url, StringComparer.OrdinalIgnoreCase))
                    {
                        allImages.Add(url);
                    }
                }

                foreach (var variant in variants)
                {
                    if (string.IsNullOrWhiteSpace(variant?.HinhAnh)) continue;
                    var url = ImageUrlHelper.FixImageUrl(variant.HinhAnh, checkPhysicalFile: true);

                    if (!string.Equals(url, "/images/no-image.png", StringComparison.OrdinalIgnoreCase) &&
                        !allImages.Contains(url, StringComparer.OrdinalIgnoreCase))
                    {
                        allImages.Add(url);
                    }
                }

                if (allImages.Count == 0)
                {
                    allImages.Add(resolvedMainImage);
                }

                var productReviews = (product.DanhGiaSanPhams ?? new List<DanhGiaSanPham>())
                    .Where(r => r != null && r.TrangThai == true)
                    .ToList();

                decimal averageRating = 0m;
                var ratedReviews = productReviews.Where(r => r.SoLuongSao.HasValue && r.SoLuongSao.Value > 0).ToList();
                if (ratedReviews.Any())
                {
                    averageRating = (decimal)ratedReviews.Average(r => Math.Clamp(r.SoLuongSao!.Value, 1, 5));
                }

                var viewModel = new ProductDetailsViewModel
                {
                    ProductId = product.MaSanPham,
                    Name = product.TenSanPham ?? "Sản phẩm",
                    Category = product.MaDanhMucNavigation?.TenDanhMuc ?? "Công nghệ",
                    Brand = product.MaThuongHieuNavigation?.TenThuongHieu ?? "EMUA",

                    Price = defaultVariant?.Gia
                        ?? product.GiaGoc
                        ?? 0,

                    Stock = (defaultVariant != null &&
                            (string.IsNullOrWhiteSpace(defaultVariant.TrangThai) ||
                             string.Equals(defaultVariant.TrangThai, "Còn hàng", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(defaultVariant.TrangThai, "Active", StringComparison.OrdinalIgnoreCase)))
                        ? Math.Max(0, defaultVariant.SoLuong)
                        : 0,

                    ImageUrl = resolvedMainImage,
                    ImageUrls = allImages,
                    DefaultVariantId = defaultVariant?.MaBienThe ?? 0,

                    Description = product.MoTa,
                    Specifications = product.ThongSoKyThuat,
                    Warranty = product.BaoHanh,

                    Variants = variants.Select(v =>
                    {
                        var varImg = ImageUrlHelper.FixImageUrl(v.HinhAnh, checkPhysicalFile: true);
                        if (string.Equals(varImg, "/images/no-image.png", StringComparison.OrdinalIgnoreCase))
                        {
                            varImg = resolvedMainImage;
                        }
                        var labelParts = new[] { v.MauSac, v.PhienBan }
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                        return new ProductVariantViewModel
                        {
                            VariantId = v.MaBienThe,
                            Label = labelParts.Count > 0 ? string.Join(" / ", labelParts) : "Mặc định",
                            Price = v.Gia,
                            Stock = (string.IsNullOrWhiteSpace(v.TrangThai) ||
                                     string.Equals(v.TrangThai, "Còn hàng", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(v.TrangThai, "Active", StringComparison.OrdinalIgnoreCase))
                                ? Math.Max(0, v.SoLuong)
                                : 0,
                            ImageUrl = varImg
                        };
                    }).ToList(),

                    Reviews = productReviews.Select(r => new ProductReviewViewModel
                    {
                        CustomerName = r.MaNguoiDungNavigation != null
                            ? (r.MaNguoiDungNavigation.TenNguoiDung ?? r.MaNguoiDungNavigation.Email ?? "Khách hàng")
                            : "Khách hàng",
                        Rating = Math.Clamp(r.SoLuongSao.GetValueOrDefault(5), 1, 5),
                        Comment = r.BinhLuan,
                        CreatedAt = r.NgayDanhGia
                    }).ToList(),

                    AverageRating = averageRating
                };

                ViewBag.IsFavorited = false;

                if (User.Identity?.IsAuthenticated == true &&
                    int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
                {
                    try
                    {
                        ViewBag.IsFavorited = await _db.Database
                            .SqlQuery<int>($@"
                                SELECT COUNT(*)::int AS ""Value""
                                FROM public.yeuthich
                                WHERE manguoidung = {currentUserId}
                                  AND masanpham = {id}")
                            .FirstOrDefaultAsync() > 0;
                    }
                    catch
                    {
                        ViewBag.IsFavorited = false;
                    }
                }

                return View("~/Views/Product/Details.cshtml", viewModel);
            }
            catch (Exception ex)
            {
                // Ghi log dự phòng nếu có lỗi phát sinh để không làm trắng trang
                return StatusCode(500, $"Đã xảy ra lỗi khi tải chi tiết sản phẩm #{id}: {ex.Message}");
            }
        }

        // =====================================================
        // DANH MỤC THEO ID
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Category(int id)
        {
            bool exists = await _db.DanhMucSanPhams
                .AsNoTracking()
                .AnyAsync(x => x.MaDanhMuc == id);

            if (!exists)
                return NotFound();

            return RedirectToAction(
                nameof(Index), new { categoryId = id });
        }

        // =====================================================
        // THƯƠNG HIỆU THEO ID
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Brand(int id)
        {
            bool exists = await _db.ThuongHieus
                .AsNoTracking()
                .AnyAsync(x => x.MaThuongHieu == id);

            if (!exists)
                return NotFound();

            return RedirectToAction(
                nameof(Index), new { brandId = id });
        }
    }
}
