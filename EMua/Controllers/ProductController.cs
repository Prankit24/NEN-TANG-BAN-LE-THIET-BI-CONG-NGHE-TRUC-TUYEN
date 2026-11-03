
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EMua.Data;
using EMua.ViewModels;

namespace EMua.Controllers
{
    public class ProductController : Controller
    {
        private readonly EMuaDbContext _db;

        public ProductController(EMuaDbContext db)
        {
            _db = db;
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
            int? categoryId)
        {
            category = category?.Trim();
            keyword = keyword?.Trim();
            sort = sort?.Trim();

            var query = _db.SanPhams
                .AsNoTracking()
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn")
                .AsQueryable();

            // Tìm kiếm không phân biệt chữ hoa/thường.
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x =>
                    x.TenSanPham != null &&
                    EF.Functions.ILike(
                        x.TenSanPham,
                        "%" + keyword + "%"));
            }

            // Đọc danh mục một lần, đồng bộ liên kết Header với dropdown.
            var categories = await _db.DanhMucSanPhams
                .AsNoTracking()
                .Where(x => x.TrangThaiDanhMuc == null || x.TrangThaiDanhMuc != "Ẩn")
                .OrderBy(x => x.TenDanhMuc)
                .ToListAsync();

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

                default:
                    query = query.OrderByDescending(
                        x => x.MaSanPham);
                    break;
            }

            ViewBag.Category = category;
            ViewBag.CategoryId = categoryId;
            ViewBag.BrandId = brandId;
            ViewBag.Keyword = keyword;
            ViewBag.Sort = sort;

            ViewBag.Categories = categories;

            ViewBag.Brands = await _db.ThuongHieus
                .AsNoTracking()
                .Where(x => x.TrangThai)
                .OrderBy(x => x.TenThuongHieu)
                .ToListAsync();

            var products = await query
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
                product.TrangThaiSanPham == "Ẩn")
            {
                return NotFound();
            }

            var variants = product.BienTheSanPhams
                .OrderByDescending(v =>
                    v.SoLuong > 0 &&
                    v.Gia > 0 &&
                    v.TrangThai == "Còn hàng")
                .ThenBy(v => v.Gia)
                .ThenBy(v => v.MaBienThe)
                .ToList();

            var defaultVariant = variants.FirstOrDefault();

            string FixImage(string? url)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return "/images/no-image.png";

                url = url.Trim().Replace("\\", "/");

                if (url.StartsWith(
                        "http://",
                        StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith(
                        "https://",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                return url.StartsWith("/") ? url : "/" + url;
            }

            var mainProductImage = product.SanPhamHinhAnhs
                .OrderByDescending(x => x.LaAnhChinh)
                .ThenBy(x => x.ThuTu)
                .FirstOrDefault();

            var rawImageUrl =
                defaultVariant?.HinhAnh ??
                mainProductImage?.DuongDanAnh;

            var allImages = new List<string>();

            foreach (var image in product.SanPhamHinhAnhs
                .OrderByDescending(x => x.LaAnhChinh)
                .ThenBy(x => x.ThuTu))
            {
                var url = FixImage(image.DuongDanAnh);

                if (url != "/images/no-image.png" &&
                    !allImages.Contains(
                        url,
                        StringComparer.OrdinalIgnoreCase))
                {
                    allImages.Add(url);
                }
            }

            foreach (var variant in variants)
            {
                var url = FixImage(variant.HinhAnh);

                if (url != "/images/no-image.png" &&
                    !allImages.Contains(
                        url,
                        StringComparer.OrdinalIgnoreCase))
                {
                    allImages.Add(url);
                }
            }

            if (allImages.Count == 0)
            {
                allImages.Add(FixImage(rawImageUrl));
            }

            var viewModel = new ProductDetailsViewModel
            {
                ProductId = product.MaSanPham,
                Name = product.TenSanPham ?? string.Empty,
                Category =
                    product.MaDanhMucNavigation?.TenDanhMuc,
                Brand =
                    product.MaThuongHieuNavigation?.TenThuongHieu,

                Price = defaultVariant?.Gia
                    ?? product.GiaGoc
                    ?? 0,

                Stock = defaultVariant?.TrangThai == "Còn hàng"
                    ? defaultVariant.SoLuong
                    : 0,

                ImageUrl = FixImage(rawImageUrl),
                ImageUrls = allImages,
                DefaultVariantId =
                    defaultVariant?.MaBienThe ?? 0,

                Description = product.MoTa,
                Specifications = product.ThongSoKyThuat,
                Warranty = product.BaoHanh,

                Variants = variants.Select(v =>
                    new ProductVariantViewModel
                    {
                        VariantId = v.MaBienThe,
                        Label = string.Join(" / ",
                            new[] { v.MauSac, v.PhienBan }
                            .Where(s =>
                                !string.IsNullOrWhiteSpace(s))),
                        Price = v.Gia,
                        Stock = v.TrangThai == "Còn hàng"
                            ? v.SoLuong
                            : 0,
                        ImageUrl = FixImage(v.HinhAnh)
                    }).ToList(),

                Reviews = product.DanhGiaSanPhams
                    .Where(r => r.TrangThai == true)
                    .Select(r => new ProductReviewViewModel
                    {
                        CustomerName =
                            r.MaNguoiDungNavigation != null
                                ? r.MaNguoiDungNavigation
                                    .TenNguoiDung
                                    ?? r.MaNguoiDungNavigation
                                        .Email
                                    ?? "Khách hàng"
                                : "Khách hàng",

                        Rating =
                            r.SoLuongSao.GetValueOrDefault(5),
                        Comment = r.BinhLuan,
                        CreatedAt = r.NgayDanhGia
                    }).ToList(),

                AverageRating =
                    product.DanhGiaSanPhams.Any(r =>
                        r.TrangThai == true &&
                        r.SoLuongSao.HasValue)
                    ? (decimal)product.DanhGiaSanPhams
                        .Where(r =>
                            r.TrangThai == true &&
                            r.SoLuongSao.HasValue)
                        .Average(r => r.SoLuongSao!.Value)
                    : 0m
            };

            ViewBag.IsFavorited = false;

            if (User.Identity?.IsAuthenticated == true &&
                int.TryParse(
                    User.FindFirstValue(
                        ClaimTypes.NameIdentifier),
                    out var currentUserId))
            {
                ViewBag.IsFavorited = await _db.Database
                    .SqlQuery<int>($@"
                        SELECT COUNT(*)::int AS ""Value""
                        FROM public.yeuthich
                        WHERE manguoidung = {currentUserId}
                          AND masanpham = {id}")
                    .FirstOrDefaultAsync() > 0;
            }

            return View(
                "~/Views/Product/Details.cshtml",
                viewModel);
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
