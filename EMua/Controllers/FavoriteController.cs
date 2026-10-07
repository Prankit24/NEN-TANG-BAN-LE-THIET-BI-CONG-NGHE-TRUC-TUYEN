using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class FavoriteController : Controller
{
    private readonly EMuaDbContext _db;

    public FavoriteController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!TryGetUserId(out var userId))
            return RedirectToAction("Login", "Auth", new { returnUrl = Url.Action(nameof(Index)) });

        var favorites = await _db.YeuThiches
            .AsNoTracking()
            .Where(x => x.MaNguoiDung == userId &&
                        (x.MaSanPhamNavigation.TrangThaiSanPham == null || x.MaSanPhamNavigation.TrangThaiSanPham != "Ẩn"))
            .Include(x => x.MaSanPhamNavigation)
                .ThenInclude(x => x.BienTheSanPhams)
            .Include(x => x.MaSanPhamNavigation)
                .ThenInclude(x => x.SanPhamHinhAnhs)
            .Include(x => x.MaSanPhamNavigation)
                .ThenInclude(x => x.MaThuongHieuNavigation)
            .OrderByDescending(x => x.NgayThem)
            .ToListAsync();

        return View(favorites);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        if (!TryGetUserId(out var userId))
            return RedirectToAction("Login", "Auth", new { returnUrl = Url.Action(nameof(Index)) });

        var favorite = await _db.YeuThiches
            .FirstOrDefaultAsync(x => x.MaYeuThich == id && x.MaNguoiDung == userId);

        if (favorite != null)
        {
            _db.YeuThiches.Remove(favorite);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userIdText, out userId);
    }
}