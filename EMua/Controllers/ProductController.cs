using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers
{
    public class ProductController : Controller
    {
        private readonly EMuaDbContext _db;

        public ProductController(EMuaDbContext db)
        {
            _db = db;
        }

        // =========================================================
        // DANH SÁCH SẢN PHẨM
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? category,
            int? brandId,
            string? keyword,
            string? sort,
            bool favoritesOnly = false)
        {
            var userId = GetUserId();
            var query = _db.SanPhams
                .AsNoTracking()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x => x.TrangThaiSanPham == null || x.TrangThaiSanPham != "Ẩn")
                .AsQueryable();

            var filterFavorites = favoritesOnly || sort == "favorites";
            if (filterFavorites)
            {
                if (userId == null)
                {
                    return RedirectToAction("Login", "Auth", new { returnUrl = Request.Path + Request.QueryString });
                }

                query = query.Where(x => _db.YeuThiches.Any(f => f.MaSanPham == x.MaSanPham && f.MaNguoiDung == userId));
            }

            // TÌM KIẾM
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();
                query = query.Where(x => x.TenSanPham != null && x.TenSanPham.Contains(keyword));
            }

            // LỌC DANH MỤC TỪ HEADER
            if (!string.IsNullOrWhiteSpace(category))
            {
                switch (category)
                {
                    case "Laptop":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("laptop"));
                        break;

                    case "DienThoai":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 (x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("điện thoại") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("smartphone") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("phone")));
                        break;

                    case "Gaming":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 (x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("gaming") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("game")));
                        break;

                    case "PhuKien":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 (x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("phụ kiện") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("accessory")));
                        break;

                    case "AmThanh":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 (x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("âm thanh") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("tai nghe") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("loa") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("audio")));
                        break;

                    case "ThietBiThongMinh":
                        query = query.Where(x => x.MaDanhMucNavigation != null &&
                                                 x.MaDanhMucNavigation.TenDanhMuc != null &&
                                                 (x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("thiết bị thông minh") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("smart") ||
                                                  x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("đồng hồ")));
                        break;
                }
            }

            // LỌC THEO THƯƠNG HIỆU
            if (brandId.HasValue)
            {
                query = query.Where(x => x.MaThuongHieu == brandId.Value);
            }

            // SẮP XẾP
            switch (sort)
            {
                case "favorites":
                    query = query.OrderByDescending(x => x.MaSanPham);
                    break;

                case "price-asc":
                    query = query.OrderBy(x => x.BienTheSanPhams
                        .Where(v => v.SoLuong > 0)
                        .Select(v => (decimal?)v.Gia)
                        .Min() ?? x.GiaGoc ?? 0);
                    break;

                case "price-desc":
                    query = query.OrderByDescending(x => x.BienTheSanPhams
                        .Where(v => v.SoLuong > 0)
                        .Select(v => (decimal?)v.Gia)
                        .Min() ?? x.GiaGoc ?? 0);
                    break;

                case "name-asc":
                    query = query.OrderBy(x => x.TenSanPham);
                    break;

                case "name-desc":
                    query = query.OrderByDescending(x => x.TenSanPham);
                    break;

                default:
                    query = query.OrderByDescending(x => x.MaSanPham);
                    break;
            }

            ViewBag.Category = category;
            ViewBag.Keyword = keyword;
            ViewBag.BrandId = brandId;
            ViewBag.Sort = sort;
            ViewBag.FavoritesOnly = filterFavorites;
            ViewBag.FavoriteProductIds = userId.HasValue
                ? new HashSet<int>(await _db.YeuThiches.AsNoTracking()
                    .Where(x => x.MaNguoiDung == userId)
                    .Select(x => x.MaSanPham)
                    .ToListAsync())
                : new HashSet<int>();

            ViewBag.Categories = await _db.DanhMucSanPhams
                .AsNoTracking()
                .Where(x => x.TrangThaiDanhMuc == null || x.TrangThaiDanhMuc != "Ẩn")
                .OrderBy(x => x.TenDanhMuc)
                .ToListAsync();

            ViewBag.Brands = await _db.ThuongHieus
                .AsNoTracking()
                .Where(x => x.TrangThai)
                .OrderBy(x => x.TenThuongHieu)
                .ToListAsync();

            var products = await query.ToListAsync();
            return View(products);
        }

        // =========================================================
        // SEARCH TỪ HEADER
        // =========================================================
        [HttpGet]
        public IActionResult Search(string? keyword)
        {
            return RedirectToAction(nameof(Index), new { keyword = keyword });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Favorite(int productId, string? returnUrl = null)
        {
            var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
            var userId = GetUserId();

            if (userId == null)
            {
                if (wantsJson) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để lưu sản phẩm yêu thích." });
                var loginReturnUrl = Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : Url.Action(nameof(Details), new { id = productId });
                return RedirectToAction("Login", "Auth", new { returnUrl = loginReturnUrl });
            }

            if (productId <= 0)
            {
                return NotFound();
            }

            var favorite = await _db.YeuThiches
                .FirstOrDefaultAsync(x => x.MaNguoiDung == userId && x.MaSanPham == productId);

            var isFavorite = favorite == null;
            if (isFavorite)
            {
                var productExists = await _db.SanPhams
                    .AnyAsync(x => x.MaSanPham == productId && (x.TrangThaiSanPham == null || x.TrangThaiSanPham != "Ẩn"));

                if (!productExists)
                {
                    return NotFound();
                }

                _db.YeuThiches.Add(new YeuThich
                {
                    MaNguoiDung = userId.Value,
                    MaSanPham = productId,
                    NgayThem = DateTime.Now
                });

                await _db.SaveChangesAsync();
                TempData["Success"] = "Đã thêm sản phẩm vào danh sách yêu thích.";
            }
            else
            {
                _db.YeuThiches.Remove(favorite!);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Đã xóa sản phẩm khỏi danh sách yêu thích.";
            }

            if (wantsJson) return Json(new { success = true, isFavorite, productId });

            return Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl!)
                : RedirectToAction(nameof(Details), new { id = productId });
        }

        // =========================================================
        // CHI TIẾT SẢN PHẨM (ĐÃ ĐƯỢC CHỈNH SỬA CHUẨN MODEL & VIEW)
        // /Product/Details/5
        // =========================================================
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
                .FirstOrDefaultAsync(x => x.MaSanPham == id);

            if (product == null || product.TrangThaiSanPham == "Ẩn")
            {
                return NotFound();
            }

            var defaultVariant = product.BienTheSanPhams.FirstOrDefault();

            // 1. Lấy chuỗi đường dẫn thô
            var rawImageUrl = defaultVariant?.HinhAnh
                ?? product.SanPhamHinhAnhs.FirstOrDefault()?.DuongDanAnh;

            // 2. Hàm xử lý đường dẫn tuyệt đối chuẩn ASP.NET Core
            string FixImage(string? url)
            {
                if (string.IsNullOrWhiteSpace(url)) return "/images/no-image.png";

                url = url.Trim().Replace("\\", "/");

                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return url;

                if (!url.StartsWith("/"))
                    return "/" + url; // Ghép dấu / vào đầu để thành đường dẫn tuyệt đối tính từ gốc root

                return url;
            }

            // Map dữ liệu từ Entity SanPham sang ProductDetailsViewModel
            var viewModel = new ProductDetailsViewModel
            {
                ProductId = product.MaSanPham,
                Name = product.TenSanPham ?? string.Empty,
                Category = product.MaDanhMucNavigation?.TenDanhMuc,
                Brand = product.MaThuongHieuNavigation?.TenThuongHieu,
                Price = defaultVariant?.Gia ?? product.GiaGoc ?? 0,
                Stock = defaultVariant?.SoLuong ?? 0,

                // Gán đường dẫn đã được thêm dấu / tuyệt đối ở đầu
                ImageUrl = FixImage(rawImageUrl),

                DefaultVariantId = defaultVariant?.MaBienThe ?? 0,
                Description = product.MoTa,
                Specifications = product.ThongSoKyThuat,
                Warranty = product.BaoHanh,

                Variants = product.BienTheSanPhams.Select(v => new ProductVariantViewModel
                {
                    VariantId = v.MaBienThe,
                    Label = string.Join(" / ", new[] { v.MauSac, v.PhienBan }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    Price = v.Gia,
                    Stock = v.SoLuong,
                    ImageUrl = FixImage(v.HinhAnh)
                }).ToList(),

                Reviews = product.DanhGiaSanPhams
                    .Where(r => r.TrangThai == true)
                    .Select(r => new ProductReviewViewModel
                    {
                        CustomerName = r.MaNguoiDungNavigation != null
                            ? (r.MaNguoiDungNavigation.TenNguoiDung ?? r.MaNguoiDungNavigation.Email ?? "Khách hàng")
                            : "Khách hàng",
                        Rating = r.SoLuongSao.GetValueOrDefault(5),
                        Comment = r.BinhLuan,
                        CreatedAt = r.NgayDanhGia
                    }).ToList(),

                AverageRating = product.DanhGiaSanPhams.Any(r => r.TrangThai == true && r.SoLuongSao.HasValue)
                    ? (decimal)product.DanhGiaSanPhams.Where(r => r.TrangThai == true && r.SoLuongSao.HasValue).Average(r => r.SoLuongSao!.Value)
                    : 0m
            };

            return View("~/Views/Product/Details.cshtml", viewModel);
        }

        // =========================================================
        // LỌC TRỰC TIẾP BẰNG ID DANH MỤC
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Category(int id)
        {
            var categoryExists = await _db.DanhMucSanPhams
                .AsNoTracking()
                .AnyAsync(x => x.MaDanhMuc == id);

            if (!categoryExists)
            {
                return NotFound();
            }

            var products = await _db.SanPhams
                .AsNoTracking()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x => x.MaDanhMuc == id && (x.TrangThaiSanPham == null || x.TrangThaiSanPham != "Ẩn"))
                .OrderByDescending(x => x.MaSanPham)
                .ToListAsync();

            ViewBag.CategoryId = id;
            return View("Index", products);
        }

        // =========================================================
        // LỌC THƯƠNG HIỆU
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Brand(int id)
        {
            var brandExists = await _db.ThuongHieus
                .AsNoTracking()
                .AnyAsync(x => x.MaThuongHieu == id);

            if (!brandExists)
            {
                return NotFound();
            }

            var products = await _db.SanPhams
                .AsNoTracking()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x => x.MaThuongHieu == id && (x.TrangThaiSanPham == null || x.TrangThaiSanPham != "Ẩn"))
                .OrderByDescending(x => x.MaSanPham)
                .ToListAsync();

            ViewBag.BrandId = id;
            return View("Index", products);
        }

        private int? GetUserId()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdText, out var userId) ? userId : null;
        }
    }
}