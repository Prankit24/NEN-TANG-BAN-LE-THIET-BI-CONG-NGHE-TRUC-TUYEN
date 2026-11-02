using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EMua.Data;
using EMua.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

public class FavoriteController : Controller
{
    private readonly EMuaDbContext _db;

    public FavoriteController(EMuaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.Identity?.IsAuthenticated != true ||
            !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var returnUrl = Url.Action(nameof(Index), "Favorite");
            return RedirectToAction("Login", "Auth", new { returnUrl });
        }

        var favorites = await _db.YeuThiches
            .AsNoTracking()
            .Include(f => f.MaSanPhamNavigation)
                .ThenInclude(p => p.MaDanhMucNavigation)
            .Include(f => f.MaSanPhamNavigation)
                .ThenInclude(p => p.MaThuongHieuNavigation)
            .Include(f => f.MaSanPhamNavigation)
                .ThenInclude(p => p.BienTheSanPhams)
            .Include(f => f.MaSanPhamNavigation)
                .ThenInclude(p => p.SanPhamHinhAnhs)
            .Where(f => f.MaNguoiDung == userId &&
                        (f.MaSanPhamNavigation.TrangThaiSanPham == null || f.MaSanPhamNavigation.TrangThaiSanPham != "Ẩn"))
            .OrderByDescending(f => f.NgayThem)
            .ToListAsync();

        var model = favorites.Select(f =>
        {
            var p = f.MaSanPhamNavigation;
            var availableVariant = p.BienTheSanPhams?
                .Where(v => v.SoLuong > 0 && v.TrangThai == "Còn hàng")
                .OrderBy(v => v.Gia)
                .FirstOrDefault();

            var anyVariant = availableVariant ?? p.BienTheSanPhams?.OrderBy(v => v.Gia).FirstOrDefault();

            var rawImg = p.SanPhamHinhAnhs?
                .OrderByDescending(h => h.LaAnhChinh)
                .ThenBy(h => h.ThuTu)
                .FirstOrDefault()?.DuongDanAnh
                ?? anyVariant?.HinhAnh;

            var price = anyVariant?.Gia ?? p.GiaGoc ?? 0;

            return new FavoriteItemViewModel
            {
                FavoriteId = f.MaYeuThich,
                ProductId = p.MaSanPham,
                ProductName = p.TenSanPham ?? "Sản phẩm",
                CategoryName = p.MaDanhMucNavigation?.TenDanhMuc,
                BrandName = p.MaThuongHieuNavigation?.TenThuongHieu,
                ImageUrl = rawImg,
                Price = price,
                OriginalPrice = p.GiaGoc,
                InStock = availableVariant != null && availableVariant.SoLuong > 0,
                AddedAt = f.NgayThem
            };
        }).ToList();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId, string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated != true ||
            !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = returnUrl ?? Url.Action(nameof(Index), "Favorite") });
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM public.yeuthich
            WHERE manguoidung = {userId} AND masanpham = {productId}");

        TempData["Success"] = "Đã xóa sản phẩm khỏi danh sách yêu thích.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }
}
