using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class InvoiceController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public InvoiceController(EMuaDbContext db) => _db = db;

    // 1. DANH SÁCH HÓA ĐƠN
    public async Task<IActionResult> Index(string? q, string? status, int page = 1, int pageSize = 10)
    {
        var invoicesQuery = _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .AsQueryable();

        // Tìm kiếm theo mã hóa đơn hoặc mã đơn hàng
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            invoicesQuery = invoicesQuery.Where(x => x.MaHoaDon.ToString().Contains(search) || x.MaDonHang.ToString().Contains(search));
        }

        // Lấy danh sách về bộ nhớ để xử lý đồng bộ trạng thái linh hoạt
        var allInvoices = await invoicesQuery
            .OrderByDescending(x => x.MaHoaDon)
            .ThenByDescending(x => x.MaDonHang)
            .ToListAsync();

        // Tự động đồng bộ trạng thái hiển thị: Nếu đơn hàng đã hoàn thành/giao thì hóa đơn hiện Đã thanh toán
        foreach (var inv in allInvoices)
        {
            var orderStatus = inv.MaDonHangNavigation?.TrangThaiDonHang;
            if (orderStatus == "Đã hoàn thành" || orderStatus == "Đã giao")
            {
                inv.TrangThaiHoaDon = "Đã thanh toán";
            }
        }

        // Lọc theo trạng thái (sau khi đã đồng bộ)
        if (!string.IsNullOrWhiteSpace(status))
        {
            allInvoices = allInvoices.Where(x => x.TrangThaiHoaDon == status).ToList();
        }

        // Phân trang thủ công trên danh sách đã xử lý
        int totalItems = allInvoices.Count;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = allInvoices
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.Search = q;
        ViewBag.SelectedStatus = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.PageSize = pageSize;

        // Lấy danh sách các trạng thái độc lập để hiển thị dropdown lọc
        ViewBag.StatusList = allInvoices
            .Where(x => x.TrangThaiHoaDon != null)
            .Select(x => x.TrangThaiHoaDon!)
            .Distinct()
            .ToList();

        return View(items);
    }

    // 2. XEM CHI TIẾT HÓA ĐƠN
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
                .ThenInclude(d => d.ChiTietDonHangs)
                    .ThenInclude(ct => ct.MaBienTheNavigation)
                        .ThenInclude(bt => bt.MaSanPhamNavigation)
            .Include(x => x.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);

        if (invoice == null)
        {
            TempData["Error"] = "Không tìm thấy hóa đơn.";
            return RedirectToAction(nameof(Index));
        }

        var orderStatus = invoice.MaDonHangNavigation?.TrangThaiDonHang;
        if (orderStatus == "Đã hoàn thành" || orderStatus == "Đã giao")
        {
            invoice.TrangThaiHoaDon = "Đã thanh toán";
        }

        return View(invoice);
    }
}