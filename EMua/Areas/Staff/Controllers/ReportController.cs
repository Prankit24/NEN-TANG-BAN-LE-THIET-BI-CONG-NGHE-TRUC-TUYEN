using EMua.Areas.Staff.Models;
using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class ReportController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public ReportController(EMuaDbContext db) => _db = db;
    public async Task<IActionResult> Index(int? month, int? year)
    {
        var selectedMonth = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        var selectedYear = year is >= 2000 and <= 2100 ? year.Value : DateTime.Today.Year;
        var firstDayOfMonth = new DateTime(selectedYear, selectedMonth, 1);
        var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);
        var monthlyOrders = await _db.DonHangs.AsNoTracking()
            .Where(x => x.NgayDat >= firstDayOfMonth && x.NgayDat < firstDayOfNextMonth)
            .ToListAsync();

        var successfulStatuses = new[] { "Đã hoàn thành", "Đã giao" };
        var returnedStatuses = new[] { "Đã hủy", "Đã huỷ", "Đã hoàn" };
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

        var favoriteProducts = await _db.YeuThiches.AsNoTracking()
            .Where(x => x.NgayThem >= firstDayOfMonth && x.NgayThem < firstDayOfNextMonth)
            .GroupBy(x => x.MaSanPhamNavigation.TenSanPham)
            .OrderByDescending(group => group.Count())
            .Take(5)
            .Select(group => new ReportProductItem
            {
                Name = group.Key ?? "Chưa đặt tên",
                Quantity = group.Count()
            })
            .ToListAsync();

        var totalOrders = monthlyOrders.Count;
        var successfulOrders = monthlyOrders.Count(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty));
        var revenue = monthlyOrders
            .Where(x => successfulStatuses.Contains(x.TrangThaiDonHang ?? string.Empty))
            .Sum(x => x.TongTien ?? 0);
        var orderStatuses = monthlyOrders
            .GroupBy(x => string.IsNullOrWhiteSpace(x.TrangThaiDonHang) ? "Chưa xác định" : x.TrangThaiDonHang!)
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
            AverageOrderValue = successfulOrders == 0 ? 0 : revenue / successfulOrders,
            TotalOrders = totalOrders,
            SuccessfulOrders = successfulOrders,
            ReturnedOrders = monthlyOrders.Count(x => returnedStatuses.Contains(x.TrangThaiDonHang ?? string.Empty)),
            PendingOrders = monthlyOrders.Count(x => x.TrangThaiDonHang is "Chờ xử lý" or "Đang xử lý"),
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
        var result = await Index(month, year) as ViewResult;
        if (result?.Model is not ReportViewModel report) return BadRequest();

        var rows = new List<string>
        {
            "Bao cao;Gia tri",
            $"Doanh thu;{report.Revenue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}",
            $"Tong so don;{report.TotalOrders}",
            $"Don thanh cong;{report.SuccessfulOrders}",
            $"Ti le thanh cong;{report.SuccessRate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%",
            $"Don hoan huy;{report.ReturnedOrders}",
            "",
            "Mat hang ban chay;So luong;Doanh thu"
        };
        rows.AddRange(report.BestSellingProducts.Select(x => $"{EscapeCsv(x.Name)};{x.Quantity};{x.Revenue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"));
        rows.Add("\nSan pham yeu thich;Luot yeu thich");
        rows.AddRange(report.FavoriteProducts.Select(x => $"{EscapeCsv(x.Name)};{x.Quantity}"));
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, rows))).ToArray(), "text/csv", $"bao-cao-{year ?? DateTime.Today.Year}-{month ?? DateTime.Today.Month:00}.csv");
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
