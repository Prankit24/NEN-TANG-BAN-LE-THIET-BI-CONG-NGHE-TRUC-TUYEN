using EMua.Models.Database;

namespace EMua.Models.ViewModels
{
    public class PeriodStats
    {
        public string Label { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int Orders { get; set; }
        public decimal Tax { get; set; }
        public decimal Operating { get; set; }
        public decimal Salary { get; set; }

        public decimal TotalExpense => Tax + Operating + Salary;
        public decimal TotalCost => TotalExpense;
        public decimal NetProfit => Revenue - TotalExpense;
        public decimal AvgOrderValue => Orders > 0 ? Revenue / Orders : 0;
        public decimal Margin => Revenue > 0 ? (NetProfit / Revenue) * 100 : 0;
    }

    public class BaoCaoDashboardViewModel
    {
        public string FilterType { get; set; } = "month";
        public int Month { get; set; }
        public int Quarter { get; set; }
        public int Year { get; set; }

        public PeriodStats Current { get; set; } = new();
        public PeriodStats Previous { get; set; } = new();

        public int TotalUsers { get; set; }
        public List<BaoCao> BaoCaoLuuTru { get; set; } = new();

        public List<string> TrendLabels { get; set; } = new();
        public List<decimal> TrendRevenue { get; set; } = new();
        public List<decimal> TrendProfit { get; set; } = new();

        public static decimal Growth(decimal current, decimal previous)
        {
            if (previous == 0)
            {
                return current > 0 ? 100m : 0m;
            }
            return Math.Round(((current - previous) / Math.Abs(previous)) * 100m, 1);
        }
    }
}