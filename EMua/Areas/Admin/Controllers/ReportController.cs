using EMua.Data;
using EMua.Models.Database;
using EMua.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
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

        [HttpGet]
        public async Task<IActionResult> ExportExcel(string filterType = "month", int? month = null, int? quarter = null, int? year = null)
        {
            var result = await Index(filterType, month, quarter, year) as ViewResult;
            if (result?.Model is not BaoCaoDashboardViewModel vm) return BadRequest();

            var columns = new List<EMua.Services.ExcelColumn>
            {
                new() { Header = "Hạng mục đối chiếu / Thời gian", Width = 34, Type = EMua.Services.ExcelCellType.Text },
                new() { Header = "Kỳ hiện tại (" + vm.Current.Label + ")", Width = 25, Type = EMua.Services.ExcelCellType.Currency },
                new() { Header = "Kỳ trước (" + vm.Previous.Label + ")", Width = 25, Type = EMua.Services.ExcelCellType.Currency },
                new() { Header = "Chênh lệch (VNĐ)", Width = 22, Type = EMua.Services.ExcelCellType.Currency },
                new() { Header = "Tăng trưởng (%)", Width = 18, Type = EMua.Services.ExcelCellType.TextCenter }
            };

            var cur = vm.Current;
            var prev = vm.Previous;

            decimal revDiff = cur.Revenue - prev.Revenue;
            decimal revGrowth = BaoCaoDashboardViewModel.Growth(cur.Revenue, prev.Revenue);

            decimal orderDiff = cur.Orders - prev.Orders;
            decimal orderGrowth = BaoCaoDashboardViewModel.Growth(cur.Orders, prev.Orders);

            decimal profitDiff = cur.NetProfit - prev.NetProfit;
            decimal profitGrowth = BaoCaoDashboardViewModel.Growth(cur.NetProfit, prev.NetProfit);

            var rows = new List<object?[]>
            {
                new object?[] { "DOANH THU BÁN HÀNG", cur.Revenue, prev.Revenue, revDiff, $"{(revGrowth >= 0 ? "+" : "")}{revGrowth:0.#}%" },
                new object?[] { "Số lượng đơn hàng hoàn thành", (decimal)cur.Orders, (decimal)prev.Orders, orderDiff, $"{(orderGrowth >= 0 ? "+" : "")}{orderGrowth:0.#}%" },
                new object?[] { "Thuế GTGT ước tính (~5%)", cur.Tax, prev.Tax, cur.Tax - prev.Tax, null },
                new object?[] { "Giá vốn & Vận hành (~40%)", cur.Operating, prev.Operating, cur.Operating - prev.Operating, null },
                new object?[] { "Chi phí lương nhân sự", cur.Salary, prev.Salary, cur.Salary - prev.Salary, null },
                new object?[] { "LỢI NHUẬN RÒNG ƯỚC TÍNH", cur.NetProfit, prev.NetProfit, profitDiff, $"{(profitGrowth >= 0 ? "+" : "")}{profitGrowth:0.#}%" },
                new object?[] { "Biên lợi nhuận ròng", null, null, null, $"{cur.Margin:0.#}%" },
                new object?[] { "--- DIỄN BIẾN DOANH THU 12 THÁNG NĂM " + vm.Year + " ---", null, null, null, null }
            };

            for (int m = 0; m < vm.TrendLabels.Count; m++)
            {
                rows.Add(new object?[]
                {
                    $"Tháng {m + 1}/{vm.Year}",
                    vm.TrendRevenue[m],
                    null,
                    vm.TrendProfit[m],
                    "Lợi nhuận T" + (m + 1)
                });
            }

            var summary = new object?[]
            {
                "TỔNG KẾT KỲ BÁO CÁO",
                cur.NetProfit,
                prev.NetProfit,
                profitDiff,
                $"{cur.Margin:0.#}% Biên LN"
            };

            var fileBytes = EMua.Services.ExcelExportService.CreateWorkbook(
                $"BÁO CÁO TÀI CHÍNH & HIỆU QUẢ KINH DOANH - EMUA ADMIN",
                $"Kỳ báo cáo: {cur.Label} (So với: {prev.Label}) | Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm} | Admin: {User.Identity?.Name ?? "Admin"}",
                columns,
                rows,
                summary);

            var fileName = $"BaoCao_TaiChinh_{vm.Year}_{(filterType == "quarter" ? "Q" + vm.Quarter : "T" + vm.Month)}.xlsx";
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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