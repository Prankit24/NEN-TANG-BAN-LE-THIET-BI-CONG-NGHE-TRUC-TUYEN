using EMua.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DashboardController : Controller
{
    private readonly EMuaDbContext _db;

    public DashboardController(EMuaDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        // Kiểm tra xem User có phải là Admin/Quản trị viên không
        var isAuthorized = User.IsInRole("Quản trị viên") || User.IsInRole("Admin") || User.IsInRole("QuanTriVien");

        if (!isAuthorized)
        {
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        var now = DateTime.Now;

        var firstDayThisMonth = new DateTime(now.Year, now.Month, 1);
        var firstDayNextMonth = firstDayThisMonth.AddMonths(1);
        var firstDayLastMonth = firstDayThisMonth.AddMonths(-1);

        var firstDayThisYear = new DateTime(now.Year, 1, 1);
        var firstDayNextYear = firstDayThisYear.AddYears(1);

        var validOrders = _db.DonHangs.Where(x =>
            x.NgayDat != null &&
            (x.TrangThaiDonHang == null ||
             (x.TrangThaiDonHang != "Đã hủy" &&
              x.TrangThaiDonHang != "Đã huỷ")));

        var thisMonthOrders = validOrders.Where(x =>
            x.NgayDat >= firstDayThisMonth &&
            x.NgayDat < firstDayNextMonth);

        var lastMonthOrders = validOrders.Where(x =>
            x.NgayDat >= firstDayLastMonth &&
            x.NgayDat < firstDayThisMonth);

        var revenueThisMonth = await thisMonthOrders
            .SumAsync(x => x.TongTien ?? 0);

        var revenueLastMonth = await lastMonthOrders
            .SumAsync(x => x.TongTien ?? 0);

        var orderCountThisMonth = await thisMonthOrders.CountAsync();
        var orderCountLastMonth = await lastMonthOrders.CountAsync();

        var newCustomersThisMonth = await _db.NguoiDungs.CountAsync(x =>
            x.MaQuyen == 3 &&
            x.NgayTao >= firstDayThisMonth &&
            x.NgayTao < firstDayNextMonth);

        var newCustomersLastMonth = await _db.NguoiDungs.CountAsync(x =>
            x.MaQuyen == 3 &&
            x.NgayTao >= firstDayLastMonth &&
            x.NgayTao < firstDayThisMonth);

        var rawMonthlyRevenue = await validOrders
            .Where(x =>
                x.NgayDat >= firstDayThisYear &&
                x.NgayDat < firstDayNextYear)
            .GroupBy(x => x.NgayDat!.Value.Month)
            .Select(group => new
            {
                Month = group.Key,
                Revenue = group.Sum(x => x.TongTien ?? 0)
            })
            .ToListAsync();

        var monthlyRevenue = Enumerable.Range(1, 12)
            .Select(month =>
                rawMonthlyRevenue
                    .FirstOrDefault(x => x.Month == month)
                    ?.Revenue ?? 0)
            .ToList();

        var categorySales = await (
            from detail in _db.ChiTietDonHangs
            join order in validOrders
                on detail.MaDonHang equals order.MaDonHang
            join variant in _db.BienTheSanPhams
                on detail.MaBienThe equals variant.MaBienThe
            join product in _db.SanPhams
                on variant.MaSanPham equals product.MaSanPham
            join category in _db.DanhMucSanPhams
                on product.MaDanhMuc equals category.MaDanhMuc
            group detail by category.TenDanhMuc into categoryGroup
            select new
            {
                Name = categoryGroup.Key ?? "Chưa phân loại",
                Total = categoryGroup.Sum(x => x.ThanhTien)
            })
            .OrderByDescending(x => x.Total)
            .Take(6)
            .ToListAsync();

        var maxCategorySales = categorySales.Count == 0
            ? 1
            : categorySales.Max(x => x.Total);

        var recentOrdersRaw = await (
            from order in _db.DonHangs
            join user in _db.NguoiDungs
                on order.MaNguoiDung equals user.MaNguoiDung into users
            from user in users.DefaultIfEmpty()
            orderby order.NgayDat descending
            select new
            {
                OrderCode = $"#DH-{order.MaDonHang:D5}",
                CustomerName = order.HoTenNhanHang
                    ?? user.TenNguoiDung
                    ?? "Khách hàng",
                Total = order.TongTien ?? 0,
                Status = order.TrangThaiDonHang ?? "Chờ xử lý",
                OrderDate = order.NgayDat
            })
            .Take(8)
            .ToListAsync();

        ViewBag.RevenueThisMonth = revenueThisMonth;
        ViewBag.RevenueGrowth = CalculateGrowth(
            revenueThisMonth,
            revenueLastMonth);

        ViewBag.OrderCountThisMonth = orderCountThisMonth;
        ViewBag.OrderGrowth = CalculateGrowth(
            orderCountThisMonth,
            orderCountLastMonth);

        ViewBag.NewCustomersThisMonth = newCustomersThisMonth;
        ViewBag.CustomerGrowth = CalculateGrowth(
            newCustomersThisMonth,
            newCustomersLastMonth);

        ViewBag.ReturnRate = "Chưa có dữ liệu";

        ViewBag.MonthlyRevenue = monthlyRevenue;
        ViewBag.CategorySales = categorySales;
        ViewBag.HasCategorySales = categorySales.Count > 0;
        ViewBag.MaxCategorySales = maxCategorySales;

        ViewBag.RecentOrders = recentOrdersRaw.Select(x => new
        {
            x.OrderCode,
            x.CustomerName,
            TotalText = x.Total.ToString("N0") + " đ",
            x.Status,
            OrderDateText = x.OrderDate?.ToString("dd/MM/yyyy") ?? "—"
        }).ToList();

        return View();
    }

    private static decimal CalculateGrowth(
        decimal currentValue,
        decimal previousValue)
    {
        if (previousValue <= 0)
            return currentValue > 0 ? 100 : 0;

        return Math.Round(
            (currentValue - previousValue) / previousValue * 100,
            1);
    }
}