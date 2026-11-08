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

[Area("Staff")]
[Authorize(Roles = "NhanVien, Nhân viên, Quản trị viên, Admin, QuanTriVien")]
public class DashboardController : StaffControllerBase
{
    private readonly EMuaDbContext _db;

    public DashboardController(EMuaDbContext db)
    {
        _db = db;
    }

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

    [HttpGet]
    public async Task<IActionResult> Index(int? month, int? year)
    {
        var vnNow = GetVietnamNow();

        var selectedMonth = month is >= 1 and <= 12
            ? month.Value
            : vnNow.Month;

        var selectedYear = year is >= 2000 and <= 2100
            ? year.Value
            : vnNow.Year;

        var monthStart = new DateTime(selectedYear, selectedMonth, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var previousMonthStart = monthStart.AddMonths(-1);

        var firstYearDay = new DateTime(selectedYear, 1, 1);
        var nextYearDay = firstYearDay.AddYears(1);

        var successfulStatuses = new[] { "Đã hoàn thành", "Đã giao" };

        var orders = _db.DonHangs.AsNoTracking();

        // 1. Số liệu việc cần xử lý ngay (toàn hệ thống)
        var pendingOrderCount = await orders.CountAsync(x => x.TrangThaiDonHang == null || x.TrangThaiDonHang == "Chờ xử lý" || x.TrangThaiDonHang == "Chờ xác nhận");
        var returnRequestCount = await orders.CountAsync(x => x.TrangThaiDonHang != null && (x.TrangThaiDonHang.Contains("đổi trả") || x.TrangThaiDonHang.Contains("Đổi trả") || x.TrangThaiDonHang.Contains("hoàn tiền")));
        var processingOrderCount = await orders.CountAsync(x => x.TrangThaiDonHang == "Đang xử lý");

        var successfulOrders = orders.Where(x =>
            x.NgayDat != null &&
            successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));

        var trackedOrders = orders.Where(x =>
            x.NgayDat != null &&
            x.TrangThaiDonHang != "Đã hủy" &&
            x.TrangThaiDonHang != "Đã huỷ");

        // Đơn hàng trong tháng đang chọn
        var thisMonthOrders = trackedOrders.Where(x => x.NgayDat >= monthStart && x.NgayDat < nextMonthStart);
        var previousMonthOrders = trackedOrders.Where(x => x.NgayDat >= previousMonthStart && x.NgayDat < monthStart);

        // Đơn thành công trong tháng đang chọn
        var thisMonthSuccessfulOrders = successfulOrders.Where(x => x.NgayDat >= monthStart && x.NgayDat < nextMonthStart);
        var previousMonthSuccessfulOrders = successfulOrders.Where(x => x.NgayDat >= previousMonthStart && x.NgayDat < monthStart);

        // Doanh thu
        var revenue = await thisMonthSuccessfulOrders.SumAsync(x => x.TongTien ?? 0);
        var previousRevenue = await previousMonthSuccessfulOrders.SumAsync(x => x.TongTien ?? 0);

        // Số đơn hàng
        var orderCount = await thisMonthOrders.CountAsync();
        var previousOrderCount = await previousMonthOrders.CountAsync();

        // Khách hàng mới (MaQuyen = 3)
        var newCustomers = await _db.NguoiDungs
            .AsNoTracking()
            .CountAsync(x =>
                x.MaQuyen == 3 &&
                x.NgayTao >= monthStart &&
                x.NgayTao < nextMonthStart);

        // Tổng số lượng sản phẩm đã bán
        var soldUnits = await (
            from detail in _db.ChiTietDonHangs.AsNoTracking()
            join order in thisMonthSuccessfulOrders
                on detail.MaDonHang equals order.MaDonHang
            select (int?)detail.SoLuong
        ).SumAsync() ?? 0;

        // Doanh thu 12 tháng trong năm
        var revenueByMonthRaw = await successfulOrders
            .Where(x => x.NgayDat >= firstYearDay && x.NgayDat < nextYearDay)
            .GroupBy(x => x.NgayDat!.Value.Month)
            .Select(group => new
            {
                Month = group.Key,
                Revenue = group.Sum(x => x.TongTien ?? 0)
            })
            .ToListAsync();

        var revenueByMonth = Enumerable.Range(1, 12)
            .Select(monthNumber =>
                revenueByMonthRaw.FirstOrDefault(x => x.Month == monthNumber)?.Revenue ?? 0)
            .ToList();

        // Cảnh báo tồn kho
        var lowStockQuery = _db.BienTheSanPhams
            .AsNoTracking()
            .Where(x => x.SoLuong <= 10);

        var lowStockCount = await lowStockQuery.CountAsync();

        var lowStockRaw = await lowStockQuery
            .Include(x => x.MaSanPhamNavigation)
            .OrderBy(x => x.SoLuong)
            .ThenBy(x => x.MaBienThe)
            .Take(5)
            .ToListAsync();

        var lowStock = lowStockRaw
            .Select(item => new StockAlertItem
            {
                ProductName = item.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm chưa đặt tên",
                VariantName = string.Join(" - ", new[] { item.MauSac, item.PhienBan }.Where(v => !string.IsNullOrWhiteSpace(v))),
                Quantity = item.SoLuong
            })
            .ToList();

        // 5 đơn gần đây
        var recentOrdersRaw = await orders
            .Include(x => x.MaNguoiDungNavigation)
            .Include(x => x.ChiTietDonHangs)
                .ThenInclude(x => x.MaBienTheNavigation)
                    .ThenInclude(x => x.MaSanPhamNavigation)
            .OrderByDescending(x => x.NgayDat)
            .ThenByDescending(x => x.MaDonHang)
            .Take(5)
            .ToListAsync();

        var recentOrders = recentOrdersRaw
            .Select(order => new DashboardOrderItem
            {
                OrderId = order.MaDonHang,
                CustomerName = !string.IsNullOrWhiteSpace(order.HoTenNhanHang)
                    ? order.HoTenNhanHang
                    : order.MaNguoiDungNavigation?.TenNguoiDung ?? "Khách hàng",
                CustomerEmail = order.MaNguoiDungNavigation?.Email,
                ProductSummary = string.Join(", ", order.ChiTietDonHangs.Take(2).Select(d => d.MaBienTheNavigation?.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm")),
                OrderedAt = order.NgayDat,
                Total = order.TongTien ?? 0,
                Status = order.TrangThaiDonHang ?? "Chờ xử lý"
            })
            .ToList();

        var model = new StaffDashboardViewModel
        {
            SelectedMonth = selectedMonth,
            SelectedYear = selectedYear,
            Revenue = revenue,
            RevenueGrowth = CalculateGrowth(revenue, previousRevenue),
            OrderCount = orderCount,
            OrderGrowth = CalculateGrowth(orderCount, previousOrderCount),
            SoldUnits = soldUnits,
            NewCustomers = newCustomers,
            LowStockCount = lowStockCount,
            LowStockItems = lowStock,
            PendingOrderCount = pendingOrderCount,
            ReturnRequestCount = returnRequestCount,
            ProcessingOrderCount = processingOrderCount,
            RevenueByMonth = revenueByMonth,
            RecentOrders = recentOrders
        };

        return View(model);
    }

    private static decimal CalculateGrowth(decimal current, decimal previous)
    {
        if (previous <= 0)
        {
            return current > 0 ? 100 : 0;
        }

        return Math.Round((current - previous) / previous * 100, 1);
    }
}
