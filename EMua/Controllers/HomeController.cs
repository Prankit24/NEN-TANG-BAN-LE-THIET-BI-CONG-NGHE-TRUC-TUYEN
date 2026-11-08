
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Models.ViewModels;
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

        private const string HomeCacheKey = "EMUA_HOME_V2";

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

        private async Task<HomeViewModel> LoadHomeDataAsync()
        {
            var totalTimer = Stopwatch.StartNew();

            // Chỉ dùng truy vấn nhẹ để tìm ID.
            // Không Include ảnh và biến thể ở bước này.
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

            // 1. Sản phẩm mới nhất.
            var sanPhamMoiIds = await baseQuery
                .OrderByDescending(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();

            // 2. Sản phẩm nổi bật theo tổng tồn kho.
            // Giữ nguyên tiêu chí trong code cũ.
            var sanPhamNoiBatIds = await availableQuery
                .OrderByDescending(x =>
                    x.BienTheSanPhams.Sum(v => v.SoLuong))
                .ThenBy(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();

            // 3. Flash Sale theo giá thấp nhất.
            // Giữ nguyên logic chọn sản phẩm trước đây.
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

            // 4. Best Choice theo sản phẩm mới còn hàng.
            var bestChoiceIds = await availableQuery
                .OrderByDescending(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();

            // Gộp các ID, loại bỏ ID trùng nhau.
            var allProductIds = sanPhamMoiIds
                .Concat(sanPhamNoiBatIds)
                .Concat(flashSaleIds)
                .Concat(bestChoiceIds)
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "EMUA HOME: {Count} unique products selected.",
                allProductIds.Count);

            // 5. Chỉ tải chi tiết các sản phẩm cần thiết.
            // Không tải lại cùng sản phẩm bốn lần.
            var products = await _db.SanPhams
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x => allProductIds.Contains(x.MaSanPham))
                .ToListAsync();

            // Tạo Dictionary để lấy sản phẩm nhanh theo ID.
            var productDictionary = products.ToDictionary(
                x => x.MaSanPham);

            // Giữ đúng thứ tự đã chọn từ database.
            // Sử dụng ToList để View nhận danh sách.
            var sanPhamMoi = sanPhamMoiIds
                .Where(productDictionary.ContainsKey)
                .Select(id => productDictionary[id])
                .ToList();

            var sanPhamNoiBat = sanPhamNoiBatIds
                .Where(productDictionary.ContainsKey)
                .Select(id => productDictionary[id])
                .ToList();

            var flashSale = flashSaleIds
                .Where(productDictionary.ContainsKey)
                .Select(id => productDictionary[id])
                .ToList();

            var bestChoice = bestChoiceIds
                .Where(productDictionary.ContainsKey)
                .Select(id => productDictionary[id])
                .ToList();

            // 6. Danh mục và thương hiệu.
            var danhMucs = await _db.DanhMucSanPhams
                .AsNoTracking()
                .Where(x =>
                    x.TrangThaiDanhMuc == null ||
                    x.TrangThaiDanhMuc != "Ẩn")
                .ToListAsync();

            var thuongHieus = await _db.ThuongHieus
                .AsNoTracking()
                .Where(x => x.TrangThai)
                .ToListAsync();

            totalTimer.Stop();

            _logger.LogInformation(
                "EMUA HOME: DB load finished in {Elapsed}ms. " +
                "Unique products: {Count}.",
                totalTimer.ElapsedMilliseconds,
                products.Count);

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
