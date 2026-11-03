
using System.Diagnostics;
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

        private const string HomeCacheKey = "EMUA_HOME_V1";

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

            var model = await _cache.GetOrCreateAsync(
                HomeCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromSeconds(60);

                    _logger.LogInformation(
                        "EMUA HOME: Loading data from Supabase");

                    return await LoadHomeDataAsync();
                });

            stopwatch.Stop();

            _logger.LogInformation(
                "EMUA HOME: Data loaded in {Elapsed}ms",
                stopwatch.ElapsedMilliseconds);

            return View(model);
        }

        private async Task<HomeViewModel> LoadHomeDataAsync()
        {
            var baseQuery = _db.SanPhams
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn");

            // 8 sản phẩm mới nhất
            var sanPhamMoi = await baseQuery
                .OrderByDescending(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();

            // Sản phẩm còn hàng
            var sanPhamNoiBat = await baseQuery
                .Where(x =>
                    x.BienTheSanPhams.Any(v =>
                        v.SoLuong > 0 &&
                        v.TrangThai == "Còn hàng"))
                .OrderByDescending(x =>
                    x.BienTheSanPhams.Sum(v => v.SoLuong))
                .Take(8)
                .ToListAsync();

            // Flash Sale
            var flashSale = await baseQuery
                .Where(x =>
                    x.BienTheSanPhams.Any(v =>
                        v.SoLuong > 0 &&
                        v.TrangThai == "Còn hàng"))
                .OrderBy(x =>
                    x.BienTheSanPhams
                        .Where(v =>
                            v.TrangThai == "Còn hàng")
                        .Min(v => v.Gia))
                .Take(8)
                .ToListAsync();

            // Best Choice hiện dùng cùng tiêu chí với sản phẩm mới
            // nhưng vẫn có điều kiện còn hàng.
            var bestChoice = await baseQuery
                .Where(x =>
                    x.BienTheSanPhams.Any(v =>
                        v.SoLuong > 0 &&
                        v.TrangThai == "Còn hàng"))
                .OrderByDescending(x => x.MaSanPham)
                .Take(8)
                .ToListAsync();

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
