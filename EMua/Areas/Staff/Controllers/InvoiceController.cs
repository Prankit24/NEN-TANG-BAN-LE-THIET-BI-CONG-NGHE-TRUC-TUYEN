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
            var nextDay = toDate.Value.Date.AddDays(1);
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon < nextDay);
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
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon < toDate.Value.Date.AddDays(1));
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

    // 2.1 XUẤT BÁO CÁO TỔNG HỢP HÓA ĐƠN (EXCEL .XLSX ĐỊNH DẠNG CHUYÊN NGHIỆP)
    [HttpGet]
    public async Task<IActionResult> ExportReportExcel(string? status, int? month, int? year, DateTime? fromDate, DateTime? toDate)
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
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon < toDate.Value.Date.AddDays(1));
        if (month.HasValue && month.Value > 0)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Month == month.Value);
        if (year.HasValue && year.Value > 0)
            invoicesQuery = invoicesQuery.Where(x => x.NgayLapHoaDon != null && x.NgayLapHoaDon.Value.Year == year.Value);

        var list = await invoicesQuery.OrderByDescending(x => x.MaHoaDon).ToListAsync();

        var columns = new List<ExcelColumn>
        {
            new() { Header = "Mã Hóa Đơn", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Mã Đơn Hàng", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Ngày Lập", Width = 18, Type = ExcelCellType.DateTime },
            new() { Header = "Khách Hàng", Width = 25, Type = ExcelCellType.Text },
            new() { Header = "Số Điện Thoại", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Tiền Trước Thuế", Width = 18, Type = ExcelCellType.Currency },
            new() { Header = "Thuế VAT", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Phí Giao Hàng", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Giảm Giá", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Tổng Thanh Toán", Width = 20, Type = ExcelCellType.Currency },
            new() { Header = "Trạng Thái", Width = 16, Type = ExcelCellType.TextCenter },
            new() { Header = "Người Lập", Width = 20, Type = ExcelCellType.Text }
        };

        var rows = new List<object?[]>();
        decimal sumNet = 0, sumVat = 0, sumShip = 0, sumDisc = 0, sumTotal = 0;

        foreach (var inv in list)
        {
            var net = inv.TienTruocThue ?? 0m;
            var vat = inv.TienThueVat ?? 0m;
            var ship = inv.PhiVanChuyen ?? 0m;
            var disc = inv.TienGiamGia ?? 0m;
            var gross = inv.TongTien ?? 0m;

            sumNet += net;
            sumVat += vat;
            sumShip += ship;
            sumDisc += disc;
            sumTotal += gross;

            rows.Add(new object?[]
            {
                $"#HD-{inv.MaHoaDon:D5}",
                $"#DH-{inv.MaDonHang:D5}",
                inv.NgayLapHoaDon,
                inv.HoTenKhachHang ?? "",
                inv.SoDienThoai ?? "",
                net,
                vat,
                ship,
                disc,
                gross,
                inv.TrangThaiHoaDon ?? "Đã lập",
                inv.TenNguoiLap ?? "Hệ thống"
            });
        }

        var summary = new object?[]
        {
            "TỔNG CỘNG",
            $"{list.Count} hóa đơn",
            null,
            null,
            null,
            sumNet,
            sumVat,
            sumShip,
            sumDisc,
            sumTotal,
            "ĐÃ ĐỐI SOÁT",
            null
        };

        var fileBytes = ExcelExportService.CreateWorkbook(
            "BÁO CÁO TỔNG HỢP HÓA ĐƠN BÁN HÀNG - EMUA TECHNOLOGY STORE",
            $"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Bộ lọc: {status ?? "Tất cả"} | Tổng số: {list.Count} chứng từ",
            columns,
            rows,
            summary);

        var fileName = $"BaoCao_HoaDon_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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
        var existingInvoice = await _db.HoaDons.AsNoTracking().Include(x => x.ChiTietHoaDons).FirstOrDefaultAsync(x => x.MaDonHang == orderId);
        if (existingInvoice != null && (existingInvoice.MaNguoiLap.HasValue || existingInvoice.ChiTietHoaDons.Any()))
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
        decimal voucherDiscount, shippingFee;
        try
        {
            OrderWorkflowService.ValidateExistingPricing(order);
            voucherDiscount = order.TienGiamGiaDaChot ?? 0m;
            shippingFee = order.PhiVanChuyenDaChot ?? OrderRules.ShippingFee(itemsGrossTotal, OrderRules.IsExpress(order.PhuongThucVanChuyen));
        }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; return RedirectToAction("Details", "Order", new { id = orderId }); }
        string? voucherCode = order.MaKhuyenMaiNavigation?.MaCode;
        decimal finalTotal = order.TongTien ?? 0m;

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
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffId) || staffId <= 0) return Challenge();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.HoTenKhachHang) || model.HoTenKhachHang.Length > 100 || string.IsNullOrWhiteSpace(model.SoDienThoai) || model.SoDienThoai.Length > 20 || string.IsNullOrWhiteSpace(model.DiaChiGiaoHang) || model.DiaChiGiaoHang.Length > 255 || (model.GhiChu?.Length ?? 0) > 350 || (model.MaSoThueNguoiMua?.Length ?? 0) > 50)
        { TempData["Error"] = "Dữ liệu chưa hợp lệ: ghi chú tối đa 350 ký tự, mã số thuế tối đa 50 ký tự."; return RedirectToAction(nameof(Create), new { orderId = model.MaDonHang }); }
        try
        {
            var invoiceId = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT madonhang FROM public.donhang WHERE madonhang = {model.MaDonHang} FOR UPDATE");
                var order = await _db.DonHangs.Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation).ThenInclude(x => x.MaDanhMucNavigation)
                    .Include(x => x.ThanhToans).Include(x => x.MaKhuyenMaiNavigation).Include(x => x.HoaDon).ThenInclude(x => x!.ChiTietHoaDons)
                    .AsSplitQuery().FirstOrDefaultAsync(x => x.MaDonHang == model.MaDonHang);
                var actor = await _db.NguoiDungs.Include(x => x.MaQuyenNavigation).AsNoTracking().FirstOrDefaultAsync(x => x.MaNguoiDung == staffId && x.TrangThai);
                if (actor == null || !(new[] { "Admin", "Quản trị viên", "QuanTriVien", "NhanVien", "Nhân viên" }.Contains(actor.MaQuyenNavigation.TenQuyen)))
                    throw new ArgumentException("Tài khoản đã khóa hoặc không có quyền lập hóa đơn.");
                if (order == null) throw new ArgumentException("Không tìm thấy đơn hàng.");
                if (OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.Cancelled || new[] { OrderRules.ReturnRequested, OrderRules.Returning, OrderRules.ReturnReceived, OrderRules.Refunded }.Contains(OrderRules.Normalize(order.TrangThaiDonHang)))
                    throw new ArgumentException("Đơn hủy/đổi trả phải xử lý chứng từ riêng, không lập mới hóa đơn bán hàng tại đây.");
                if (order.HoaDon != null && (order.HoaDon.MaNguoiLap.HasValue || order.HoaDon.ChiTietHoaDons.Any())) return order.HoaDon.MaHoaDon;
                OrderWorkflowService.ValidateExistingPricing(order);
                if (order.ThanhToans.Count != 1 || order.ThanhToans.Single().SoTien != order.TongTien)
                    throw new ArgumentException("Số tiền thanh toán chưa khớp đơn. Cần đối soát trước khi lập hóa đơn.");
                if (model.TongThanhToan != order.TongTien) throw new ArgumentException("Đơn đã thay đổi tổng tiền. Mở lại màn hình lập hóa đơn để kiểm tra số tiền mới.");
                var payment = order.ThanhToans.Single();
                var gross = order.ChiTietDonHangs.Sum(x => x.ThanhTien);
                var fee = order.PhiVanChuyenDaChot ?? OrderRules.ShippingFee(gross, OrderRules.IsExpress(order.PhuongThucVanChuyen));
                var discount = order.TienGiamGiaDaChot ?? 0m;
                var now = OrderRules.VietnamNow();
                var invoice = order.HoaDon ?? new HoaDon { MaDonHang = order.MaDonHang };
                if (order.HoaDon == null) _db.HoaDons.Add(invoice);
                invoice.NgayLapHoaDon = now; invoice.TongTien = order.TongTien;
                invoice.TrangThaiHoaDon = OrderRules.IsPaid(payment.TrangThai) ? OrderRules.Paid : "Chờ thanh toán";
                invoice.MaNguoiLap = staffId; invoice.TenNguoiLap = actor.TenNguoiDung ?? actor.TenDangNhap;
                invoice.HoTenKhachHang = model.HoTenKhachHang.Trim(); invoice.SoDienThoai = model.SoDienThoai.Trim();
                invoice.DiaChiGiaoHang = model.DiaChiGiaoHang.Trim(); invoice.PhiVanChuyen = fee; invoice.TienGiamGia = discount;
                invoice.MaKhm = order.MaKhuyenMaiNavigation?.MaCode; invoice.PhuongThucThanhToan = payment.PhuongThuc;
                invoice.GhiChu = (model.GhiChu ?? "").Trim() + (string.IsNullOrWhiteSpace(model.MaSoThueNguoiMua) ? "" : $" [MST Người Mua: {model.MaSoThueNguoiMua.Trim()}]");
                decimal tax = 0m, net = 0m;
                foreach (var ct in order.ChiTietDonHangs)
                {
                    var product = ct.MaBienTheNavigation?.MaSanPhamNavigation;
                    var variant = ct.MaBienTheNavigation;
                    // Giữ quy tắc VAT hiện có; toàn bộ giá trị được tính từ dữ liệu đơn ở máy chủ.
                    var rule = VatConfigService.DetermineVatRate(product?.MaDanhMucNavigation?.TenDanhMuc ?? "", product?.TenSanPham ?? "", order.NgayDat ?? now);
                    var lineTax = VatConfigService.CalculateVatAmount(ct.ThanhTien, rule.RatePercent);
                    tax += lineTax; net += ct.ThanhTien - lineTax;
                    invoice.ChiTietHoaDons.Add(new ChiTietHoaDon { MaBienThe = ct.MaBienThe, TenSanPham = product?.TenSanPham ?? "Sản phẩm",
                        BienThe = $"{variant?.MauSac} {variant?.PhienBan}".Trim(), SoLuong = ct.SoLuong,
                        DonGiaChuaThue = Math.Round((ct.ThanhTien - lineTax) / ct.SoLuong, 2, MidpointRounding.AwayFromZero),
                        ThueSuatVat = rule.RatePercent, TienThueVat = lineTax, DonGiaSauThue = ct.DonGia, ChietKhau = 0m, ThanhTien = ct.ThanhTien });
                }
                invoice.TienTruocThue = net; invoice.TienThueVat = tax;
                await _db.SaveChangesAsync(); await tx.CommitAsync(); return invoice.MaHoaDon;
            });
            TempData["Success"] = "Đã lưu hóa đơn theo số tiền của đơn hàng. Đơn đã lập hóa đơn sẽ khóa sửa số lượng.";
            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Create), new { orderId = model.MaDonHang }); }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<InvoiceController>>().LogError(ex, "Lỗi lập hóa đơn đơn {OrderId}", model.MaDonHang);
            TempData["Error"] = "Chưa xác định được kết quả lập hóa đơn. Kiểm tra danh sách hóa đơn trước khi thử lại.";
            return RedirectToAction(nameof(Index));
        }
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
        var targetDate = (date ?? OrderRules.VietnamNow()).Date;
        var startOfDay = targetDate;
        var nextDay = targetDate.AddDays(1);

        // 1. Tổng số đơn hàng phát sinh trong ngày (theo NGÀY ĐẶT HÀNG)
        var totalOrdersCreated = await _db.DonHangs.AsNoTracking()
            .CountAsync(d => d.NgayDat >= startOfDay && d.NgayDat < nextDay);

        // 2. Số đơn đã bàn giao trong ngày (theo NGÀY BÀN GIAO THỰC TẾ từ LichSuVanChuyen hoặc trạng thái hoàn thành)
        var deliveredOrderIds = await _db.LichSuVanChuyens.AsNoTracking()
            .Where(ls => ls.ThoiGian >= startOfDay && ls.ThoiGian < nextDay 
                      && (ls.TrangThai == "Đã hoàn thành" || ls.TrangThai == "Đã giao" || ls.TrangThai == "Giao hàng thành công"))
            .Select(ls => ls.MaDonHang)
            .Distinct()
            .ToListAsync();

        // Chỉ tính giao hàng theo sự kiện bàn giao thực tế; không dùng ngày đặt hàng.
        int totalOrdersDelivered = deliveredOrderIds.Count;

        // 3. Danh sách hóa đơn phát sinh trong ngày (theo NGÀY LẬP HÓA ĐƠN)
        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(h => h.MaNguoiLapNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon < nextDay)
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
                KhachHang = d.HoTenNhanHang ?? "Khách hàng",
                SoDienThoai = d.SoDienThoaiNhanHang ?? "",
                TongTien = d.TongTien ?? 0m,
                TrangThaiDonHang = d.TrangThaiDonHang ?? "Đã giao",
                PhuongThucThanhToan = d.ThanhToans.OrderByDescending(t => t.NgayTao).Select(t => t.PhuongThuc).FirstOrDefault() ?? "COD",
                TrangThaiThanhToan = d.ThanhToans.OrderByDescending(t => t.NgayTao).Select(t => t.TrangThai).FirstOrDefault() ?? "Chưa xác định"
            })
            .ToListAsync();

        foreach (var item in deliveredWithoutInvoiceOrders)
        {
            item.NgayBanGiao = startOfDay;
        }

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
            bool isPaid = p != null && OrderRules.IsPaid(p.TrangThai);

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
        var targetDate = (date ?? OrderRules.VietnamNow()).Date;
        var startOfDay = targetDate;
        var nextDay = targetDate.AddDays(1);

        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon < nextDay)
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

    // XUẤT EXCEL (.XLSX) BÁO CÁO HÓA ĐƠN CUỐI NGÀY
    [HttpGet]
    public async Task<IActionResult> ExportDailyReportExcel(DateTime? date)
    {
        var targetDate = (date ?? OrderRules.VietnamNow()).Date;
        var startOfDay = targetDate;
        var nextDay = targetDate.AddDays(1);

        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon < nextDay)
            .OrderByDescending(h => h.MaHoaDon)
            .ToListAsync();

        var columns = new List<ExcelColumn>
        {
            new() { Header = "Mã HĐ", Width = 14, Type = ExcelCellType.TextCenter },
            new() { Header = "Mã ĐH", Width = 14, Type = ExcelCellType.TextCenter },
            new() { Header = "Giờ Lập", Width = 14, Type = ExcelCellType.DateTime },
            new() { Header = "Khách Hàng", Width = 26, Type = ExcelCellType.Text },
            new() { Header = "SĐT", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Tiền Trước Thuế", Width = 18, Type = ExcelCellType.Currency },
            new() { Header = "Tiền VAT", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Phí Vận Chuyển", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Giảm Giá", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Tổng Thanh Toán", Width = 20, Type = ExcelCellType.Currency },
            new() { Header = "Trạng Thái", Width = 16, Type = ExcelCellType.TextCenter },
            new() { Header = "Nhân Viên Lập", Width = 20, Type = ExcelCellType.Text }
        };

        var rows = new List<object?[]>();
        decimal sumNet = 0, sumVat = 0, sumShip = 0, sumDisc = 0, sumTotal = 0;

        foreach (var inv in invoicesToday)
        {
            var net = inv.TienTruocThue ?? 0m;
            var vat = inv.TienThueVat ?? 0m;
            var ship = inv.PhiVanChuyen ?? 0m;
            var disc = inv.TienGiamGia ?? 0m;
            var gross = inv.TongTien ?? 0m;

            sumNet += net;
            sumVat += vat;
            sumShip += ship;
            sumDisc += disc;
            sumTotal += gross;

            rows.Add(new object?[]
            {
                $"#HD-{inv.MaHoaDon:D5}",
                $"#DH-{inv.MaDonHang:D5}",
                inv.NgayLapHoaDon,
                inv.HoTenKhachHang ?? "",
                inv.SoDienThoai ?? "",
                net,
                vat,
                ship,
                disc,
                gross,
                inv.TrangThaiHoaDon ?? "Đã lập",
                inv.TenNguoiLap ?? "Hệ thống"
            });
        }

        var summary = new object?[]
        {
            "TỔNG CUỐI NGÀY",
            $"{invoicesToday.Count} HĐ",
            null,
            null,
            null,
            sumNet,
            sumVat,
            sumShip,
            sumDisc,
            sumTotal,
            "ĐÃ KHÓA SỔ",
            null
        };

        var fileBytes = ExcelExportService.CreateWorkbook(
            $"BÁO CÁO DOANH THU & HÓA ĐƠN BÁN HÀNG NGÀY {targetDate:dd/MM/yyyy} - EMUA STORE",
            $"Thời gian xuất sổ: {DateTime.Now:dd/MM/yyyy HH:mm} | Nhân viên trực: {User.Identity?.Name ?? "Nhân viên"} | Tổng: {invoicesToday.Count} hóa đơn",
            columns,
            rows,
            summary);

        var fileName = $"BaoCao_CuoiNgay_{targetDate:yyyyMMdd}.xlsx";
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}