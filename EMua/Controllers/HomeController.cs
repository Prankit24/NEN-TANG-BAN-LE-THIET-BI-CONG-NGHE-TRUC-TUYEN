using System.Diagnostics;
using EMua.Data;
using EMua.Models.ViewModels;
using EMua.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly EMuaDbContext _db;

        public HomeController(
            ILogger<HomeController> logger,
            EMuaDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var baseQuery = _db.SanPhams
                .AsNoTracking()
                .Include(x => x.MaDanhMucNavigation)
                .Include(x => x.MaThuongHieuNavigation)
                .Include(x => x.BienTheSanPhams)
                .Include(x => x.SanPhamHinhAnhs)
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn");

            var model = new HomeViewModel
            {
                // 8 sản phẩm mới nhất
                SanPhamMoi = await baseQuery
                    .OrderByDescending(x => x.MaSanPham)
                    .Take(8)
                    .ToListAsync(),

                // Tạm thời lấy 8 sản phẩm có hàng làm nổi bật
                SanPhamNoiBat = await baseQuery
                    .Where(x =>
                        x.BienTheSanPhams.Any(v =>
                            v.SoLuong > 0 &&
                            v.TrangThai == "Còn hàng"))
                    .OrderByDescending(x => x.BienTheSanPhams.Sum(v => v.SoLuong))
                    .Take(8)
                    .ToListAsync(),

                // Tạm thời dùng sản phẩm mới cho Flash Sale.
                // Sau này có bảng khuyến mãi thì đổi query này.
                FlashSale = await baseQuery
                    .Where(x =>
                        x.BienTheSanPhams.Any(v =>
                            v.SoLuong > 0 &&
                            v.TrangThai == "Còn hàng"))
                    .OrderBy(x => x.BienTheSanPhams
                        .Where(v => v.TrangThai == "Còn hàng")
                        .Min(v => v.Gia))
                    .Take(8)
                    .ToListAsync(),

                // Tạm thời lấy 8 sản phẩm làm Best Choice.
                // Sau này có cột nhân viên set Best Choice thì lọc tại đây.
                BestChoice = await baseQuery
                    .Where(x =>
                        x.BienTheSanPhams.Any(v =>
                            v.SoLuong > 0 &&
                            v.TrangThai == "Còn hàng"))
                    .OrderByDescending(x => x.MaSanPham)
                    .Take(8)
                    .ToListAsync(),

                DanhMucs = await _db.DanhMucSanPhams
                    .AsNoTracking()
                    .Where(x =>
                        x.TrangThaiDanhMuc == null ||
                        x.TrangThaiDanhMuc != "Ẩn")
                    .ToListAsync(),

                ThuongHieus = await _db.ThuongHieus
                    .AsNoTracking()
                    .Where(x => x.TrangThai)
                    .ToListAsync()
            };

            return View(model);
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
        // Action cho Landing Page Bản đồ Hệ thống Cửa hàng
        public IActionResult StoreLocations()
        {
            return View();
        }

        // Action Trang Nhượng quyền (nếu chưa có)
        public IActionResult Franchise()
        {
            return View();
        }
    }
}