using EMua.Data;
using EMua.Models.Database;
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
        //
        // Ví dụ:
        // /Product
        // /Product?category=Laptop
        // /Product?category=DienThoai
        // /Product?brandId=1
        // /Product?keyword=iPhone
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? category,
            int? brandId,
            string? keyword,
            string? sort)
        {
            var query = _db.SanPhams
                .AsNoTracking()

                .Include(x => x.MaDanhMucNavigation)

                .Include(x => x.MaThuongHieuNavigation)

                .Include(x => x.BienTheSanPhams)

                .Include(x => x.SanPhamHinhAnhs)

                // Không hiện sản phẩm bị ẩn
                .Where(x =>
                    x.TrangThaiSanPham == null ||
                    x.TrangThaiSanPham != "Ẩn")

                .AsQueryable();


            // =====================================================
            // TÌM KIẾM
            // =====================================================
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();

                query = query.Where(x =>
                    x.TenSanPham != null &&
                    x.TenSanPham.Contains(keyword));
            }


            // =====================================================
            // LỌC DANH MỤC TỪ HEADER
            // =====================================================
            if (!string.IsNullOrWhiteSpace(category))
            {
                switch (category)
                {
                    // -----------------------------
                    // LAPTOP
                    // -----------------------------
                    case "Laptop":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            x.MaDanhMucNavigation.TenDanhMuc
                                .ToLower()
                                .Contains("laptop"));

                        break;


                    // -----------------------------
                    // ĐIỆN THOẠI
                    // -----------------------------
                    case "DienThoai":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            (
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("điện thoại")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("smartphone")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("phone")
                            ));

                        break;


                    // -----------------------------
                    // GAMING
                    // -----------------------------
                    case "Gaming":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            (
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("gaming")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("game")
                            ));

                        break;


                    // -----------------------------
                    // PHỤ KIỆN
                    // -----------------------------
                    case "PhuKien":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            (
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("phụ kiện")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("accessory")
                            ));

                        break;


                    // -----------------------------
                    // ÂM THANH
                    // -----------------------------
                    case "AmThanh":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            (
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("âm thanh")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("tai nghe")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("loa")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("audio")
                            ));

                        break;


                    // -----------------------------
                    // THIẾT BỊ THÔNG MINH
                    // -----------------------------
                    case "ThietBiThongMinh":

                        query = query.Where(x =>
                            x.MaDanhMucNavigation != null &&
                            x.MaDanhMucNavigation.TenDanhMuc != null &&
                            (
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("thiết bị thông minh")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("smart")
                                ||
                                x.MaDanhMucNavigation.TenDanhMuc
                                    .ToLower()
                                    .Contains("đồng hồ")
                            ));

                        break;
                }
            }


            // =====================================================
            // LỌC THEO THƯƠNG HIỆU
            // =====================================================
            if (brandId.HasValue)
            {
                query = query.Where(x =>
                    x.MaThuongHieu == brandId.Value);
            }


            // =====================================================
            // SẮP XẾP
            // =====================================================
            switch (sort)
            {
                // Giá thấp → cao
                case "price-asc":

                    query = query
                        .OrderBy(x =>
                            x.BienTheSanPhams
                                .Where(v => v.SoLuong > 0)
                                .Select(v => (decimal?)v.Gia)
                                .Min()
                            ??
                            x.GiaGoc
                            ??
                            0);

                    break;


                // Giá cao → thấp
                case "price-desc":

                    query = query
                        .OrderByDescending(x =>
                            x.BienTheSanPhams
                                .Where(v => v.SoLuong > 0)
                                .Select(v => (decimal?)v.Gia)
                                .Min()
                            ??
                            x.GiaGoc
                            ??
                            0);

                    break;


                // Tên A-Z
                case "name-asc":

                    query = query
                        .OrderBy(x => x.TenSanPham);

                    break;


                // Tên Z-A
                case "name-desc":

                    query = query
                        .OrderByDescending(x => x.TenSanPham);

                    break;


                // Mới nhất
                default:

                    query = query
                        .OrderByDescending(x => x.MaSanPham);

                    break;
            }


            // =====================================================
            // DATA DÙNG CHO FILTER Ở VIEW
            // =====================================================
            ViewBag.Category = category;

            ViewBag.Keyword = keyword;

            ViewBag.BrandId = brandId;

            ViewBag.Sort = sort;


            ViewBag.Categories = await _db.DanhMucSanPhams
                .AsNoTracking()
                .Where(x =>
                    x.TrangThaiDanhMuc == null ||
                    x.TrangThaiDanhMuc != "Ẩn")
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
        //
        // Header hiện tại:
        // asp-action="Search"
        // input name="keyword"
        // =========================================================
        [HttpGet]
        public IActionResult Search(string? keyword)
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    keyword = keyword
                });
        }


        // =========================================================
        // CHI TIẾT SẢN PHẨM
        // /Product/Details/5
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var product = await _db.SanPhams
                .AsNoTracking()

                .Include(x =>
                    x.MaDanhMucNavigation)

                .Include(x =>
                    x.MaThuongHieuNavigation)

                .Include(x =>
                    x.BienTheSanPhams)

                .Include(x =>
                    x.SanPhamHinhAnhs)

                .Include(x =>
                    x.DanhGiaSanPhams)

                .FirstOrDefaultAsync(x =>
                    x.MaSanPham == id);


            if (product == null)
            {
                return NotFound();
            }


            // Sản phẩm đang bị ẩn thì khách không xem
            if (product.TrangThaiSanPham == "Ẩn")
            {
                return NotFound();
            }


            return View(product);
        }


        // =========================================================
        // LỌC TRỰC TIẾP BẰNG ID DANH MỤC
        //
        // Có thể dùng cho card danh mục ở trang chủ:
        // /Product/Category/3
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Category(int id)
        {
            var categoryExists =
                await _db.DanhMucSanPhams
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.MaDanhMuc == id);


            if (!categoryExists)
            {
                return NotFound();
            }


            var products = await _db.SanPhams
                .AsNoTracking()

                .Include(x =>
                    x.MaDanhMucNavigation)

                .Include(x =>
                    x.MaThuongHieuNavigation)

                .Include(x =>
                    x.BienTheSanPhams)

                .Include(x =>
                    x.SanPhamHinhAnhs)

                .Where(x =>
                    x.MaDanhMuc == id
                    &&
                    (
                        x.TrangThaiSanPham == null ||
                        x.TrangThaiSanPham != "Ẩn"
                    ))

                .OrderByDescending(x =>
                    x.MaSanPham)

                .ToListAsync();


            ViewBag.CategoryId = id;


            return View("Index", products);
        }


        // =========================================================
        // LỌC THƯƠNG HIỆU
        //
        // /Product/Brand/1
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Brand(int id)
        {
            var brandExists =
                await _db.ThuongHieus
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.MaThuongHieu == id);


            if (!brandExists)
            {
                return NotFound();
            }


            var products = await _db.SanPhams
                .AsNoTracking()

                .Include(x =>
                    x.MaDanhMucNavigation)

                .Include(x =>
                    x.MaThuongHieuNavigation)

                .Include(x =>
                    x.BienTheSanPhams)

                .Include(x =>
                    x.SanPhamHinhAnhs)

                .Where(x =>
                    x.MaThuongHieu == id
                    &&
                    (
                        x.TrangThaiSanPham == null ||
                        x.TrangThaiSanPham != "Ẩn"
                    ))

                .OrderByDescending(x =>
                    x.MaSanPham)

                .ToListAsync();


            ViewBag.BrandId = id;


            return View("Index", products);
        }
    }
}