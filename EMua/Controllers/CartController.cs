using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly EMuaDbContext _db;

    public CartController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index() => View(await BuildCartAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int variantId, int quantity = 1, int? productId = null, bool buyNow = false, string? returnUrl = null)
    {
        var variant = await _db.BienTheSanPhams.FindAsync(variantId);
        if (variant == null || variant.SoLuong <= 0 ||
            string.Equals(variant.TrangThai, "Ẩn", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        quantity = Math.Max(1, quantity);
        var userId = CurrentUserId();
        var item = await _db.ChiTietGioHangs.FirstOrDefaultAsync(x => x.MaNguoiDung == userId && x.MaBienThe == variantId);
        var nextQuantity = (item?.SoLuong ?? 0) + quantity;
        if (nextQuantity > variant.SoLuong)
        {
            TempData["Error"] = "Số lượng sản phẩm vượt quá tồn kho.";
            return RedirectToAction("Details", "Product", new { id = productId ?? variant.MaSanPham });
        }
        if (item == null)
            _db.ChiTietGioHangs.Add(new ChiTietGioHang { MaNguoiDung = userId, MaBienThe = variantId, SoLuong = quantity, NgayThem = DateTime.Now });
        else item.SoLuong = nextQuantity;
        await _db.SaveChangesAsync();
        if (buyNow)
            return RedirectToAction("Index", "Checkout");
        TempData["Success"] = $"Đã thêm {quantity} sản phẩm vào giỏ hàng.";
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Product");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, int quantity)
    {
        var item = await _db.ChiTietGioHangs.Include(x => x.MaBienTheNavigation)
            .FirstOrDefaultAsync(x => x.MaChiTietGioHang == id && x.MaNguoiDung == CurrentUserId());
        if (item == null) return NotFound();
        if (quantity <= 0) _db.ChiTietGioHangs.Remove(item);
        else if (item.MaBienTheNavigation.SoLuong <= 0 || quantity > item.MaBienTheNavigation.SoLuong)
            TempData["Error"] = "Số lượng vượt quá tồn kho.";
        else item.SoLuong = quantity;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var item = await _db.ChiTietGioHangs.FirstOrDefaultAsync(x => x.MaChiTietGioHang == id && x.MaNguoiDung == CurrentUserId());
        if (item == null) return NotFound();
        _db.ChiTietGioHangs.Remove(item);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<CartViewModel> BuildCartAsync()
    {
        var entities = await _db.ChiTietGioHangs.AsNoTracking()
            .Include(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Where(x => x.MaNguoiDung == CurrentUserId())
            .OrderBy(x => x.NgayThem)
            .ToListAsync();

        return new CartViewModel
        {
            Items = entities.Select(x => new CartItemViewModel
            {
                CartItemId = x.MaChiTietGioHang,
                ProductId = x.MaBienTheNavigation.MaSanPham,
                VariantId = x.MaBienThe,
                ProductName = x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
                VariantLabel = VariantLabel(x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan),
                ImageUrl = x.MaBienTheNavigation.HinhAnh,
                UnitPrice = x.MaBienTheNavigation.Gia,
                Quantity = x.SoLuong,
                Stock = x.MaBienTheNavigation.SoLuong
            }).ToList()
        };
    }

    private static string VariantLabel(string? color, string? version) =>
        string.Join(" / ", new[] { color, version }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}