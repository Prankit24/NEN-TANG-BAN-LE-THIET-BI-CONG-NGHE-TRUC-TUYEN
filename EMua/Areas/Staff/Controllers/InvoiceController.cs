using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class InvoiceController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public InvoiceController(EMuaDbContext db) => _db = db;

    // 1. DANH SÁCH HÓA ĐƠN & BÁO CÁO THỐNG KÊ THEO NGÀY / THÁNG / NĂM
    public async Task<IActionResult> Index(
        string? q, 
        string? status, 
        int? month, 
        int? year, 
        DateTime? fromDate, 
        DateTime? toDate, 
        int page = 1, 
        int pageSize = 10)
    {
        var invoicesQuery = _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .Include(x => x.MaNguoiLapNavigation)
            .Include(x => x.ChiTietHoaDons)
            .AsQueryable();

        // 1.1 Tìm kiếm theo mã hóa đơn, mã đơn hàng, khách hàng, SĐT, hoặc nhân viên lập
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var searchUpper = search.ToUpperInvariant();

            int? exactInvoiceId = null;
            int? exactOrderId = null;

            if (searchUpper.StartsWith("#HD-") || searchUpper.StartsWith("HD-"))
            {
                var numPart = searchUpper.Replace("#HD-", "").Replace("HD-", "").Trim();
                if (int.TryParse(numPart, out var parsed)) exactInvoiceId = parsed;
            }
            else if (searchUpper.StartsWith("#DH-") || searchUpper.StartsWith("DH-"))
            {
                var numPart = searchUpper.Replace("#DH-", "").Replace("DH-", "").Trim();
                if (int.TryParse(numPart, out var parsed)) exactOrderId = parsed;
            }
            else if (int.TryParse(search, out var parsedNum))
            {
                exactInvoiceId = parsedNum;
                exactOrderId = parsedNum;
            }

            var pattern = $"%{search}%";

            invoicesQuery = invoicesQuery.Where(x =>
                (exactInvoiceId.HasValue && x.MaHoaDon == exactInvoiceId.Value) ||
                (exactOrderId.HasValue && x.MaDonHang == exactOrderId.Value) ||
                (x.HoTenKhachHang != null && EF.Functions.ILike(x.HoTenKhachHang, pattern)) ||
                (x.SoDienThoai != null && EF.Functions.ILike(x.SoDienThoai, pattern)) ||
                (x.TenNguoiLap != null && EF.Functions.ILike(x.TenNguoiLap, pattern)) ||
                (x.MaNguoiLapNavigation != null && x.MaNguoiLapNavigation.TenNguoiDung != null && EF.Functions.ILike(x.MaNguoiLapNavigation.TenNguoiDung, pattern)) ||
                (x.MaDonHangNavigation != null && x.MaDonHangNavigation.HoTenNhanHang != null && EF.Functions.ILike(x.MaDonHangNavigation.HoTenNhanHang, pattern)));
        }

        // 1.2 Lọc theo trạng thái hóa đơn
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            invoicesQuery = invoicesQuery.Where(x => x.TrangThaiHoaDon == status);
        }

        // 1.3 Lọc theo tháng / năm / khoảng ngày
        if (fromDate.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon >= fromDate.Value.Date);
        }
        if (toDate.HasValue)
        {
            var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon <= endOfDay);
        }
        if (month.HasValue && month.Value > 0)
        {
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Month == month.Value);
        }
        if (year.HasValue && year.Value > 0)
        {
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Year == year.Value);
        }

        // 1.4 Danh sách trạng thái thực tế từ CSDL để hiển thị vào dropdown
        ViewBag.StatusList = await _db.HoaDons.AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.TrangThaiHoaDon))
            .Select(x => x.TrangThaiHoaDon!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        // 1.5 Tính toán số liệu thống kê tổng hợp (KPI) dựa trên tập hóa đơn đã lọc
        var statsList = await invoicesQuery
            .Select(x => new
            {
                x.TongTien,
                x.TienTruocThue,
                x.TienThueVat,
                x.TienGiamGia,
                VatItems = x.ChiTietHoaDons.Select(d => new { d.ThueSuatVat, d.TienThueVat })
            })
            .ToListAsync();

        int totalInvoicesCount = statsList.Count;
        decimal totalGross = statsList.Sum(x => x.TongTien ?? 0m);
        decimal totalNet = statsList.Sum(x => x.TienTruocThue ?? 0m);
        decimal totalVat = statsList.Sum(x => x.TienThueVat ?? 0m);
        decimal totalDiscount = statsList.Sum(x => x.TienGiamGia ?? 0m);

        var vatByRateDict = new Dictionary<decimal, decimal>();
        foreach (var s in statsList)
        {
            foreach (var item in s.VatItems)
            {
                if (!vatByRateDict.ContainsKey(item.ThueSuatVat))
                    vatByRateDict[item.ThueSuatVat] = 0m;
                vatByRateDict[item.ThueSuatVat] += item.TienThueVat;
            }
        }

        ViewBag.TotalInvoicesCount = totalInvoicesCount;
        ViewBag.TotalGrossRevenue = totalGross;
        ViewBag.TotalNetRevenue = totalNet;
        ViewBag.TotalVatAmount = totalVat;
        ViewBag.TotalDiscount = totalDiscount;
        ViewBag.VatByRateDict = vatByRateDict;

        // Phân trang
        int totalItems = totalInvoicesCount;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = await invoicesQuery
            .OrderByDescending(x => x.MaHoaDon)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = q;
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedMonth = month;
        ViewBag.SelectedYear = year;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        ViewBag.PageSize = pageSize;

        return View(items);
    }

    // 2. XUẤT BÁO CÁO TỔNG HỢP HÓA ĐƠN (CSV)
    [HttpGet]
    public async Task<IActionResult> ExportReportCsv(string? status, int? month, int? year, DateTime? fromDate, DateTime? toDate)
    {
        var invoicesQuery = _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .Include(x => x.ChiTietHoaDons)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            invoicesQuery = invoicesQuery.Where(x => x.TrangThaiHoaDon == status);

        if (fromDate.HasValue)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon >= fromDate.Value.Date);
        if (toDate.HasValue)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        if (month.HasValue && month.Value > 0)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Month == month.Value);
        if (year.HasValue && year.Value > 0)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Year == year.Value);

        var list = await invoicesQuery.OrderByDescending(x => x.MaHoaDon).ToListAsync();

        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append('\uFEFF');
        sb.AppendLine("Mã HĐ,Mã Đơn Hàng,Ngày Lập,Người Mua,SĐT,Tiền Trước Thuế,Tiền VAT,Phí Vận Chuyển,Giảm Giá,Tổng Thanh Toán,Trạng Thái,Người Lập");

        foreach (var inv in list)
        {
            var dateStr = inv.NgayLapHoaDon?.ToString("dd/MM/yyyy HH:mm") ?? "";
            var customer = (inv.HoTenKhachHang ?? "").Replace(",", " - ");
            var phone = inv.SoDienThoai ?? "";
            var net = inv.TienTruocThue ?? 0m;
            var vat = inv.TienThueVat ?? 0m;
            var ship = inv.PhiVanChuyen ?? 0m;
            var disc = inv.TienGiamGia ?? 0m;
            var gross = inv.TongTien ?? 0m;
            var st = inv.TrangThaiHoaDon ?? "Đã lập";
            var staff = (inv.TenNguoiLap ?? "").Replace(",", " - ");

            sb.AppendLine($"#HD-{inv.MaHoaDon:D5},#DH-{inv.MaDonHang:D5},{dateStr},\"{customer}\",{phone},{net},{vat},{ship},{disc},{gross},\"{st}\",\"{staff}\"");
        }

        var fileName = $"BaoCao_HoaDon_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    // 3. XEM TRƯỚC VÀ LẬP HÓA ĐƠN CHO ĐƠN HÀNG (GET)
    [HttpGet]
    public async Task<IActionResult> Create(int orderId)
    {
        var order = await _db.DonHangs
            .AsNoTracking()
            .Include(d => d.ChiTietDonHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
            .Include(d => d.MaKhuyenMaiNavigation)
            .Include(d => d.ThanhToans)
            .FirstOrDefaultAsync(d => d.MaDonHang == orderId);

        if (order == null)
        {
            TempData["Error"] = $"Không tìm thấy đơn hàng #DH-{orderId:D5}.";
            return RedirectToAction("Index", "Order");
        }

        // Ngăn lập trùng hóa đơn
        var existingInvoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == orderId);
        if (existingInvoice != null)
        {
            TempData["Warning"] = $"Đơn hàng #DH-{orderId:D5} đã có hóa đơn #HD-{existingInvoice.MaHoaDon:D5}. Không thể lập hóa đơn trùng.";
            return RedirectToAction(nameof(Details), new { id = existingInvoice.MaHoaDon });
        }

        // Kiểm tra điều kiện đơn hàng không bị hủy
        if (order.TrangThaiDonHang != null && order.TrangThaiDonHang.Contains("hủy", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = $"Đơn hàng #DH-{orderId:D5} đã bị hủy, không thể lập hóa đơn bán hàng.";
            return RedirectToAction("Details", "Order", new { id = orderId });
        }

        // Kiểm tra và lấy thông tin tài khoản nhân viên đang đăng nhập
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(currentUserIdStr, out int currentUserId) || currentUserId <= 0)
        {
            TempData["Error"] = "Không thể xác thực thông tin tài khoản nhân viên. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Order");
        }

        var currentUser = await _db.NguoiDungs
            .Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == currentUserId);

        if (currentUser == null || !currentUser.TrangThai)
        {
            TempData["Error"] = "Tài khoản nhân viên không hợp lệ hoặc đã bị vô hiệu hóa.";
            return RedirectToAction("Index", "Order");
        }

        var staffName = !string.IsNullOrWhiteSpace(currentUser.TenNguoiDung)
            ? currentUser.TenNguoiDung.Trim()
            : (!string.IsNullOrWhiteSpace(currentUser.TenDangNhap) ? currentUser.TenDangNhap.Trim() : User.Identity?.Name?.Trim());

        if (string.IsNullOrWhiteSpace(staffName))
        {
            TempData["Error"] = "Không thể xác định tên nhân viên lập hóa đơn từ tài khoản hiện tại.";
            return RedirectToAction("Index", "Order");
        }

        // Lấy phương thức thanh toán
        var payment = order.ThanhToans.OrderByDescending(p => p.NgayTao).FirstOrDefault();
        var paymentMethod = payment?.PhuongThuc ?? "Thanh toán khi nhận hàng (COD)";

        decimal itemsGrossTotal = 0m;
        decimal itemsNetTotal = 0m;
        decimal totalVat = 0m;
        var itemsList = new List<InvoiceItemViewModel>();
        var vatGroupDict = new Dictionary<decimal, (decimal net, decimal vat, decimal gross)>();

        var invoiceTime = DateTime.Now;

        foreach (var ct in order.ChiTietDonHangs)
        {
            var sp = ct.MaBienTheNavigation?.MaSanPhamNavigation;
            var bt = ct.MaBienTheNavigation;
            var categoryName = sp?.MaDanhMucNavigation?.TenDanhMuc ?? "";
            var productName = sp?.TenSanPham ?? "Sản phẩm";

            // Áp dụng luật thuế GTGT:
            // Luật 48/2024/QH15, NQ 204/2025/QH15, NĐ 174/2025/NĐ-CP
            var vatRule = VatConfigService.DetermineVatRate(categoryName, productName, invoiceTime);
            decimal vatRate = vatRule.RatePercent;

            decimal unitGrossPrice = ct.DonGia;
            decimal lineGross = ct.ThanhTien;
            decimal lineVat = VatConfigService.CalculateVatAmount(lineGross, vatRate);
            decimal lineNet = lineGross - lineVat;
            decimal unitNetPrice = Math.Round(lineNet / ct.SoLuong, 2, MidpointRounding.AwayFromZero);

            itemsGrossTotal += lineGross;
            itemsNetTotal += lineNet;
            totalVat += lineVat;

            if (!vatGroupDict.ContainsKey(vatRate))
                vatGroupDict[vatRate] = (0m, 0m, 0m);

            var curr = vatGroupDict[vatRate];
            vatGroupDict[vatRate] = (curr.net + lineNet, curr.vat + lineVat, curr.gross + lineGross);

            itemsList.Add(new InvoiceItemViewModel
            {
                MaBienThe = ct.MaBienThe,
                TenSanPham = productName,
                BienThe = $"{bt?.MauSac ?? ""} {bt?.PhienBan ?? ""}".Trim(),
                SoLuong = ct.SoLuong,
                DonGiaGoc = unitGrossPrice,
                ThueSuatVat = vatRate,
                DonGiaChuaThue = unitNetPrice,
                TienThueVat = lineVat,
                DonGiaSauThue = unitGrossPrice,
                ChietKhau = 0m,
                ThanhTien = lineGross,
                HinhAnh = bt?.HinhAnh,
                DanhMuc = categoryName,
                CanCuPhapLyThue = vatRule.LegalBasis
            });
        }

        // Tính chiết khấu voucher
        decimal voucherDiscount = 0m;
        string? voucherCode = order.MaKhuyenMaiNavigation?.MaCode;

        if (order.MaKhuyenMaiNavigation != null)
        {
            var km = order.MaKhuyenMaiNavigation;
            if (km.LoaiGiamGia != null && (km.LoaiGiamGia.Contains("%") || km.LoaiGiamGia.Contains("trăm", StringComparison.OrdinalIgnoreCase)))
            {
                voucherDiscount = Math.Round(itemsGrossTotal * km.GiaTriGiam / 100m, 0, MidpointRounding.AwayFromZero);
            }
            else
            {
                voucherDiscount = km.GiaTriGiam;
            }
            voucherDiscount = Math.Clamp(voucherDiscount, 0m, itemsGrossTotal);
        }

        // Tính phí vận chuyển thực tế từ đơn hàng:
        decimal finalTotal = order.TongTien ?? Math.Max(0m, itemsGrossTotal - voucherDiscount);
        decimal shippingFee = Math.Max(0m, finalTotal + voucherDiscount - itemsGrossTotal);

        var vatSummaries = vatGroupDict.Select(kv => new VatRateSummaryItem
        {
            ThueSuatVat = kv.Key,
            TienChuaThue = kv.Value.net,
            TienThueVat = kv.Value.vat,
            TongTienSauThue = kv.Value.gross
        }).OrderBy(x => x.ThueSuatVat).ToList();

        var viewModel = new InvoiceCreateViewModel
        {
            MaDonHang = order.MaDonHang,
            NgayDatHang = order.NgayDat ?? DateTime.Now,
            TrangThaiDonHang = order.TrangThaiDonHang ?? "Chờ xử lý",
            HoTenKhachHang = order.HoTenNhanHang ?? "Khách hàng",
            SoDienThoai = order.SoDienThoaiNhanHang ?? "",
            DiaChiGiaoHang = order.DiaChiNhanHang ?? "",
            PhuongThucThanhToan = paymentMethod,
            PhuongThucVanChuyen = order.PhuongThucVanChuyen ?? "Giao hàng tiêu chuẩn",
            MaNguoiLap = currentUserId,
            TenNguoiLap = staffName,
            TongTienHang = itemsGrossTotal,
            TongTienTruocThue = itemsNetTotal,
            TongTienThueVat = totalVat,
            PhiVanChuyen = shippingFee,
            GiamGiaVoucher = voucherDiscount,
            MaVoucher = voucherCode,
            TongThanhToan = finalTotal,
            GhiChu = order.GhiChu,
            Items = itemsList,
            VatSummaries = vatSummaries
        };

        return View(viewModel);
    }

    // 4. XÁC NHẬN LẬP HÓA ĐƠN VÀ LƯU SNAPSHOT (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InvoiceCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu hóa đơn không hợp lệ. Vui lòng kiểm tra lại.";
            return View(model);
        }

        // Ngăn lập trùng hóa đơn
        var existingInvoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == model.MaDonHang);
        if (existingInvoice != null)
        {
            TempData["Warning"] = $"Đơn hàng #DH-{model.MaDonHang:D5} đã có hóa đơn #HD-{existingInvoice.MaHoaDon:D5}.";
            return RedirectToAction(nameof(Details), new { id = existingInvoice.MaHoaDon });
        }

        var order = await _db.DonHangs
            .Include(d => d.ChiTietDonHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
            .Include(d => d.ThanhToans)
            .FirstOrDefaultAsync(d => d.MaDonHang == model.MaDonHang);

        if (order == null)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng tương ứng.";
            return RedirectToAction("Index", "Order");
        }

        // Xác thực danh tính nhân viên
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(currentUserIdStr, out int currentUserId) || currentUserId <= 0)
        {
            TempData["Error"] = "Phiên làm việc đã hết hạn hoặc không xác thực được tài khoản. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Order");
        }

        var currentUser = await _db.NguoiDungs
            .Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == currentUserId);

        if (currentUser == null || !currentUser.TrangThai)
        {
            TempData["Error"] = "Tài khoản thực hiện không tồn tại hoặc đã bị khóa.";
            return RedirectToAction("Index", "Order");
        }

        var staffName = !string.IsNullOrWhiteSpace(currentUser.TenNguoiDung)
            ? currentUser.TenNguoiDung.Trim()
            : (!string.IsNullOrWhiteSpace(currentUser.TenDangNhap) ? currentUser.TenDangNhap.Trim() : User.Identity?.Name?.Trim());

        // Kiểm tra đối soát tổng tiền bằng backend
        if (order.TongTien.HasValue && Math.Abs(order.TongTien.Value - model.TongThanhToan) > 100m)
        {
            TempData["Error"] = $"Tổng tiền hóa đơn ({model.TongThanhToan:N0} đ) không khớp với số tiền thanh toán của đơn hàng ({order.TongTien.Value:N0} đ). Vui lòng kiểm tra lại.";
            return View(model);
        }

        // Xác định trạng thái hóa đơn dựa vào tình trạng thanh toán
        // COD chưa nhận hàng -> "Chưa thanh toán" / "Đã lập"; Đã giao/chuyển khoản thành công -> "Đã thanh toán"
        var latestPayment = order.ThanhToans.OrderByDescending(p => p.NgayTao).FirstOrDefault();
        bool isPaymentCompleted = (latestPayment != null && (latestPayment.TrangThai == "Thành công" || latestPayment.TrangThai == "Đã thanh toán"))
            || (order.TrangThaiDonHang == "Đã hoàn thành" || order.TrangThaiDonHang == "Đã giao");

        string invoiceStatus = isPaymentCompleted ? "Đã thanh toán" : "Chờ thanh toán";

        var strategy = _db.Database.CreateExecutionStrategy();
        int createdInvoiceId = 0;

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;
                var invoice = new HoaDon
                {
                    MaDonHang = model.MaDonHang,
                    NgayLapHoaDon = now,
                    TongTien = model.TongThanhToan,
                    TrangThaiHoaDon = invoiceStatus,
                    MaNguoiLap = currentUserId,
                    TenNguoiLap = staffName,
                    HoTenKhachHang = model.HoTenKhachHang?.Trim(),
                    SoDienThoai = model.SoDienThoai?.Trim(),
                    DiaChiGiaoHang = model.DiaChiGiaoHang?.Trim(),
                    TienTruocThue = model.TongTienTruocThue,
                    TienThueVat = model.TongTienThueVat,
                    PhiVanChuyen = model.PhiVanChuyen,
                    TienGiamGia = model.GiamGiaVoucher,
                    MaKhm = model.MaVoucher,
                    GhiChu = (model.GhiChu ?? "") + (!string.IsNullOrWhiteSpace(model.MaSoThueNguoiMua) ? $" [MST Người Mua: {model.MaSoThueNguoiMua.Trim()}]" : ""),
                    PhuongThucThanhToan = model.PhuongThucThanhToan
                };

                _db.HoaDons.Add(invoice);
                await _db.SaveChangesAsync();
                createdInvoiceId = invoice.MaHoaDon;

                // Thêm snapshot từng dòng chi tiết với thuế suất và tiền thuế cố định tại thời điểm lập
                if (order.ChiTietDonHangs != null && order.ChiTietDonHangs.Count > 0)
                {
                    foreach (var ct in order.ChiTietDonHangs)
                    {
                        var sp = ct.MaBienTheNavigation?.MaSanPhamNavigation;
                        var bt = ct.MaBienTheNavigation;
                        var catName = sp?.MaDanhMucNavigation?.TenDanhMuc ?? "";
                        var prodName = sp?.TenSanPham ?? "Sản phẩm";

                        var vatRule = VatConfigService.DetermineVatRate(catName, prodName, now);
                        decimal lineGross = ct.ThanhTien;
                        decimal lineVat = VatConfigService.CalculateVatAmount(lineGross, vatRule.RatePercent);
                        decimal lineNet = lineGross - lineVat;
                        decimal unitNet = Math.Round(lineNet / ct.SoLuong, 2, MidpointRounding.AwayFromZero);

                        var detail = new ChiTietHoaDon
                        {
                            MaHoaDon = invoice.MaHoaDon,
                            MaBienThe = ct.MaBienThe,
                            TenSanPham = prodName,
                            BienThe = $"{bt?.MauSac ?? ""} {bt?.PhienBan ?? ""}".Trim(),
                            SoLuong = ct.SoLuong,
                            DonGiaChuaThue = unitNet,
                            ThueSuatVat = vatRule.RatePercent,
                            TienThueVat = lineVat,
                            DonGiaSauThue = ct.DonGia,
                            ChietKhau = 0m,
                            ThanhTien = lineGross
                        };
                        _db.ChiTietHoaDons.Add(detail);
                    }
                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception("Lỗi khi lưu snapshot hóa đơn VAT: " + ex.Message, ex);
            }
        });

        TempData["Success"] = $"Đã lập hóa đơn bán hàng #HD-{createdInvoiceId:D5} thành công!";
        return RedirectToAction(nameof(Details), new { id = createdInvoiceId });
    }

    // 5. XEM CHI TIẾT HÓA ĐƠN
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(x => x.MaNguoiLapNavigation)
            .Include(x => x.ChiTietHoaDons)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);

        if (invoice == null)
        {
            TempData["Error"] = $"Không tìm thấy hóa đơn #HD-{id:D5}.";
            return RedirectToAction(nameof(Index));
        }

        // Tự động nạp fallback từ DonHang nếu là hóa đơn cũ chưa có snapshot
        if (!invoice.ChiTietHoaDons.Any() && invoice.MaDonHangNavigation != null)
        {
            var orderDetails = await _db.ChiTietDonHangs
                .AsNoTracking()
                .Include(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
                .Where(ct => ct.MaDonHang == invoice.MaDonHang)
                .ToListAsync();

            ViewBag.LegacyDetails = orderDetails;
        }

        // Kiểm tra chứng thư chữ ký số demo
        ViewBag.DigitalSignature = DigitalSignatureInfoService.InspectAvailableDigitalCertificate();

        return View(invoice);
    }

    // 6. IN & XUẤT PDF HÓA ĐƠN (CÓ CHỮ KÝ SỐ KỸ THUẬT DEMO)
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var invoice = await _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(x => x.MaNguoiLapNavigation)
            .Include(x => x.ChiTietHoaDons)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);

        if (invoice == null)
        {
            return NotFound($"Không tìm thấy hóa đơn #HD-{id:D5}");
        }

        if (!invoice.ChiTietHoaDons.Any() && invoice.MaDonHangNavigation != null)
        {
            var orderDetails = await _db.ChiTietDonHangs
                .AsNoTracking()
                .Include(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
                .Where(ct => ct.MaDonHang == invoice.MaDonHang)
                .ToListAsync();

            ViewBag.LegacyDetails = orderDetails;
        }

        ViewBag.DigitalSignature = DigitalSignatureInfoService.InspectAvailableDigitalCertificate();

        return View(invoice);
    }

    // 7. BÁO CÁO TỔNG HỢP HÓA ĐƠN CUỐI NGÀY (DAILY INVOICE RECONCILIATION REPORT)
    [HttpGet]
    public async Task<IActionResult> DailyReport(DateTime? date)
    {
        var targetDate = (date ?? DateTime.Today).Date;
        var startOfDay = targetDate;
        var endOfDay = targetDate.AddDays(1).AddTicks(-1);

        // 1. Tổng số đơn hàng phát sinh trong ngày (theo NGÀY ĐẶT HÀNG)
        var totalOrdersCreated = await _db.DonHangs.AsNoTracking()
            .CountAsync(d => d.NgayDat >= startOfDay && d.NgayDat <= endOfDay);

        // 2. Số đơn đã bàn giao trong ngày (theo NGÀY BÀN GIAO THỰC TẾ từ LichSuVanChuyen hoặc trạng thái hoàn thành)
        var deliveredOrderIds = await _db.LichSuVanChuyens.AsNoTracking()
            .Where(ls => ls.ThoiGian >= startOfDay && ls.ThoiGian <= endOfDay 
                      && (ls.TrangThai == "Đã hoàn thành" || ls.TrangThai == "Đã giao" || ls.TrangThai == "Giao hàng thành công"))
            .Select(ls => ls.MaDonHang)
            .Distinct()
            .ToListAsync();

        // Bổ sung đơn có trạng thái hoàn thành trong ngày nếu lịch sử vận chuyển chưa ghi
        var directDelivered = await _db.DonHangs.AsNoTracking()
            .Where(d => d.NgayDat >= startOfDay && d.NgayDat <= endOfDay && (d.TrangThaiDonHang == "Đã hoàn thành" || d.TrangThaiDonHang == "Đã giao"))
            .Select(d => d.MaDonHang)
            .ToListAsync();

        foreach (var did in directDelivered)
        {
            if (!deliveredOrderIds.Contains(did))
                deliveredOrderIds.Add(did);
        }

        int totalOrdersDelivered = deliveredOrderIds.Count;

        // 3. Danh sách hóa đơn phát sinh trong ngày (theo NGÀY LẬP HÓA ĐƠN)
        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(h => h.MaNguoiLapNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon <= endOfDay)
            .OrderByDescending(h => h.MaHoaDon)
            .ToListAsync();

        int totalInvoicesIssued = invoicesToday.Count;

        // 4. Số đơn đã bàn giao nhưng chưa lập hóa đơn (Cần lập HĐ theo thời điểm luật định)
        var deliveredWithoutInvoiceOrders = await _db.DonHangs.AsNoTracking()
            .Include(d => d.HoaDon)
            .Include(d => d.ThanhToans)
            .Where(d => deliveredOrderIds.Contains(d.MaDonHang) && d.HoaDon == null)
            .Select(d => new DailyReportOrderPendingInvoiceItem
            {
                MaDonHang = d.MaDonHang,
                NgayDat = d.NgayDat,
                NgayBanGiao = startOfDay,
                KhachHang = d.HoTenNhanHang ?? "Khách hàng",
                SoDienThoai = d.SoDienThoaiNhanHang ?? "",
                TongTien = d.TongTien ?? 0m,
                TrangThaiDonHang = d.TrangThaiDonHang ?? "Đã giao",
                PhuongThucThanhToan = d.ThanhToans.OrderByDescending(t => t.NgayTao).Select(t => t.PhuongThuc).FirstOrDefault() ?? "COD",
                TrangThaiThanhToan = d.ThanhToans.OrderByDescending(t => t.NgayTao).Select(t => t.TrangThai).FirstOrDefault() ?? (d.TrangThaiDonHang == "Đã hoàn thành" ? "Đã thanh toán" : "Chưa thanh toán")
            })
            .ToListAsync();

        // 5. Tổng hợp tài chính từ các hóa đơn phát sinh trong ngày
        decimal totalGross = invoicesToday.Sum(h => h.TongTien ?? 0m);
        decimal totalNet = invoicesToday.Sum(h => h.TienTruocThue ?? 0m);
        decimal totalVat = invoicesToday.Sum(h => h.TienThueVat ?? 0m);
        decimal totalShip = invoicesToday.Sum(h => h.PhiVanChuyen ?? 0m);
        decimal totalDiscount = invoicesToday.Sum(h => h.TienGiamGia ?? 0m);

        var vatByRateDict = new Dictionary<decimal, decimal>();
        foreach (var inv in invoicesToday)
        {
            if (inv.ChiTietHoaDons != null)
            {
                foreach (var ct in inv.ChiTietHoaDons)
                {
                    if (!vatByRateDict.ContainsKey(ct.ThueSuatVat))
                        vatByRateDict[ct.ThueSuatVat] = 0m;
                    vatByRateDict[ct.ThueSuatVat] += ct.TienThueVat;
                }
            }
        }

        // 6. Phân định tiền đã xác nhận thanh toán vs chưa thanh toán
        decimal totalPaid = 0m;
        decimal totalUnpaid = 0m;

        foreach (var inv in invoicesToday)
        {
            var p = inv.MaDonHangNavigation?.ThanhToans?.OrderByDescending(x => x.NgayTao).FirstOrDefault();
            bool isPaid = inv.TrangThaiHoaDon == "Đã thanh toán" 
                || (p != null && (p.TrangThai == "Thành công" || p.TrangThai == "Đã thanh toán"))
                || (inv.MaDonHangNavigation?.TrangThaiDonHang == "Đã hoàn thành" || inv.MaDonHangNavigation?.TrangThaiDonHang == "Đã giao");

            if (isPaid)
                totalPaid += (inv.TongTien ?? 0m);
            else
                totalUnpaid += (inv.TongTien ?? 0m);
        }

        var vm = new DailyInvoiceReportViewModel
        {
            SelectedDate = targetDate,
            TotalOrdersCreatedToday = totalOrdersCreated,
            TotalOrdersDeliveredToday = totalOrdersDelivered,
            TotalInvoicesIssuedToday = totalInvoicesIssued,
            TotalDeliveredPendingInvoicesCount = deliveredWithoutInvoiceOrders.Count,
            DeliveredPendingInvoiceOrders = deliveredWithoutInvoiceOrders,
            TotalNetRevenue = totalNet,
            TotalVatAmount = totalVat,
            TotalShippingFee = totalShip,
            TotalDiscount = totalDiscount,
            TotalGrossRevenue = totalGross,
            VatAmountByRate = vatByRateDict,
            TotalPaidAmount = totalPaid,
            TotalUnpaidAmount = totalUnpaid,
            InvoicesToday = invoicesToday
        };

        return View(vm);
    }

    // XUẤT CSV BÁO CÁO HÓA ĐƠN CUỐI NGÀY
    [HttpGet]
    public async Task<IActionResult> ExportDailyReportCsv(DateTime? date)
    {
        var targetDate = (date ?? DateTime.Today).Date;
        var startOfDay = targetDate;
        var endOfDay = targetDate.AddDays(1).AddTicks(-1);

        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon <= endOfDay)
            .OrderByDescending(h => h.MaHoaDon)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine($"BÁO CÁO TỔNG HỢP HÓA ĐƠN BÁN HÀNG NGÀY {targetDate:dd/MM/yyyy}");
        sb.AppendLine($"Thời gian xuất báo cáo,{DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine("Ghi chú: Bản báo cáo tổng hợp nội bộ đối soát - Không phải hóa đơn GTGT tổng");
        sb.AppendLine();
        sb.AppendLine("Mã HĐ,Mã ĐH,Ngày Lập,Khách Hàng,SĐT,Tiền Trước Thuế,Tiền VAT,Phí Ship,Giảm Giá,Tổng Thanh Toán,Trạng Thái HĐ,Người Lập");

        foreach (var inv in invoicesToday)
        {
            var dateStr = inv.NgayLapHoaDon?.ToString("dd/MM/yyyy HH:mm") ?? "";
            var cust = (inv.HoTenKhachHang ?? "").Replace(",", " - ");
            var staff = (inv.TenNguoiLap ?? "").Replace(",", " - ");
            sb.AppendLine($"#HD-{inv.MaHoaDon:D5},#DH-{inv.MaDonHang:D5},{dateStr},\"{cust}\",{inv.SoDienThoai},{inv.TienTruocThue ?? 0},{inv.TienThueVat ?? 0},{inv.PhiVanChuyen ?? 0},{inv.TienGiamGia ?? 0},{inv.TongTien ?? 0},\"{inv.TrangThaiHoaDon}\",\"{staff}\"");
        }

        var fileName = $"BaoCao_CuoiNgay_{targetDate:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }
}