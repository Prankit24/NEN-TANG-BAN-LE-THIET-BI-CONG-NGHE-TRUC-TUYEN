
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Models.Database;
using EMua.Models.ViewModels;
using EMua.Services;
using EMua.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMua.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly EMuaDbContext _db;
        private readonly IMemoryCache _cache;

        public const string HomeCacheKey = AppCacheKeys.Home;
        public const string CategoriesCacheKey = AppCacheKeys.Categories;
        public const string FeaturedProductsCacheKey = AppCacheKeys.FeaturedProducts;

        private static readonly SemaphoreSlim HomeCacheLock =
            new SemaphoreSlim(1, 1);

        private static readonly TimeSpan CacheDuration =
            TimeSpan.FromMinutes(3);

        public HomeController(
            ILogger<HomeController> logger,
            EMuaDbContext db,
            IMemoryCache cache)
        {
            _logger = logger;
            _db = db;
            _cache = cache;
        }

        public async Task<IActionResult> Index()
        {
            var stopwatch = Stopwatch.StartNew();

            if (!_cache.TryGetValue(
                HomeCacheKey,
                out HomeViewModel? model) || model == null)
            {
                await HomeCacheLock.WaitAsync(
                    HttpContext.RequestAborted);

                try
                {
                    // Kiểm tra lần hai để tránh nhiều request
                    // cùng truy vấn Supabase khi cache hết hạn.
                    if (!_cache.TryGetValue(
                        HomeCacheKey,
                        out model) || model == null)
                    {
                        _logger.LogInformation(
                            "EMUA HOME: Cache MISS, loading data.");

                        model = await LoadHomeDataAsync();

                        _cache.Set(
                            HomeCacheKey,
                            model,
                            new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow =
                                    CacheDuration
                            });
                    }
                }
                finally
                {
                    HomeCacheLock.Release();
                }
            }
            else
            {
                _logger.LogDebug("EMUA HOME: Cache HIT.");
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "EMUA HOME: Data ready in {Elapsed}ms",
                stopwatch.ElapsedMilliseconds);

            return View(model!);
        }

        /// <summary>
        /// Cache danh mục sản phẩm trong IMemoryCache (thời gian lưu 30 phút, sliding 10 phút)
        /// </summary>
        public async Task<List<DanhMucSanPham>> GetCachedCategoriesAsync()
        {
            if (!_cache.TryGetValue(CategoriesCacheKey, out List<DanhMucSanPham>? categories) || categories == null)
            {
                categories = await _db.DanhMucSanPhams
                    .AsNoTracking()
                    .Where(x =>
                        x.TrangThaiDanhMuc == null ||
                        x.TrangThaiDanhMuc != "Ẩn")
                    .OrderBy(x => x.TenDanhMuc)
                    .ToListAsync();

                _cache.Set(
                    CategoriesCacheKey,
                    categories,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    });
            }

            return categories;
        }

        /// <summary>
        /// Cache danh sách Sản phẩm nổi bật trang chủ trong IMemoryCache (thời gian lưu 10 phút, sliding 5 phút)
        /// </summary>
        public async Task<List<SanPham>> GetCachedFeaturedProductsAsync()
        {
            if (!_cache.TryGetValue(FeaturedProductsCacheKey, out List<SanPham>? featuredProducts) || featuredProducts == null)
            {
                var baseQuery = _db.SanPhams
                    .AsNoTracking()
                    .Where(x =>
                        x.TrangThaiSanPham == null ||
                        x.TrangThaiSanPham != "Ẩn");

                var availableQuery = baseQuery
                    .Where(x =>
                        x.BienTheSanPhams.Any(v =>
                            v.SoLuong > 0 &&
                            v.TrangThai == "Còn hàng"));

                var sanPhamNoiBatIds = await availableQuery
                    .OrderByDescending(x =>
                        x.BienTheSanPhams.Sum(v => v.SoLuong))
                    .ThenBy(x => x.MaSanPham)
                    .Select(x => x.MaSanPham)
                    .Take(8)
                    .ToListAsync();

                featuredProducts = await LoadProductsByIdsAsync(sanPhamNoiBatIds);

                _cache.Set(
                    FeaturedProductsCacheKey,
                    featuredProducts,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                        SlidingExpiration = TimeSpan.FromMinutes(5)
                    });
            }

            return featuredProducts;
        }

        private async Task<List<SanPham>> LoadProductsByIdsAsync(List<int> productIds)
        {
            if (productIds == null || productIds.Count == 0)
                return new List<SanPham>();

            var rawProducts = await _db.SanPhams
                .AsNoTracking()
                .Where(x => productIds.Contains(x.MaSanPham))
                .Select(x => new
                {
                    x.MaSanPham,
                    x.TenSanPham,
                    x.GiaGoc,
                    x.MaThuongHieu,
                    ThuongHieuTen = x.MaThuongHieuNavigation != null ? x.MaThuongHieuNavigation.TenThuongHieu : null,
                    MainImage = x.SanPhamHinhAnhs
                        .Where(img => !string.IsNullOrEmpty(img.DuongDanAnh))
                        .OrderByDescending(img => img.LaAnhChinh)
                        .ThenBy(img => img.ThuTu)
                        .Select(img => img.DuongDanAnh)
                        .FirstOrDefault(),
                    Variants = x.BienTheSanPhams
                        .Select(v => new
                        {
                            v.MaBienThe,
                            v.Gia,
                            v.PhienBan,
                            v.MauSac,
                            v.HinhAnh,
                            v.SoLuong,
                            v.TrangThai
                        })
                        .ToList()
                })
                .ToListAsync();

            var productsDict = rawProducts.Select(item => new SanPham
            {
                MaSanPham = item.MaSanPham,
                TenSanPham = item.TenSanPham,
                GiaGoc = item.GiaGoc,
                MaThuongHieu = item.MaThuongHieu,
                MaThuongHieuNavigation = new ThuongHieu
                {
                    MaThuongHieu = item.MaThuongHieu,
                    TenThuongHieu = item.ThuongHieuTen ?? "EMUA"
                },
                SanPhamHinhAnhs = string.IsNullOrEmpty(item.MainImage)
                    ? new List<SanPhamHinhAnh>()
                    : new List<SanPhamHinhAnh>
                    {
                        new() { DuongDanAnh = item.MainImage, LaAnhChinh = true, ThuTu = 1, MaSanPham = item.MaSanPham }
                    },
                BienTheSanPhams = item.Variants.Select(v => new BienTheSanPham
                {
                    MaBienThe = v.MaBienThe,
                    MaSanPham = item.MaSanPham,
                    Gia = v.Gia,
                    PhienBan = v.PhienBan,
                    MauSac = v.MauSac,
                    HinhAnh = v.HinhAnh,
                    SoLuong = v.SoLuong,
                    TrangThai = v.TrangThai
                }).ToList()
            }).ToDictionary(x => x.MaSanPham);

            return productIds
                .Where(id => productsDict.ContainsKey(id))
                .Select(id => productsDict[id])
                .ToList();
        }

        private async Task<HomeViewModel> LoadHomeDataAsync()
        {
            var totalTimer = Stopwatch.StartNew();
            var stepTimer = Stopwatch.StartNew();

            // 1. Lấy Sản phẩm nổi bật từ IMemoryCache (chạy tuần tự trên _db nếu cache miss)
            var sanPhamNoiBat = await GetCachedFeaturedProductsAsync();
            var tFeatured = stepTimer.ElapsedMilliseconds;

            var baseQuery = _db.SanPhams
                .AsNoTracking()
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn");

            var availableQuery = baseQuery
                .Where(x =>
                    x.BienTheSanPhams.Any(v =>
                        v.SoLuong > 0 &&
                        v.TrangThai == "Còn hàng"));

            // 2. Sản phẩm mới nhất.
            stepTimer.Restart();
            var sanPhamMoiIds = await baseQuery
                .OrderByDescending(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();
            var tNew = stepTimer.ElapsedMilliseconds;

            // 3. Flash Sale theo giá thấp nhất.
            stepTimer.Restart();
            var flashSaleIds = await availableQuery
                .OrderBy(x =>
                    x.BienTheSanPhams
                        .Where(v =>
                            v.TrangThai == "Còn hàng")
                        .Min(v => v.Gia))
                .ThenBy(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();
            var tFlashSale = stepTimer.ElapsedMilliseconds;

            // 4. Best Choice theo sản phẩm mới còn hàng.
            stepTimer.Restart();
            var bestChoiceIds = await availableQuery
                .OrderByDescending(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();
            var tBestChoice = stepTimer.ElapsedMilliseconds;

            // 5. Tải dữ liệu các sản phẩm còn lại
            var remainingIds = sanPhamMoiIds
                .Concat(flashSaleIds)
                .Concat(bestChoiceIds)
                .Distinct()
                .ToList();

            stepTimer.Restart();
            var remainingProducts = await LoadProductsByIdsAsync(remainingIds);
            var productDictionary = remainingProducts.ToDictionary(x => x.MaSanPham);
            foreach (var fp in sanPhamNoiBat)
            {
                if (!productDictionary.ContainsKey(fp.MaSanPham))
                    productDictionary[fp.MaSanPham] = fp;
            }
            var tProducts = stepTimer.ElapsedMilliseconds;

            var sanPhamMoi = sanPhamMoiIds
                .Where(id => productDictionary.ContainsKey(id))
                .Select(id => productDictionary[id])
                .ToList();

            var flashSale = flashSaleIds
                .Where(id => productDictionary.ContainsKey(id))
                .Select(id => productDictionary[id])
                .ToList();

            var bestChoice = bestChoiceIds
                .Where(id => productDictionary.ContainsKey(id))
                .Select(id => productDictionary[id])
                .ToList();

            // 6. Danh mục (từ IMemoryCache, tuần tự trên _db nếu cache miss) và thương hiệu
            stepTimer.Restart();
            var danhMucs = await GetCachedCategoriesAsync();

            var thuongHieus = await _db.ThuongHieus
                .AsNoTracking()
                .Where(x => x.TrangThai)
                .ToListAsync();
            var tMeta = stepTimer.ElapsedMilliseconds;

            totalTimer.Stop();

            _logger.LogInformation(
                "EMUA HOME: DB load finished in {Elapsed}ms (Feat: {TFeat}ms, New: {TNew}ms, Flash: {TFlash}ms, Best: {TBest}ms, Products: {TProd}ms, Meta: {TMeta}ms).",
                totalTimer.ElapsedMilliseconds,
                tFeatured,
                tNew,
                tFlashSale,
                tBestChoice,
                tProducts,
                tMeta);

            return new HomeViewModel
            {
                SanPhamMoi = sanPhamMoi,
                SanPhamNoiBat = sanPhamNoiBat,
                FlashSale = flashSale,
                BestChoice = bestChoice,
                DanhMucs = danhMucs,
                ThuongHieus = thuongHieus
            };
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id
                            ?? HttpContext.TraceIdentifier
            });
        }

        public IActionResult StoreLocations()
        {
            return View();
        }

        public IActionResult Franchise()
        {
            return View();
        }
    }
}
