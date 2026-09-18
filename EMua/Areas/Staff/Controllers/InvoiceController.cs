using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class InvoiceController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public InvoiceController(EMuaDbContext db) => _db = db;
    public async Task<IActionResult> Index(string? q)
    {
        var invoices = _db.HoaDons.AsNoTracking().Include(x => x.MaDonHangNavigation).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            invoices = invoices.Where(x => x.MaHoaDon.ToString().Contains(search) || x.MaDonHang.ToString().Contains(search));
        }
        ViewBag.Search = q;
        return View(await invoices.OrderByDescending(x => x.NgayLapHoaDon).ToListAsync());
    }
}
