using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

public class ProductController : Controller
{
    private readonly EMuaDbContext _db;

    public ProductController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public IActionResult Search(string? keyword) => RedirectToAction(nameof(Index), new { keyword });

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, string? category, string? sort, bool favoritesOnly = false)
    {
        var query = _db.SanPhams.AsNoTracking()
            .Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .Include(x => x.YeuThiches)
            .AsSplitQuery()
            .Where(x => x.TrangThaiSanPham == null || x.TrangThaiSanPham.ToLower() != "ẩn");

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            var normalizedSearch = search.ToLowerInvariant();
            query = query.Where(x =>
                (x.TenSanPham != null && x.TenSanPham.ToLower().Contains(normalizedSearch)) ||
                (x.MoTa != null && x.MoTa.ToLower().Contains(normalizedSearch)) ||
                (x.ThongSoKyThuat != null && x.ThongSoKyThuat.ToLower().Contains(normalizedSearch)) ||
                x.MaThuongHieuNavigation.TenThuongHieu.ToLower().Contains(normalizedSearch) ||
                (x.MaDanhMucNavigation != null && x.MaDanhMucNavigation.TenDanhMuc != null &&
                 x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = NormalizeCategory(category);
            query = query.Where(x => x.MaDanhMucNavigation != null &&
                x.MaDanhMucNavigation.TenDanhMuc != null &&
                x.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains(normalizedCategory));
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)
            : (int?)null;
        if (favoritesOnly)
            query = userId.HasValue
                ? query.Where(x => x.YeuThiches.Any(f => f.MaNguoiDung == userId.Value))
                : query.Where(x => false);

        query = sort switch
        {
            "price-asc" => query.OrderBy(x => x.BienTheSanPhams.Min(v => (decimal?)v.Gia)),
            "price-desc" => query.OrderByDescending(x => x.BienTheSanPhams.Max(v => (decimal?)v.Gia)),
            "name" => query.OrderBy(x => x.TenSanPham),
            _ => query.OrderByDescending(x => x.MaSanPham)
        };

        var products = await query.ToListAsync();
        return View(new ProductListViewModel
        {
            Keyword = keyword,
            Category = category,
            Sort = sort,
            FavoritesOnly = favoritesOnly,
            Products = products.Select(product => ToCard(product, userId)).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var product = await _db.SanPhams.AsNoTracking()
            .Include(x => x.MaDanhMucNavigation)
            .Include(x => x.MaThuongHieuNavigation)
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .Include(x => x.DanhGiaSanPhams).ThenInclude(x => x.MaNguoiDungNavigation)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.MaSanPham == id &&
                (x.TrangThaiSanPham == null || x.TrangThaiSanPham.ToLower() != "ẩn"));

        if (product == null)
            return NotFound();

        var model = new ProductDetailsViewModel
        {
            ProductId = product.MaSanPham,
            Name = product.TenSanPham ?? "Sản phẩm chưa đặt tên",
            Category = product.MaDanhMucNavigation?.TenDanhMuc,
            Brand = product.MaThuongHieuNavigation.TenThuongHieu,
            Price = product.BienTheSanPhams.OrderBy(v => v.Gia).Select(v => v.Gia).FirstOrDefault(),
            Stock = product.BienTheSanPhams.Sum(v => v.SoLuong),
            ImageUrl = MainImage(product.SanPhamHinhAnhs),
            Description = product.MoTa,
            Specifications = product.ThongSoKyThuat,
            Warranty = product.BaoHanh,
            Variants = product.BienTheSanPhams.Select(v => new ProductVariantViewModel
            {
                VariantId = v.MaBienThe,
                Label = VariantLabel(v.MauSac, v.PhienBan),
                Price = v.Gia,
                Stock = v.SoLuong,
                ImageUrl = v.HinhAnh ?? MainImage(product.SanPhamHinhAnhs)
            }).ToList()
        };

        model.Reviews = product.DanhGiaSanPhams.Where(x => x.TrangThai != false)
            .OrderByDescending(x => x.NgayDanhGia)
            .Select(x => new ProductReviewViewModel
            {
                CustomerName = x.MaNguoiDungNavigation?.TenNguoiDung ?? "Khách hàng",
                Rating = x.SoLuongSao ?? 0,
                Comment = x.BinhLuan,
                CreatedAt = x.NgayDanhGia
            }).ToList();
        model.AverageRating = model.Reviews.Count == 0 ? 0 : (decimal)model.Reviews.Average(x => x.Rating);
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Favorite(int productId)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var favorite = await _db.YeuThiches.FirstOrDefaultAsync(x => x.MaNguoiDung == userId && x.MaSanPham == productId);
        if (favorite == null)
            _db.YeuThiches.Add(new YeuThich { MaNguoiDung = userId, MaSanPham = productId, NgayThem = DateTime.Now });
        else
            _db.YeuThiches.Remove(favorite);
        await _db.SaveChangesAsync();
        TempData["Success"] = favorite == null ? "Đã thêm sản phẩm vào yêu thích." : "Đã bỏ sản phẩm khỏi yêu thích.";
        return RedirectToAction(nameof(Index));
    }

    private static ProductCardViewModel ToCard(EMua.Models.Database.SanPham product, int? userId = null) => new()
    {
        ProductId = product.MaSanPham,
        DefaultVariantId = product.BienTheSanPhams.OrderBy(v => v.Gia).Select(v => v.MaBienThe).FirstOrDefault(),
        IsFavorite = userId.HasValue && product.YeuThiches.Any(x => x.MaNguoiDung == userId.Value),
        Name = product.TenSanPham ?? "Sản phẩm chưa đặt tên",
        Category = product.MaDanhMucNavigation?.TenDanhMuc,
        Brand = product.MaThuongHieuNavigation.TenThuongHieu,
        Price = product.BienTheSanPhams.OrderBy(v => v.Gia).Select(v => v.Gia).FirstOrDefault(),
        Stock = product.BienTheSanPhams.Sum(v => v.SoLuong),
        ImageUrl = MainImage(product.SanPhamHinhAnhs)
    };

    private static string? MainImage(IEnumerable<EMua.Models.Database.SanPhamHinhAnh> images) =>
        images.OrderByDescending(x => x.LaAnhChinh).ThenBy(x => x.ThuTu).Select(x => x.DuongDanAnh).FirstOrDefault();

    private static string VariantLabel(string? color, string? version) =>
        string.Join(" / ", new[] { color, version }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string NormalizeCategory(string category) => category.Trim().ToLowerInvariant() switch
    {
        "dienthoai" => "điện thoại",
        "phukien" => "phụ kiện",
        "amthanh" => "âm thanh",
        "thietbithongminh" => "thiết bị thông minh",
        _ => category.Trim().ToLowerInvariant()
    };
}