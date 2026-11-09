
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Models.Database;
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

        private const string HomeCacheKey = "EMUA_HOME_V3";

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
            var stepTimer = Stopwatch.StartNew();

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
            var tNew = stepTimer.ElapsedMilliseconds;

            // 2. Sản phẩm nổi bật theo tổng tồn kho.
            stepTimer.Restart();
            var sanPhamNoiBatIds = await availableQuery
                .OrderByDescending(x =>
                    x.BienTheSanPhams.Sum(v => v.SoLuong))
                .ThenBy(x => x.MaSanPham)
                .Select(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();
            var tFeatured = stepTimer.ElapsedMilliseconds;

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

            // 5. Chỉ tải dữ liệu và cột cần thiết hiển thị cho các sản phẩm đã chọn (tránh tải MoTa, ThongSoKyThuat, ảnh thừa).
            stepTimer.Restart();
            var products = new List<SanPham>();

            if (allProductIds.Count > 0)
            {
                var rawProducts = await _db.SanPhams
                    .AsNoTracking()
                    .Where(x => allProductIds.Contains(x.MaSanPham))
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

                products = rawProducts.Select(item => new SanPham
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
                }).ToList();
            }
            var tProducts = stepTimer.ElapsedMilliseconds;

            // Tạo Dictionary để lấy sản phẩm nhanh theo ID.
            var productDictionary = products.ToDictionary(
                x => x.MaSanPham);

            // Giữ đúng thứ tự đã chọn từ database.
            var sanPhamMoi = sanPhamMoiIds
                .Where(id => productDictionary.ContainsKey(id))
                .Select(id => productDictionary[id])
                .ToList();

            var sanPhamNoiBat = sanPhamNoiBatIds
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

            // 6. Danh mục và thương hiệu.
            stepTimer.Restart();
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
            var tMeta = stepTimer.ElapsedMilliseconds;

            totalTimer.Stop();

            _logger.LogInformation(
                "EMUA HOME: DB load finished in {Elapsed}ms (New: {TNew}ms, Feat: {TFeat}ms, Flash: {TFlash}ms, Best: {TBest}ms, Products: {TProd}ms, Meta: {TMeta}ms). Unique products: {Count}.",
                totalTimer.ElapsedMilliseconds,
                tNew,
                tFeatured,
                tFlashSale,
                tBestChoice,
                tProducts,
                tMeta,
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
