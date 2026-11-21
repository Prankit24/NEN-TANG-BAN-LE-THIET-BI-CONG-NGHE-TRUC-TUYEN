using System.Text.Json;
using EMua.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EMua.Data;

/// <summary>
/// Nạp dữ liệu thật từ seed-products.json vào database.
/// Tự động map ảnh .webp/.jpg/.png từ wwwroot/images/products/.
/// Có kiểm tra trùng lặp – chạy lại an toàn (idempotent).
/// </summary>
public class RealDataSeeder
{
    private readonly EMuaDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<RealDataSeeder> _logger;

    public RealDataSeeder(EMuaDbContext db, IWebHostEnvironment env, ILogger<RealDataSeeder> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // ================================================================
    //  PUBLIC API
    // ================================================================

    public async Task SeedAsync()
    {
        _logger.LogInformation("▶ RealDataSeeder: Bắt đầu nạp dữ liệu…");

        var json = await LoadJsonAsync();
        if (json == null) return;

        var thuongHieuMap = await SeedThuongHieusAsync(json);
        var danhMucMap = await SeedDanhMucsAsync(json);
        var sanPhamMap = await SeedSanPhamsAsync(json, thuongHieuMap, danhMucMap);
        await SeedDanhGiasAsync(json, sanPhamMap);
        await SeedDonHangsAsync(json, sanPhamMap);

        _logger.LogInformation("✅ RealDataSeeder: Hoàn tất nạp dữ liệu.");
    }

    // ================================================================
    //  LOAD JSON
    // ================================================================

    private async Task<JsonDocument?> LoadJsonAsync()
    {
        var seedPath = Path.Combine(_env.ContentRootPath, "Data", "seed-products.json");
        if (!File.Exists(seedPath))
        {
            _logger.LogWarning("⚠ Không tìm thấy {Path}", seedPath);
            return null;
        }

        await using var stream = File.OpenRead(seedPath);
        return await JsonDocument.ParseAsync(stream);
    }

    // ================================================================
    //  THƯƠNG HIỆU
    // ================================================================

    private async Task<Dictionary<string, int>> SeedThuongHieusAsync(JsonDocument json)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existing = await _db.ThuongHieus.AsNoTracking().ToListAsync();
        foreach (var th in existing)
            map[th.TenThuongHieu] = th.MaThuongHieu;

        if (!json.RootElement.TryGetProperty("thuongHieus", out var arr)) return map;

        foreach (var item in arr.EnumerateArray())
        {
            var ten = item.GetProperty("tenThuongHieu").GetString()!;
            if (map.ContainsKey(ten)) continue;

            var entity = new ThuongHieu
            {
                TenThuongHieu = ten,
                MoTa = item.TryGetProperty("moTa", out var mt) ? mt.GetString() : null,
                TrangThai = true
            };
            _db.ThuongHieus.Add(entity);
            await _db.SaveChangesAsync();
            map[ten] = entity.MaThuongHieu;
            _logger.LogInformation("  + Thương hiệu: {Name} (#{Id})", ten, entity.MaThuongHieu);
        }

        return map;
    }

    // ================================================================
    //  DANH MỤC
    // ================================================================

    private async Task<Dictionary<string, int>> SeedDanhMucsAsync(JsonDocument json)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existing = await _db.DanhMucSanPhams.AsNoTracking().ToListAsync();
        foreach (var dm in existing)
            if (dm.TenDanhMuc != null) map[dm.TenDanhMuc] = dm.MaDanhMuc;

        if (!json.RootElement.TryGetProperty("danhMucs", out var arr)) return map;

        foreach (var item in arr.EnumerateArray())
        {
            var ten = item.GetProperty("tenDanhMuc").GetString()!;
            if (map.ContainsKey(ten)) continue;

            var entity = new DanhMucSanPham
            {
                TenDanhMuc = ten,
                MoTa = item.TryGetProperty("moTa", out var mt) ? mt.GetString() : null,
                TrangThaiDanhMuc = item.TryGetProperty("trangThaiDanhMuc", out var tt) ? tt.GetString() : "Active"
            };
            _db.DanhMucSanPhams.Add(entity);
            await _db.SaveChangesAsync();
            map[ten] = entity.MaDanhMuc;
            _logger.LogInformation("  + Danh mục: {Name} (#{Id})", ten, entity.MaDanhMuc);
        }

