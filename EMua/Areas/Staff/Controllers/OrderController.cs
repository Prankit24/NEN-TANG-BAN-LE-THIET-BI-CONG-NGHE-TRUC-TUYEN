using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class OrderController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    private static readonly string[] Statuses = ["Chờ xử lý", "Đang xử lý", "Đã giao", "Đã hoàn thành", "Đã hủy"];
    public OrderController(EMuaDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q, string? status)
    {
        var orders = _db.DonHangs.AsNoTracking()
            .Include(x => x.MaNguoiDungNavigation)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            orders = orders.Where(x => x.MaDonHang.ToString().Contains(search)
                || (x.HoTenNhanHang != null && x.HoTenNhanHang.Contains(search))
                || (x.SoDienThoaiNhanHang != null && x.SoDienThoaiNhanHang.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            orders = orders.Where(x => x.TrangThaiDonHang == status);
        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.Statuses = Statuses;
        return View(await orders.OrderByDescending(x => x.NgayDat).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (!Statuses.Contains(status)) return BadRequest();
        var order = await _db.DonHangs.FindAsync(id);
        if (order == null) return NotFound();
        order.TrangThaiDonHang = status;
        _db.LichSuVanChuyens.Add(new EMua.Models.Database.LichSuVanChuyen
        {
            MaDonHang = id,
            TrangThai = status,
            MoTa = $"Đơn hàng chuyển sang trạng thái: {status}.",
            ThoiGian = DateTime.Now
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật đơn hàng #DH-{id:D5}.";
        return RedirectToAction(nameof(Index), new { status = "all" });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.DonHangs.AsNoTracking()
            .Include(x => x.MaNguoiDungNavigation)
            .Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Include(x => x.ThanhToans)
            .FirstOrDefaultAsync(x => x.MaDonHang == id);
        return order == null ? NotFound() : View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        var invoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == id);
        if (invoice == null)
        {
            var order = await _db.DonHangs.FindAsync(id);
            if (order == null) return NotFound();
            invoice = new EMua.Models.Database.HoaDon { MaDonHang = id, NgayLapHoaDon = DateTime.Now, TongTien = order.TongTien, TrangThaiHoaDon = "Đã lập" };
            _db.HoaDons.Add(invoice);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(PrintInvoice), new { id = invoice.MaHoaDon });
    }

    [HttpGet]
    public async Task<IActionResult> PrintInvoice(int id)
    {
        var invoice = await _db.HoaDons.AsNoTracking()
            .Include(x => x.MaDonHangNavigation).ThenInclude(x => x.ChiTietDonHangs)
                .ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);
        return invoice == null ? NotFound() : View(invoice);
    }
}
