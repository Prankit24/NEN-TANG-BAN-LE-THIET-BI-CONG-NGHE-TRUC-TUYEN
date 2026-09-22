using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly EMuaDbContext _db;

    public CartController(EMuaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();

        if (userId == null)
            return RedirectToAction("Login", "Auth");

        var items = await _db.ChiTietGioHangs
            .Include(x => x.MaBienTheNavigation)
                .ThenInclude(x => x.MaSanPhamNavigation)
            .Where(x => x.MaNguoiDung == userId)
            .ToListAsync();

        return View(items);
    }

    // Lấy số lượng sản phẩm để cập nhật badge giỏ hàng.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Summary()
    {
        var userId = GetUserId();

        if (userId == null)
            return Json(new
            {
                success = true,
                count = 0,
                subtotal = 0m
            });

        var items = await _db.ChiTietGioHangs
            .Include(x => x.MaBienTheNavigation)
            .Where(x => x.MaNguoiDung == userId)
            .ToListAsync();

        return Json(new
        {
            success = true,
            count = items.Sum(x => x.SoLuong),
            subtotal = items.Sum(x =>
                x.SoLuong * x.MaBienTheNavigation.Gia)
        });
    }

    // Thêm sản phẩm vào giỏ hàng bằng AJAX.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        [FromBody] CartItemRequest request)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập để thêm sản phẩm vào giỏ hàng."
            });
        }

        if (request.VariantId <= 0 || request.Quantity <= 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu sản phẩm không hợp lệ."
            });
        }

        var variant = await _db.BienTheSanPhams
            .FindAsync(request.VariantId);

        if (variant == null || variant.TrangThai != "Còn hàng")
        {
            return NotFound(new
            {
                success = false,
                message = "Sản phẩm hiện không còn bán."
            });
        }

        var cartItem = await _db.ChiTietGioHangs
            .FirstOrDefaultAsync(x =>
                x.MaNguoiDung == userId &&
                x.MaBienThe == request.VariantId);

        var newQuantity = (cartItem?.SoLuong ?? 0)
                          + request.Quantity;

        if (newQuantity > variant.SoLuong)
        {
            return BadRequest(new
            {
                success = false,
                message = "Số lượng yêu cầu vượt quá tồn kho."
            });
        }

        if (cartItem == null)
        {
            _db.ChiTietGioHangs.Add(new ChiTietGioHang
            {
                MaNguoiDung = userId.Value,
                MaBienThe = request.VariantId,
                SoLuong = request.Quantity,
                NgayThem = DateTime.Now
            });
        }
        else
        {
            cartItem.SoLuong = newQuantity;
        }

        await _db.SaveChangesAsync();

        return await Summary();
    }

    // Tăng hoặc giảm số lượng sản phẩm trong giỏ hàng.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(
        [FromBody] CartItemRequest request)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Phiên đăng nhập đã hết hạn."
            });
        }

        var item = await _db.ChiTietGioHangs
            .Include(x => x.MaBienTheNavigation)
            .FirstOrDefaultAsync(x =>
                x.MaChiTietGioHang == request.CartItemId &&
                x.MaNguoiDung == userId);

        if (item == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy sản phẩm trong giỏ hàng."
            });
        }

        // Nếu số lượng bằng 0 thì xóa khỏi giỏ.
        if (request.Quantity <= 0)
        {
            _db.ChiTietGioHangs.Remove(item);
        }
        else
        {
            if (request.Quantity > item.MaBienTheNavigation.SoLuong)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Số lượng yêu cầu vượt quá tồn kho."
                });
            }

            item.SoLuong = request.Quantity;
        }

        await _db.SaveChangesAsync();

        return await Summary();
    }

    // Xóa một sản phẩm khỏi giỏ hàng.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        [FromBody] CartItemRequest request)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Phiên đăng nhập đã hết hạn."
            });
        }

        var item = await _db.ChiTietGioHangs
            .FirstOrDefaultAsync(x =>
                x.MaChiTietGioHang == request.CartItemId &&
                x.MaNguoiDung == userId);

        if (item == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Sản phẩm không còn trong giỏ hàng."
            });
        }

        _db.ChiTietGioHangs.Remove(item);

        await _db.SaveChangesAsync();

        return await Summary();
    }


    // Gộp giỏ hàng tạm của khách (localStorage) vào giỏ hàng tài khoản sau khi đăng nhập.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeGuest(
        [FromBody] List<GuestCartItemRequest> items)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập."
            });
        }

        if (items == null || items.Count == 0)
            return await Summary();

        // Gom các variant bị lặp trong localStorage.
        var normalizedItems = items
            .Where(x => x.VariantId > 0 && x.Quantity > 0)
            .GroupBy(x => x.VariantId)
            .Select(g => new GuestCartItemRequest
            {
                VariantId = g.Key,
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        foreach (var guestItem in normalizedItems)
        {
            var variant = await _db.BienTheSanPhams
                .FirstOrDefaultAsync(x => x.MaBienThe == guestItem.VariantId);

            if (variant == null || variant.TrangThai != "Còn hàng" || variant.SoLuong <= 0)
                continue;

            var dbItem = await _db.ChiTietGioHangs
                .FirstOrDefaultAsync(x =>
                    x.MaNguoiDung == userId &&
                    x.MaBienThe == guestItem.VariantId);

            var currentQuantity = dbItem?.SoLuong ?? 0;
            var desiredQuantity = currentQuantity + guestItem.Quantity;
            var safeQuantity = Math.Min(desiredQuantity, variant.SoLuong);

            if (safeQuantity <= 0)
                continue;

            if (dbItem == null)
            {
                _db.ChiTietGioHangs.Add(new ChiTietGioHang
                {
                    MaNguoiDung = userId.Value,
                    MaBienThe = guestItem.VariantId,
                    SoLuong = safeQuantity,
                    NgayThem = DateTime.Now
                });
            }
            else
            {
                dbItem.SoLuong = safeQuantity;
            }
        }

        await _db.SaveChangesAsync();
        return await Summary();
    }

    private int? GetUserId()
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return int.TryParse(userIdText, out var userId)
            ? userId
            : null;
    }
}

public class CartItemRequest
{
    public int CartItemId { get; set; }

    public int VariantId { get; set; }

    public int Quantity { get; set; } = 1;
}
public class GuestCartItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; } = 1;
}
