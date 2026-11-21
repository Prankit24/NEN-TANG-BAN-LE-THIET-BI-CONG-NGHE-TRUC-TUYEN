using EMua.Services;
using EMua.Areas.Staff.Models;
using EMua.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EMua.Areas.Staff.Controllers;

[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class ReportController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public ReportController(EMuaDbContext db) => _db = db;

    private static DateTime GetVietnamNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(7);
        }
    }

    public async Task<IActionResult> Index(int? month, int? year)
    {
        var vnNow = GetVietnamNow();
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : vnNow.Month;
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : vnNow.Year;
        var firstDayOfMonth = new DateTime(selectedYear, selectedMonth, 1);
        var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);

        var monthlyOrders = await _db.DonHangs.AsNoTracking()
            .Where(x => x.NgayDat >= firstDayOfMonth && x.NgayDat < firstDayOfNextMonth)
            .ToListAsync();

        var successfulStatuses = new[] { "Đã hoàn thành", "Đã giao" };
        var cancelledStatuses = new[] { "Đã hủy", "Đã huỷ" };
        var returnStatuses = new[] { "Yêu cầu đổi trả", "Đang đổi trả", "Đã nhận đổi trả", "Đã hoàn tiền", "Đã hoàn" };
        var pendingStatuses = new[] { "Chờ xử lý", "Đang xử lý", "Chờ xác nhận", "Chờ thanh toán", "Đang giao hàng", "Đang giao", "Giao thất bại" };

        var successfulOrderIds = monthlyOrders
            .Where(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty))
            .Select(x => x.MaDonHang)
            .ToList();

        var bestSellingProducts = await (
            from detail in _db.ChiTietDonHangs.AsNoTracking()
            join variant in _db.BienTheSanPhams.AsNoTracking() on detail.MaBienThe equals variant.MaBienThe
            join product in _db.SanPhams.AsNoTracking() on variant.MaSanPham equals product.MaSanPham
            where successfulOrderIds.Contains(detail.MaDonHang)
            group detail by product.TenSanPham into productGroup
            orderby productGroup.Sum(x => x.SoLuong) descending
            select new ReportProductItem
            {
                Name = productGroup.Key ?? "Chưa đặt tên",
                Quantity = productGroup.Sum(x => x.SoLuong),
                Revenue = productGroup.Sum(x => x.ThanhTien)
            }).Take(5).ToListAsync();

        var favoriteProducts = await (
            from fav in _db.YeuThiches.AsNoTracking()
            join sp in _db.SanPhams.AsNoTracking() on fav.MaSanPham equals sp.MaSanPham
            where fav.NgayThem >= firstDayOfMonth && fav.NgayThem < firstDayOfNextMonth
            group fav by sp.TenSanPham into g
            orderby g.Count() descending
            select new ReportProductItem
            {
                Name = g.Key ?? "Chưa đặt tên",
                Quantity = g.Count()
            }).Take(5).ToListAsync();

        var totalOrders = monthlyOrders.Count;
        var successfulOrders = monthlyOrders.Count(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));
        var cancelledOrders = monthlyOrders.Count(x => cancelledStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));
        var returnedOrders = monthlyOrders.Count(x => returnStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));
        var pendingOrders = monthlyOrders.Count(x => string.IsNullOrWhiteSpace(x.TrangThaiDonHang) || pendingStatuses.Contains(x.TrangThaiDonHang));

        var revenue = monthlyOrders
            .Where(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty))
            .Sum(x => x.TongTien ?? 0);

        var orderStatuses = monthlyOrders
            .GroupBy(x => string.IsNullOrWhiteSpace(x.TrangThaiDonHang) ? "Chờ xử lý" : x.TrangThaiDonHang!)
            .OrderByDescending(group => group.Count())
            .Select(group => new ReportOrderStatusItem { Status = group.Key, Count = group.Count() })
            .ToList();

        var dailyRevenueRaw = monthlyOrders
            .Where(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty) && x.NgayDat.HasValue)
            .GroupBy(x => x.NgayDat!.Value.Day)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.TongTien ?? 0));

        var report = new ReportViewModel
        {
            Revenue = revenue,
            AverageOrderValue = successfulOrders == 0 ? 0 : Math.Round(revenue / successfulOrders, 0),
            TotalOrders = totalOrders,
            SuccessfulOrders = successfulOrders,
            CancelledOrders = cancelledOrders,
            ReturnedOrders = returnedOrders,
            PendingOrders = pendingOrders,
            FavoriteCount = await _db.YeuThiches.AsNoTracking()
                .CountAsync(x => x.NgayThem >= firstDayOfMonth && x.NgayThem < firstDayOfNextMonth),
            LowStockCount = await _db.BienTheSanPhams.AsNoTracking().CountAsync(x => x.SoLuong <= 10),
            SuccessRate = totalOrders == 0 ? 0 : Math.Round((decimal)successfulOrders / totalOrders * 100, 1),
            DailyRevenue = Enumerable.Range(1, DateTime.DaysInMonth(firstDayOfMonth.Year, firstDayOfMonth.Month))
                .Select(day => new ReportDailyRevenueItem { Day = day, Revenue = dailyRevenueRaw.GetValueOrDefault(day) })
                .ToList(),
            OrderStatuses = orderStatuses,
            BestSellingProducts = bestSellingProducts,
            FavoriteProducts = favoriteProducts
        };

        ViewBag.SelectedMonth = selectedMonth;
        ViewBag.SelectedYear = selectedYear;
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int? month, int? year)
    {
        return await ExportExcel(month, year);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(int? month, int? year)
    {
        var result = await Index(month, year) as ViewResult;
        if (result?.Model is not ReportViewModel report) return BadRequest();

        var vnNow = GetVietnamNow();
        var exportYear = year ?? vnNow.Year;
        var exportMonth = month ?? vnNow.Month;

        var columns = new List<ExcelColumn>
        {
            new() { Header = "Hạng mục / Sản phẩm", Width = 38, Type = ExcelCellType.Text },
            new() { Header = "Chỉ số / Số lượng", Width = 20, Type = ExcelCellType.Number },
            new() { Header = "Doanh thu (VNĐ)", Width = 24, Type = ExcelCellType.Currency },
            new() { Header = "Ghi chú / Tỷ lệ", Width = 22, Type = ExcelCellType.TextCenter }
        };

        var rows = new List<object?[]>
        {
            // Nhóm tổng quan
            new object?[] { "DOANH THU THỰC TẾ", null, report.Revenue, $"Kỳ T{exportMonth:00}/{exportYear}" },
            new object?[] { "Tổng số đơn hàng", report.TotalOrders, null, "100%" },
            new object?[] { "Đơn hàng thành công", report.SuccessfulOrders, null, $"{report.SuccessRate:0.0}%" },
            new object?[] { "Đơn hàng bị hủy", report.CancelledOrders, null, null },
            new object?[] { "Đơn hàng đổi trả", report.ReturnedOrders, null, null },
            new object?[] { "Đơn hàng chờ xử lý", report.PendingOrders, null, null },
            new object?[] { "--- TOP SẢN PHẨM BÁN CHẠY ---", null, null, null }
        };

        foreach (var item in report.BestSellingProducts)
        {
            rows.Add(new object?[] { item.Name, item.Quantity, item.Revenue, "Bán chạy" });
        }

        if (report.FavoriteProducts.Any())
        {
            rows.Add(new object?[] { "--- SẢN PHẨM ĐƯỢC YÊU THÍCH NHẤT ---", null, null, null });
            foreach (var item in report.FavoriteProducts)
            {
                rows.Add(new object?[] { item.Name, item.Quantity, null, "Lượt yêu thích" });
            }
        }

        var summary = new object?[] { "TỔNG DOANH THU", report.SuccessfulOrders, report.Revenue, "ĐÃ ĐỐI SOÁT" };

        var fileBytes = ExcelExportService.CreateWorkbook(
            $"BÁO CÁO THỐNG KÊ KINH DOANH THÁNG {exportMonth:00}/{exportYear} - EMUA STORE",
            $"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Nhân viên xuất báo cáo: {User.Identity?.Name ?? "Nhân viên"}",
            columns,
            rows,
            summary);

        var fileName = $"BaoCao_KinhDoanh_{exportYear}_{exportMonth:00}.xlsx";
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
