using System.Security.Claims;
using EMua.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.ViewComponents;

public class CartSummaryViewComponent : ViewComponent
{
    private readonly EMuaDbContext _db;

    public CartSummaryViewComponent(EMuaDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
            return View(0);

        var userIdText = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
            return View(0);

        var quantity = await _db.ChiTietGioHangs.AsNoTracking()
            .Where(x => x.MaNguoiDung == userId)
            .SumAsync(x => (int?)x.SoLuong) ?? 0;

        return View(quantity);
    }
}