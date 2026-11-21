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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class InvoiceController : Controller
{
    private readonly EMuaDbContext _db;

    public InvoiceController(EMuaDbContext db)
    {
        _db = db;
    }

    // 1. DANH SÁCH HÓA ĐƠN & BÁO CÁO THỐNG KÊ (ADMIN)
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

    // 2.1 XUẤT BÁO CÁO TỔNG HỢP HÓA ĐƠN (EXCEL .XLSX)
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

    // 3. XEM CHI TIẾT HÓA ĐƠN
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

    // 4. IN & XUẤT PDF HÓA ĐƠN
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

    // 5. BÁO CÁO TỔNG HỢP HÓA ĐƠN CUỐI NGÀY
    [HttpGet]
    public async Task<IActionResult> DailyReport(DateTime? date)
    {
        var targetDate = (date ?? OrderRules.VietnamNow()).Date;
        var startOfDay = targetDate;
        var nextDay = targetDate.AddDays(1);

        var totalOrdersCreated = await _db.DonHangs.AsNoTracking()
            .CountAsync(d => d.NgayDat >= startOfDay && d.NgayDat < nextDay);

        var deliveredOrderIds = await _db.LichSuVanChuyens.AsNoTracking()
            .Where(ls => ls.ThoiGian >= startOfDay && ls.ThoiGian < nextDay
                      && (ls.TrangThai == "Đã hoàn thành" || ls.TrangThai == "Đã giao" || ls.TrangThai == "Giao hàng thành công"))
            .Select(ls => ls.MaDonHang)
            .Distinct()
            .ToListAsync();

        int totalOrdersDelivered = deliveredOrderIds.Count;

        var invoicesToday = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(h => h.MaNguoiLapNavigation)
            .Include(h => h.ChiTietHoaDons)
            .Where(h => h.NgayLapHoaDon >= startOfDay && h.NgayLapHoaDon < nextDay)
            .OrderByDescending(h => h.MaHoaDon)
            .ToListAsync();

        int totalInvoicesIssued = invoicesToday.Count;

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

    // 6. XUẤT CSV BÁO CÁO HÓA ĐƠN CUỐI NGÀY
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

    // 7. XUẤT EXCEL (.XLSX) BÁO CÁO HÓA ĐƠN CUỐI NGÀY
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
            new() { Header = "Mã Hóa Đơn", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Mã Đơn Hàng", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Thời Gian Lập", Width = 18, Type = ExcelCellType.DateTime },
            new() { Header = "Khách Hàng", Width = 25, Type = ExcelCellType.Text },
            new() { Header = "Số Điện Thoại", Width = 15, Type = ExcelCellType.TextCenter },
            new() { Header = "Tiền Trước Thuế", Width = 18, Type = ExcelCellType.Currency },
            new() { Header = "Tiền Thuế VAT", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Phí Giao Hàng", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Tiền Giảm Giá", Width = 16, Type = ExcelCellType.Currency },
            new() { Header = "Tổng Doanh Thu", Width = 20, Type = ExcelCellType.Currency },
            new() { Header = "Trạng Thái", Width = 16, Type = ExcelCellType.TextCenter },
            new() { Header = "Người Lập", Width = 20, Type = ExcelCellType.Text }
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
            "TỔNG CỘNG NGÀY",
            $"{invoicesToday.Count} chứng từ",
            null,
            null,
            null,
            sumNet,
            sumVat,
            sumShip,
            sumDisc,
            sumTotal,
            "ĐỐI SOÁT CUỐI NGÀY",
            null
        };

        var fileBytes = ExcelExportService.CreateWorkbook(
            $"BÁO CÁO TỔNG HỢP HÓA ĐƠN BÁN HÀNG NGÀY {targetDate:dd/MM/yyyy}",
            $"Thời gian kết xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Số chứng từ phát sinh: {invoicesToday.Count}",
            columns,
            rows,
            summary);

        var fileName = $"BaoCao_CuoiNgay_{targetDate:yyyyMMdd}.xlsx";
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
