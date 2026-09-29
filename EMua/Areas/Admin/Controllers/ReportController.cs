using EMua.Data;
using EMua.Models.Database;
using EMua.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReportController : Controller
    {
        private readonly EMuaDbContext _context;

        private const decimal TaxRate = 0.05m;        // VAT ~5%
        private const decimal OperatingRate = 0.40m;  // Giá vốn & vận hành ~40%
        private const decimal MonthlyStaffSalary = 0m;

        public ReportController(EMuaDbContext context)
        {
            _context = context;
        }

        private bool IsSuccessOrder(string? status)
        {
            return status == "Đã hoàn thành" || status == "Đã giao" || status == "Hoàn thành";
        }

        public async Task<IActionResult> Index(string filterType = "month", int? month = null, int? quarter = null, int? year = null)
        {
            var now = DateTime.Now;
            filterType = filterType == "quarter" ? "quarter" : "month";

            int targetYear = Math.Clamp(year ?? now.Year, 2024, now.Year);
            int targetMonth = Math.Clamp(month ?? now.Month, 1, 12);
            int targetQuarter = Math.Clamp(quarter ?? ((now.Month - 1) / 3 + 1), 1, 4);

            DateTime start, end, prevStart, prevEnd;
            int monthsInPeriod;
            string label, prevLabel;

            if (filterType == "month")
            {
                start = new DateTime(targetYear, targetMonth, 1);
                end = start.AddMonths(1);
                prevStart = start.AddMonths(-1);
                prevEnd = start;
                monthsInPeriod = 1;
                label = $"Tháng {targetMonth}/{targetYear}";
                prevLabel = $"Tháng {prevStart.Month}/{prevStart.Year}";
            }
            else
            {
                start = new DateTime(targetYear, (targetQuarter - 1) * 3 + 1, 1);
                end = start.AddMonths(3);
                prevStart = start.AddMonths(-3);
                prevEnd = start;
                monthsInPeriod = 3;
                label = $"Quý {targetQuarter}/{targetYear}";
                int prevQ = (prevStart.Month - 1) / 3 + 1;
                prevLabel = $"Quý {prevQ}/{prevStart.Year}";
            }

            decimal monthlySalary = MonthlyStaffSalary;

            var current = await BuildStats(start, end, monthsInPeriod, monthlySalary, label);
            var previous = await BuildStats(prevStart, prevEnd, monthsInPeriod, monthlySalary, prevLabel);

            var yearStart = new DateTime(targetYear, 1, 1);
            var allYearOrders = await _context.DonHangs
                .Where(d => d.NgayDat >= yearStart && d.NgayDat < yearStart.AddYears(1))
                .Select(d => new { d.TrangThaiDonHang, d.NgayDat, d.TongTien })
                .ToListAsync();

            var yearOrders = allYearOrders.Where(d => IsSuccessOrder(d.TrangThaiDonHang)).ToList();

            var vm = new BaoCaoDashboardViewModel
            {
                FilterType = filterType,
                Month = targetMonth,
                Quarter = targetQuarter,
                Year = targetYear,
                Current = current,
                Previous = previous,
                TotalUsers = await _context.NguoiDungs.CountAsync(),
                BaoCaoLuuTru = await _context.BaoCaos
                    .OrderByDescending(b => b.NgayBaoCao)
                    .Take(10)
                    .ToListAsync()
            };

            for (int m = 1; m <= 12; m++)
            {
                decimal rev = yearOrders.Where(o => o.NgayDat!.Value.Month == m).Sum(o => o.TongTien ?? 0);
                decimal profit = rev - rev * TaxRate - rev * OperatingRate - (rev > 0 ? monthlySalary : 0);

                vm.TrendLabels.Add($"T{m}");
                vm.TrendRevenue.Add(rev);
                vm.TrendProfit.Add(profit);
            }

            return View(vm);
        }

        private async Task<PeriodStats> BuildStats(DateTime start, DateTime end, int months, decimal monthlySalary, string label)
        {
            var orders = await _context.DonHangs
                .Where(d => d.NgayDat >= start && d.NgayDat < end)
                .Select(d => new { d.TrangThaiDonHang, d.TongTien })
                .ToListAsync();

            var totals = orders
                .Where(d => IsSuccessOrder(d.TrangThaiDonHang))
                .Select(d => d.TongTien ?? 0)
                .ToList();

            decimal revenue = totals.Sum();

            return new PeriodStats
            {
                Label = label,
                Revenue = revenue,
                Orders = totals.Count,
                Tax = revenue * TaxRate,
                Operating = revenue * OperatingRate,
                Salary = monthlySalary * months
            };
        }
    }
}