using EMua.Areas.Admin.Models.ViewModels;
using EMua.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EMua.Areas.Admin.Controllers
{
    // DTO and ViewModel definitions
    public class LowStockItem
    {
        public string ProductName { get; set; } = string.Empty;
        public int SoLuong { get; set; }
    }

    public class StaffAccountItem
    {
        public string TenNguoiDung { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Created { get; set; } = string.Empty;
    }

    public class RecentOrderItem
    {
        public string OrderCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string TotalText { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string OrderDateText { get; set; } = string.Empty;
    }

    public class DashboardViewModel
    {
        public decimal RevenueThisMonth { get; set; }
        public decimal RevenueGrowth { get; set; }
        public int OrderCountThisMonth { get; set; }
        public decimal OrderGrowth { get; set; }
        public int NewCustomersThisMonth { get; set; }
        public decimal CustomerGrowth { get; set; }
        public List<decimal> MonthlyRevenue { get; set; } = new();
        public List<dynamic> CategorySales { get; set; } = new();
        public decimal MaxCategorySales { get; set; }
        public bool HasCategorySales => CategorySales?.Count > 0;
        public List<LowStockItem> LowStock { get; set; } = new();
        public int StaffTotal { get; set; }
        public int StaffActive { get; set; }
        public int StaffLocked { get; set; }
        public List<StaffAccountItem> RecentAccounts { get; set; } = new();
        public int PendingOrders { get; set; }
        public List<RecentOrderItem> RecentOrders { get; set; } = new();
        public int? SelectedMonth { get; set; }
        public int? SelectedYear { get; set; }
    }

    [Area("Admin")]
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly EMuaDbContext _db;

        public DashboardController(EMuaDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(int? month, int? year)
        {
            // Authorization check
            var isAuthorized = User.IsInRole("Quản trị viên") || User.IsInRole("Admin") || User.IsInRole("QuanTriVien");
            if (!isAuthorized)
            {
                return RedirectToAction("Index", "Home", new { area = "" });
            }

            // Determine filter period – default to current month
            var now = DateTime.Now;
            DateTime filterStart;
            if (month.HasValue && year.HasValue)
            {
                filterStart = new DateTime(year.Value, month.Value, 1);
            }
            else
            {
                filterStart = new DateTime(now.Year, now.Month, 1);
            }
            DateTime filterEnd = filterStart.AddMonths(1);
            DateTime prevStart = filterStart.AddMonths(-1);
            DateTime prevEnd = filterStart;

            // Base order query (exclude cancelled)
            var validOrders = _db.DonHangs.Where(o =>
                o.NgayDat != null && (o.TrangThaiDonHang == null || (o.TrangThaiDonHang != "Đã hủy" && o.TrangThaiDonHang != "Đã huỷ")));

            var thisMonthOrders = validOrders.Where(o => o.NgayDat >= filterStart && o.NgayDat < filterEnd);
            var lastMonthOrders = validOrders.Where(o => o.NgayDat >= prevStart && o.NgayDat < prevEnd);

            // KPI calculations
            var revenueThisMonth = await thisMonthOrders.SumAsync(o => o.TongTien ?? 0);
            var revenueLastMonth = await lastMonthOrders.SumAsync(o => o.TongTien ?? 0);
            var orderCountThisMonth = await thisMonthOrders.CountAsync();
            var orderCountLastMonth = await lastMonthOrders.CountAsync();

            var newCustomersThisMonth = await _db.NguoiDungs.CountAsync(u =>
                u.MaQuyen == 3 && u.NgayTao >= filterStart && u.NgayTao < filterEnd);
            var newCustomersLastMonth = await _db.NguoiDungs.CountAsync(u =>
                u.MaQuyen == 3 && u.NgayTao >= prevStart && u.NgayTao < prevEnd);

            // 12‑month revenue for chart
            var rawMonthlyRevenue = await validOrders
                .Where(o => o.NgayDat >= new DateTime(now.Year, 1, 1) && o.NgayDat < new DateTime(now.Year + 1, 1, 1))
                .GroupBy(o => o.NgayDat!.Value.Month)
                .Select(g => new { Month = g.Key, Revenue = g.Sum(o => o.TongTien ?? 0) })
                .ToListAsync();
            var monthlyRevenue = Enumerable.Range(1, 12)
                .Select(m => rawMonthlyRevenue.FirstOrDefault(x => x.Month == m)?.Revenue ?? 0)
                .ToList();

            // Category sales (top 6)
            var categorySales = await (from detail in _db.ChiTietDonHangs
                                      join order in validOrders on detail.MaDonHang equals order.MaDonHang
                                      join variant in _db.BienTheSanPhams on detail.MaBienThe equals variant.MaBienThe
                                      join product in _db.SanPhams on variant.MaSanPham equals product.MaSanPham
                                      join cat in _db.DanhMucSanPhams on product.MaDanhMuc equals cat.MaDanhMuc
                                      group detail by cat.TenDanhMuc into g
                                      select new { Name = g.Key ?? "Chưa phân loại", Total = g.Sum(d => d.ThanhTien) })
                .OrderByDescending(x => x.Total)
                .Take(6)
                .ToListAsync();
            var maxCategorySales = categorySales.Count == 0 ? 1m : categorySales.Max(x => x.Total);

            // Recent orders (8)
            var recentOrdersRaw = await (from order in _db.DonHangs
                                        join user in _db.NguoiDungs on order.MaNguoiDung equals user.MaNguoiDung into users
                                        from user in users.DefaultIfEmpty()
                                        orderby order.NgayDat descending
                                        select new
                                        {
                                            OrderCode = $"#DH-{order.MaDonHang:D5}",
                                            CustomerName = order.HoTenNhanHang ?? user.TenNguoiDung ?? "Khách hàng",
                                            Total = order.TongTien ?? 0,
                                            Status = order.TrangThaiDonHang ?? "Chờ xử lý",
                                            OrderDate = order.NgayDat
                                        })
                .Take(8)
                .ToListAsync();

            // Low stock (≤5)
            var lowStock = await _db.BienTheSanPhams
                .Where(b => b.SoLuong <= 5)
                .Select(b => new LowStockItem
                {
                    ProductName = b.MaSanPhamNavigation.TenSanPham ?? "(Không tên)",
                    SoLuong = b.SoLuong
                })
                .Take(5)
                .ToListAsync();

            // Staff stats (MaQuyen != 3)
            var staffTotal = await _db.NguoiDungs.CountAsync(u => u.MaQuyen != 3);
            var staffActive = await _db.NguoiDungs.CountAsync(u => u.MaQuyen != 3 && u.TrangThai);
            var staffLocked = await _db.NguoiDungs.CountAsync(u => u.MaQuyen != 3 && !u.TrangThai);

            // Recent staff accounts (latest 5, non‑customer)
            var recentAccounts = await (from u in _db.NguoiDungs
                                        where u.MaQuyen != 3
                                        orderby u.NgayTao descending
                                        select new { u.TenNguoiDung, u.Email, u.NgayTao })
                .Take(5)
                .ToListAsync();

            var recentAccountsMapped = recentAccounts.Select(u => new StaffAccountItem
            {
                TenNguoiDung = u.TenNguoiDung ?? "",
                Email = u.Email ?? "",
                Created = u.NgayTao.HasValue ? u.NgayTao.Value.ToString("dd/MM/yyyy") : "—"
            }).ToList();

            // Pending orders within filter period
            var pendingOrdersCount = await _db.DonHangs
                .Where(o => o.NgayDat >= filterStart && o.NgayDat < filterEnd &&
                    (o.TrangThaiDonHang == null || o.TrangThaiDonHang == "Chờ xử lý" || o.TrangThaiDonHang == "Chờ xác nhận"))
                .CountAsync();

            // Build ViewModel
            var model = new DashboardViewModel
            {
                RevenueThisMonth = revenueThisMonth,
                RevenueGrowth = CalculateGrowth(revenueThisMonth, revenueLastMonth),
                OrderCountThisMonth = orderCountThisMonth,
                OrderGrowth = CalculateGrowth(orderCountThisMonth, orderCountLastMonth),
                NewCustomersThisMonth = newCustomersThisMonth,
                CustomerGrowth = CalculateGrowth(newCustomersThisMonth, newCustomersLastMonth),
                MonthlyRevenue = monthlyRevenue,
                CategorySales = categorySales.Cast<dynamic>().ToList(),
                MaxCategorySales = maxCategorySales,
                LowStock = lowStock,
                StaffTotal = staffTotal,
                StaffActive = staffActive,
                StaffLocked = staffLocked,
                RecentAccounts = recentAccountsMapped,
                PendingOrders = pendingOrdersCount,
                RecentOrders = recentOrdersRaw.Select(o => new RecentOrderItem
                {
                    OrderCode = o.OrderCode,
                    CustomerName = o.CustomerName,
                    TotalText = o.Total.ToString("N0") + " đ",
                    Status = o.Status,
                    OrderDateText = o.OrderDate?.ToString("dd/MM/yyyy") ?? "—"
                }).ToList(),
                SelectedMonth = month ?? now.Month,
                SelectedYear = year ?? now.Year
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult TaskProgress()
        {
            var model = new TaskProgressViewModel
            {
                Tasks = new List<AdminTaskItem>
                {
                    new AdminTaskItem { Id = 1, Title = "Quản lý người dùng", CompletedCount = 0, TotalCount = 5 },
                    new AdminTaskItem { Id = 2, Title = "Quản lý phân quyền", CompletedCount = 0, TotalCount = 6 },
                    new AdminTaskItem { Id = 3, Title = "QL từ cấm", CompletedCount = 0, TotalCount = 4 },
                    new AdminTaskItem { Id = 4, Title = "Quản lý báo cáo thống kê", CompletedCount = 0, TotalCount = 5 }
                }
            };
            return View(model);
        }

        private static decimal CalculateGrowth(decimal currentValue, decimal previousValue)
        {
            if (previousValue <= 0)
                return currentValue > 0 ? 100 : 0;
            return Math.Round((currentValue - previousValue) / previousValue * 100, 1);
        }
    }
}
