using EMua.Areas.Staff.Models;
using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class DashboardController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public DashboardController(EMuaDbContext db) => _db = db;

    public async Task<IActionResult> Index(int? month, int? year)
    {
        var today = DateTime.Today;
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : today.Month;
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : today.Year;
        var monthStart = new DateTime(selectedYear, selectedMonth, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var previousMonthStart = monthStart.AddMonths(-1);

        var successfulStatuses = new[] { "Đã hoàn thành", "Đã giao" };
        var validOrders = _db.DonHangs.Where(x =>
            x.NgayDat != null &&
            successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));
        var trackedOrders = _db.DonHangs.Where(x =>
            x.NgayDat != null &&
            x.TrangThaiDonHang != "Đã hủy" &&
            x.TrangThaiDonHang != "Đã huỷ");

        var thisMonthOrders = trackedOrders.Where(x =>
            x.NgayDat >= monthStart && x.NgayDat < nextMonthStart);
        var previousMonthOrders = trackedOrders.Where(x =>
            x.NgayDat >= previousMonthStart && x.NgayDat < monthStart);
        var thisMonthSuccessfulOrders = validOrders.Where(x =>
            x.NgayDat >= monthStart && x.NgayDat < nextMonthStart);
        var previousMonthSuccessfulOrders = validOrders.Where(x =>
            x.NgayDat >= previousMonthStart && x.NgayDat < monthStart);

        var revenue = await thisMonthSuccessfulOrders.SumAsync(x => x.TongTien ?? 0);
        var previousRevenue = await previousMonthSuccessfulOrders.SumAsync(x => x.TongTien ?? 0);
        var orderCount = await thisMonthOrders.CountAsync();
        var previousOrderCount = await previousMonthOrders.CountAsync();
        var newCustomers = await _db.NguoiDungs.CountAsync(x =>
            x.MaQuyen == 3 && x.NgayTao >= monthStart && x.NgayTao < nextMonthStart);

        var soldUnits = await (
            from detail in _db.ChiTietDonHangs
            join order in thisMonthSuccessfulOrders on detail.MaDonHang equals order.MaDonHang
            select (int?)detail.SoLuong).SumAsync() ?? 0;

        var firstYearDay = new DateTime(selectedYear, 1, 1);
        var revenueByMonthRaw = await validOrders
            .Where(x => x.NgayDat >= firstYearDay && x.NgayDat < firstYearDay.AddYears(1))
            .GroupBy(x => x.NgayDat!.Value.Month)
            .Select(x => new { Month = x.Key, Revenue = x.Sum(y => y.TongTien ?? 0) })
            .ToListAsync();

        var lowStockRaw = await _db.BienTheSanPhams.AsNoTracking()
            .Include(x => x.MaSanPhamNavigation)
            .Where(x => x.SoLuong <= 10)
            .OrderBy(x => x.SoLuong)
            .Take(4)
            .ToListAsync();
        var lowStockCount = await _db.BienTheSanPhams.AsNoTracking()
            .CountAsync(x => x.SoLuong <= 10);

        var lowStock = lowStockRaw.Select(x => new StockAlertItem
        {
            ProductName = x.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm chưa đặt tên",
            VariantName = string.Join(" - ", new[] { x.MauSac, x.PhienBan }.Where(v => !string.IsNullOrWhiteSpace(v))),
            Quantity = x.SoLuong
        }).ToList();

        var recentOrdersRaw = await _db.DonHangs.AsNoTracking()
            .Include(x => x.MaNguoiDungNavigation)
            .Include(x => x.ChiTietDonHangs)
                .ThenInclude(x => x.MaBienTheNavigation)
                    .ThenInclude(x => x.MaSanPhamNavigation)
            .OrderByDescending(x => x.NgayDat)
            .Take(5)
            .ToListAsync();

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
            RevenueByMonth = Enumerable.Range(1, 12)
                .Select(month => revenueByMonthRaw.FirstOrDefault(x => x.Month == month)?.Revenue ?? 0)
                .ToList(),
            LowStockItems = lowStock,
            RecentOrders = recentOrdersRaw.Select(order => new DashboardOrderItem
            {
                OrderId = order.MaDonHang,
                CustomerName = order.HoTenNhanHang ?? order.MaNguoiDungNavigation?.TenNguoiDung ?? "Khách hàng",
                CustomerEmail = order.MaNguoiDungNavigation?.Email,
                ProductSummary = string.Join(", ", order.ChiTietDonHangs.Take(2).Select(x => x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm")),
                OrderedAt = order.NgayDat,
                Total = order.TongTien ?? 0,
                Status = order.TrangThaiDonHang ?? "Chờ xử lý"
            }).ToList()
        };

        return View(model);
    }

    private static decimal CalculateGrowth(decimal current, decimal previous) =>
        previous <= 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) / previous * 100, 1);
}