        return map;
    }

    // ================================================================
    //  SẢN PHẨM + BIẾN THỂ + ẢNH
    // ================================================================

    private async Task<Dictionary<string, int>> SeedSanPhamsAsync(
        JsonDocument json,
        Dictionary<string, int> thuongHieuMap,
        Dictionary<string, int> danhMucMap)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existingNames = await _db.SanPhams.AsNoTracking()
            .Where(s => s.TenSanPham != null)
            .Select(s => new { s.TenSanPham, s.MaSanPham })
            .ToListAsync();
        foreach (var sp in existingNames)
            if (sp.TenSanPham != null) map[sp.TenSanPham] = sp.MaSanPham;

        if (!json.RootElement.TryGetProperty("sanPhams", out var arr)) return map;

        foreach (var item in arr.EnumerateArray())
        {
            var ten = item.GetProperty("tenSanPham").GetString()!;
            if (map.ContainsKey(ten))
            {
                _logger.LogDebug("  ⏭ Sản phẩm đã tồn tại: {Name}", ten);
                continue;
            }

            // Resolve FK
            var thuongHieu = item.GetProperty("thuongHieu").GetString()!;
            var danhMuc = item.GetProperty("danhMuc").GetString()!;
            if (!thuongHieuMap.TryGetValue(thuongHieu, out var maTH))
            {
                _logger.LogWarning("  ⚠ Không tìm thấy thương hiệu: {TH}", thuongHieu);
                continue;
            }
            danhMucMap.TryGetValue(danhMuc, out var maDM);

            var sp = new SanPham
            {
                TenSanPham = ten,
                MoTa = item.TryGetProperty("moTa", out var mt) ? mt.GetString() : null,
                TrangThaiSanPham = item.TryGetProperty("trangThaiSanPham", out var tt) ? tt.GetString() : "Active",
                BaoHanh = item.TryGetProperty("baoHanh", out var bh) ? bh.GetString() : null,
                ThongSoKyThuat = item.TryGetProperty("thongSoKyThuat", out var ts) ? ts.GetString() : null,
                GiaGoc = item.TryGetProperty("giaGoc", out var gg) ? gg.GetDecimal() : null,
                MaThuongHieu = maTH,
                MaDanhMuc = maDM > 0 ? maDM : null
            };
            _db.SanPhams.Add(sp);
            await _db.SaveChangesAsync();
            map[ten] = sp.MaSanPham;

            // Biến thể
            var imageFolder = item.TryGetProperty("imageFolder", out var ifl) ? ifl.GetString() : null;
            if (item.TryGetProperty("bienThes", out var bienThes))
            {
                foreach (var bt in bienThes.EnumerateArray())
                {
                    var hinhAnh = bt.TryGetProperty("hinhAnh", out var ha) ? ha.GetString() : null;
                    string? resolvedImage = null;
                    if (hinhAnh != null && imageFolder != null)
                        resolvedImage = ResolveImagePath(imageFolder, hinhAnh);

                    var variant = new BienTheSanPham
                    {
                        MaSanPham = sp.MaSanPham,
                        MauSac = bt.TryGetProperty("mauSac", out var ms) ? ms.GetString() : null,
                        PhienBan = bt.TryGetProperty("phienBan", out var pb) ? pb.GetString() : null,
                        Gia = bt.GetProperty("gia").GetDecimal(),
                        SoLuong = bt.TryGetProperty("soLuong", out var sl) ? sl.GetInt32() : 0,
                        TrangThai = bt.TryGetProperty("trangThai", out var bttt) ? bttt.GetString()! : "Active",
                        HinhAnh = resolvedImage
                    };
                    _db.BienTheSanPhams.Add(variant);
                }
                await _db.SaveChangesAsync();
            }

            // Ảnh sản phẩm — tự scan thư mục
            if (imageFolder != null)
            {
                await SeedProductImagesAsync(sp.MaSanPham, imageFolder);
            }

            _logger.LogInformation("  + Sản phẩm: {Name} (#{Id}), {Variants} biến thể",
                ten, sp.MaSanPham,
                item.TryGetProperty("bienThes", out var btArr) ? btArr.GetArrayLength() : 0);
        }

        return map;
    }

    private async Task SeedProductImagesAsync(int maSanPham, string imageFolder)
    {
        var imgDir = Path.Combine(_env.WebRootPath, "images", "products", imageFolder);
        if (!Directory.Exists(imgDir)) return;

        // Kiểm tra đã có ảnh chưa
        var existingCount = await _db.SanPhamHinhAnhs
            .CountAsync(a => a.MaSanPham == maSanPham);
        if (existingCount > 0) return;

        var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".webp", ".jpg", ".jpeg", ".png" };

        // Ưu tiên .webp > .jpg > .png
        var files = Directory.GetFiles(imgDir)
            .Where(f => allowedExt.Contains(Path.GetExtension(f)))
            .OrderBy(f => Path.GetExtension(f).ToLower() switch
            {
                ".webp" => 0,
                ".jpg" => 1,
                ".jpeg" => 1,
                ".png" => 2,
                _ => 3
            })
            .ThenBy(f => Path.GetFileName(f))
            .ToList();

        // Loại bỏ trùng tên (ví dụ: 1.jpg vs 1.webp → chỉ lấy .webp)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueFiles = new List<string>();
        foreach (var f in files)
        {
            var baseName = Path.GetFileNameWithoutExtension(f);
            if (seen.Add(baseName))
                uniqueFiles.Add(f);
        }

        int order = 0;
        foreach (var filePath in uniqueFiles)
        {
            var fileName = Path.GetFileName(filePath);
            var relativePath = $"/images/products/{imageFolder}/{fileName}";

            var img = new SanPhamHinhAnh
            {
                MaSanPham = maSanPham,
                DuongDanAnh = relativePath,
                LaAnhChinh = order == 0,
                ThuTu = order
            };
            _db.SanPhamHinhAnhs.Add(img);
            order++;
        }

        if (order > 0) await _db.SaveChangesAsync();
    }

    private string? ResolveImagePath(string folder, string fileName)
    {
        // Thử .webp trước, rồi file gốc
        var webpName = Path.GetFileNameWithoutExtension(fileName) + ".webp";
        var webpPath = Path.Combine(_env.WebRootPath, "images", "products", folder, webpName);
        if (File.Exists(webpPath))
            return $"/images/products/{folder}/{webpName}";

        var originalPath = Path.Combine(_env.WebRootPath, "images", "products", folder, fileName);
        if (File.Exists(originalPath))
            return $"/images/products/{folder}/{fileName}";

        return $"/images/products/{folder}/{fileName}";
    }

    // ================================================================
    //  ĐÁNH GIÁ
    // ================================================================

    private async Task SeedDanhGiasAsync(JsonDocument json, Dictionary<string, int> sanPhamMap)
    {
        if (!json.RootElement.TryGetProperty("danhGias", out var arr)) return;

        // Kiểm tra đã seed đánh giá chưa (nếu đã có > 10 đánh giá thì bỏ qua)
        var existingReviews = await _db.DanhGiaSanPhams.CountAsync();
        if (existingReviews >= arr.GetArrayLength())
        {
            _logger.LogDebug("  ⏭ Đã có {Count} đánh giá, bỏ qua", existingReviews);
            return;
        }

        // Lấy user Customer đầu tiên hoặc bất kỳ
        var customers = await _db.NguoiDungs.AsNoTracking()
            .Where(u => u.MaQuyenNavigation.TenQuyen == "Customer" || u.MaQuyen == 3)
            .Select(u => u.MaNguoiDung)
            .Take(5)
            .ToListAsync();

        if (customers.Count == 0)
        {
            // Lấy bất kỳ user nào
            customers = await _db.NguoiDungs.AsNoTracking()
                .Select(u => u.MaNguoiDung)
                .Take(5)
                .ToListAsync();
        }

        if (customers.Count == 0)
        {
            _logger.LogWarning("  ⚠ Không có user nào để tạo đánh giá");
            return;
        }

        var now = DateTime.Now;
        int idx = 0;

        foreach (var item in arr.EnumerateArray())
        {
            var tenSP = item.GetProperty("tenSanPham").GetString()!;
            if (!sanPhamMap.TryGetValue(tenSP, out var maSP)) continue;

            var ngayOffset = item.TryGetProperty("ngayOffset", out var no) ? no.GetInt32() : 0;
            var ngayDanhGia = now.AddDays(ngayOffset);
            var userId = customers[idx % customers.Count];

            // Kiểm tra trùng
            var exists = await _db.DanhGiaSanPhams.AnyAsync(d =>
                d.MaSanPham == maSP &&
                d.MaNguoiDung == userId &&
                d.BinhLuan == item.GetProperty("binhLuan").GetString());
            if (exists) { idx++; continue; }

            var dg = new DanhGiaSanPham
            {
                MaSanPham = maSP,
                MaNguoiDung = userId,
                SoLuongSao = item.TryGetProperty("soSao", out var ss) ? ss.GetInt32() : 5,
                BinhLuan = item.GetProperty("binhLuan").GetString(),
                NgayDanhGia = ngayDanhGia,
                TrangThai = true
            };
            _db.DanhGiaSanPhams.Add(dg);
            idx++;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("  + Đã tạo {Count} đánh giá", idx);
    }

    // ================================================================
    //  ĐƠN HÀNG MẪU
    // ================================================================

    private async Task SeedDonHangsAsync(JsonDocument json, Dictionary<string, int> sanPhamMap)
    {
        if (!json.RootElement.TryGetProperty("donHangTemplates", out var tpl)) return;

        var soLuong = tpl.TryGetProperty("soLuongDonHang", out var slProp) ? slProp.GetInt32() : 60;

        // Kiểm tra đã seed đủ chưa
        var existingOrders = await _db.DonHangs.CountAsync();
        if (existingOrders >= soLuong)
        {
            _logger.LogDebug("  ⏭ Đã có {Count} đơn hàng, bỏ qua", existingOrders);
            return;
        }

        var trangThais = tpl.GetProperty("trangThais").EnumerateArray()
            .Select(t => t.GetString()!).ToList();
        var hoTens = tpl.GetProperty("hoTens").EnumerateArray()
            .Select(t => t.GetString()!).ToList();
        var phones = tpl.GetProperty("soDienThoais").EnumerateArray()
            .Select(t => t.GetString()!).ToList();
        var diaChis = tpl.GetProperty("diaChis").EnumerateArray()
            .Select(t => t.GetString()!).ToList();
        var ptvc = tpl.TryGetProperty("phuongThucVanChuyen", out var ptvcProp)
            ? ptvcProp.GetString()! : "Giao hàng tiêu chuẩn";

        // Lấy biến thể để tạo chi tiết đơn hàng
        var allVariants = await _db.BienTheSanPhams.AsNoTracking()
            .Where(b => b.TrangThai == "Active" && b.Gia > 0)
            .Select(b => new { b.MaBienThe, b.Gia, b.MaSanPham })
            .ToListAsync();

        if (allVariants.Count == 0)
        {
            _logger.LogWarning("  ⚠ Không có biến thể nào để tạo đơn hàng");
            return;
        }

        // Lấy customer users
        var customers = await _db.NguoiDungs.AsNoTracking()
            .Select(u => u.MaNguoiDung)
            .Take(10)
            .ToListAsync();
        if (customers.Count == 0) return;

        // Lấy staff user cho hóa đơn
        var staffId = await _db.NguoiDungs.AsNoTracking()
            .Where(u => u.MaQuyenNavigation.TenQuyen == "Staff" || u.MaQuyen == 2)
            .Select(u => (int?)u.MaNguoiDung)
            .FirstOrDefaultAsync();

        var rnd = new Random(42); // Deterministic seed for reproducibility
        var now = DateTime.Now;
        var ordersToCreate = soLuong - existingOrders;

        for (int i = 0; i < ordersToCreate; i++)
        {
            var trangThai = trangThais[i % trangThais.Count];
            var ngayDat = now.AddDays(-rnd.Next(1, 30)).AddHours(rnd.Next(8, 22));
            var customerId = customers[i % customers.Count];

            // Chọn 1-3 sản phẩm ngẫu nhiên
            var numItems = rnd.Next(1, 4);
            var selectedVariants = allVariants
                .OrderBy(_ => rnd.Next())
                .Take(numItems)
                .ToList();

            decimal tongTien = 0;
            var chiTiets = new List<ChiTietDonHang>();
            foreach (var v in selectedVariants)
            {
                var qty = rnd.Next(1, 3);
                var thanhTien = v.Gia * qty;
                tongTien += thanhTien;
                chiTiets.Add(new ChiTietDonHang
                {
                    MaBienThe = v.MaBienThe,
                    SoLuong = qty,
                    DonGia = v.Gia,
                    ThanhTien = thanhTien
                });
            }

            var donHang = new DonHang
            {
                MaNguoiDung = customerId,
                NgayDat = ngayDat,
                TongTien = tongTien,
                TrangThaiDonHang = trangThai,
                HoTenNhanHang = hoTens[i % hoTens.Count],
                SoDienThoaiNhanHang = phones[i % phones.Count],
                DiaChiNhanHang = diaChis[i % diaChis.Count],
                PhuongThucVanChuyen = ptvc,
                ChiTietDonHangs = chiTiets
            };
            _db.DonHangs.Add(donHang);
            await _db.SaveChangesAsync();

            // Tạo ThanhToan cho đơn đã giao
            if (trangThai == "DaGiao")
            {
                var thanhToan = new ThanhToan
                {
                    MaDonHang = donHang.MaDonHang,
                    PhuongThuc = "COD",
                    SoTien = tongTien,
                    TrangThai = "DaThanhToan",
                    NgayTao = ngayDat,
                    NgayThanhToan = ngayDat.AddDays(rnd.Next(1, 5))
                };
                _db.ThanhToans.Add(thanhToan);

                // Tạo HoaDon cho đơn đã giao
                var hoaDon = new HoaDon
                {
                    MaDonHang = donHang.MaDonHang,
                    NgayLapHoaDon = ngayDat.AddDays(rnd.Next(1, 3)),
                    TongTien = tongTien,
                    TrangThaiHoaDon = "DaXuatHoaDon",
                    MaNguoiLap = staffId,
                    TenNguoiLap = staffId.HasValue ? "Nhân viên" : null,
                    HoTenKhachHang = donHang.HoTenNhanHang,
                    SoDienThoai = donHang.SoDienThoaiNhanHang,
                    DiaChiGiaoHang = donHang.DiaChiNhanHang,
                    TienTruocThue = tongTien,
                    TienThueVat = 0,
                    PhiVanChuyen = 0,
                    TienGiamGia = 0,
                    PhuongThucThanhToan = "COD"
                };
                _db.Set<HoaDon>().Add(hoaDon);
                await _db.SaveChangesAsync();

                // Tạo ChiTietHoaDon
                foreach (var ct in chiTiets)
                {
                    var variant = allVariants.First(v => v.MaBienThe == ct.MaBienThe);
                    var cthd = new ChiTietHoaDon
                    {
                        MaHoaDon = hoaDon.MaHoaDon,
                        MaBienThe = ct.MaBienThe,
                        TenSanPham = sanPhamMap.FirstOrDefault(x => x.Value == variant.MaSanPham).Key ?? "Sản phẩm",
                        SoLuong = ct.SoLuong,
                        DonGiaChuaThue = ct.DonGia,
                        ThueSuatVat = 0,
                        TienThueVat = 0,
                        DonGiaSauThue = ct.DonGia,
                        ChietKhau = 0,
                        ThanhTien = ct.ThanhTien
                    };
                    _db.Set<ChiTietHoaDon>().Add(cthd);
                }
                await _db.SaveChangesAsync();
            }
        }

        _logger.LogInformation("  + Đã tạo {Count} đơn hàng mẫu", ordersToCreate);
    }
}
