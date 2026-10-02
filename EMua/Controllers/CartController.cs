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
    private const string CouponSessionKey = "Cart_CouponCode";

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

        var viewModel = new CartViewModel
        {
            Items = items.Select(x =>
            {
                var variant = x.MaBienTheNavigation;
                var product = variant?.MaSanPhamNavigation;

                var variantLabel = string.Join(" - ",
                    new[] { variant?.MauSac, variant?.PhienBan }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                );

                return new CartItemViewModel
                {
                    CartItemId = x.MaChiTietGioHang,
                    ProductId = variant?.MaSanPham ?? 0,
                    VariantId = x.MaBienThe,
                    ProductName = product?.TenSanPham ?? "Sản phẩm",
                    VariantLabel = variantLabel,
                    ImageUrl = variant?.HinhAnh,
                    UnitPrice = variant?.Gia ?? 0m,
                    Quantity = x.SoLuong,
                    Stock = variant?.SoLuong ?? 0
                };
            }).ToList()
        };

        await ApplyCouponFromSessionAsync(viewModel);
        viewModel.AvailableCoupons = await GetAvailableCouponsAsync(viewModel.Subtotal);

        return View(viewModel);
    }

    // Áp dụng mã khuyến mãi cho giỏ hàng, lưu lại qua Session để giữ khi đổi số lượng/tải lại trang.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string code)
    {
        var userId = GetUserId();
        if (userId == null) return RedirectToAction("Login", "Auth");

        var subtotal = await _db.ChiTietGioHangs
            .Include(x => x.MaBienTheNavigation)
            .Where(x => x.MaNguoiDung == userId)
            .SumAsync(x => x.SoLuong * (x.MaBienTheNavigation!.Gia));

        var coupon = await FindValidCouponAsync(code, subtotal);

        if (coupon == null)
        {
            TempData["Error"] = "Mã khuyến mãi không hợp lệ, đã hết hạn, hết lượt dùng hoặc đơn hàng chưa đạt giá trị tối thiểu.";
        }
        else
        {
            HttpContext.Session.SetString(CouponSessionKey, coupon.MaCode);
            TempData["Success"] = $"Đã áp dụng mã \"{coupon.MaCode}\" thành công!";
        }

        return RedirectToAction(nameof(Index));
    }

    // Gỡ mã khuyến mãi đang áp dụng khỏi giỏ hàng.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveCoupon()
    {
        HttpContext.Session.Remove(CouponSessionKey);
        TempData["Success"] = "Đã gỡ mã khuyến mãi khỏi giỏ hàng.";
        return RedirectToAction(nameof(Index));
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
    public async Task<IActionResult> Add([FromBody] CartItemRequest request)
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

        if (request == null || request.VariantId <= 0 || request.Quantity <= 0)
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

        var newQuantity = (cartItem?.SoLuong ?? 0) + request.Quantity;

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

    // Xử lý Cập nhật số lượng từ Form trong Index.cshtml
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, int quantity)
    {
        var userId = GetUserId();
        if (userId == null) return RedirectToAction("Login", "Auth");

        var item = await _db.ChiTietGioHangs
            .Include(x => x.MaBienTheNavigation)
            .FirstOrDefaultAsync(x => x.MaChiTietGioHang == id && x.MaNguoiDung == userId);

        if (item != null)
        {
            if (quantity <= 0)
            {
                _db.ChiTietGioHangs.Remove(item);
            }
            else if (quantity <= item.MaBienTheNavigation.SoLuong)
            {
                item.SoLuong = quantity;
            }
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    // Xử lý Xóa sản phẩm từ Form trong Index.cshtml
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var userId = GetUserId();
        if (userId == null) return RedirectToAction("Login", "Auth");

        var item = await _db.ChiTietGioHangs
            .FirstOrDefaultAsync(x => x.MaChiTietGioHang == id && x.MaNguoiDung == userId);

        if (item != null)
        {
            _db.ChiTietGioHangs.Remove(item);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    // Gộp giỏ hàng tạm của khách (localStorage) vào giỏ hàng tài khoản sau khi đăng nhập.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeGuest([FromBody] List<GuestCartItemRequest> items)
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

    // Đọc mã khuyến mãi đã lưu trong Session (nếu có), kiểm tra lại còn hợp lệ với giỏ hàng hiện tại không.
    // Nếu không còn hợp lệ (hết hạn, hết lượt, giỏ hàng không còn đạt tối thiểu...) thì tự gỡ và báo cho khách.
    private async Task ApplyCouponFromSessionAsync(CartViewModel model)
    {
        var code = HttpContext.Session.GetString(CouponSessionKey);
        if (string.IsNullOrWhiteSpace(code)) return;

        var coupon = await FindValidCouponAsync(code, model.Subtotal);

        if (coupon == null)
        {
            HttpContext.Session.Remove(CouponSessionKey);
            TempData["Error"] = $"Mã \"{code}\" không còn áp dụng được cho giỏ hàng hiện tại nên đã được tự động gỡ bỏ.";
            return;
        }

        model.CouponCode = coupon.MaCode;
        model.Discount = CalculateDiscount(coupon, model.Subtotal);
    }

    private async Task<KhuyenMai?> FindValidCouponAsync(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var now = DateTime.Now;
        var coupon = await _db.KhuyenMais
            .FirstOrDefaultAsync(x => x.MaCode.ToLower() == code.Trim().ToLower());

        if (coupon == null
            || !coupon.TrangThai
            || coupon.SoLuong is <= 0
            || coupon.NgayBatDau > now
            || coupon.NgayKetThuc < now
            || (coupon.GiaTriDonHangToiThieu ?? 0) > subtotal)
        {
            return null;
        }

        return coupon;
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        coupon.LoaiGiamGia.Contains("%") || coupon.LoaiGiamGia.Contains("trăm", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(subtotal, subtotal * coupon.GiaTriGiam / 100m)
            : Math.Min(subtotal, coupon.GiaTriGiam);

    // Lấy các mã khuyến mãi đang còn hiệu lực để gợi ý cho khách ngay trên trang giỏ hàng.
    private async Task<List<CouponOptionViewModel>> GetAvailableCouponsAsync(decimal subtotal)
    {
        var now = DateTime.Now;

        return await _db.KhuyenMais.AsNoTracking()
            .Where(x => x.TrangThai
                && x.SoLuong > 0
                && (x.NgayBatDau == null || x.NgayBatDau <= now)
                && (x.NgayKetThuc == null || x.NgayKetThuc >= now))
            .OrderBy(x => x.GiaTriDonHangToiThieu ?? 0)
            .ThenByDescending(x => x.GiaTriGiam)
            .Take(4)
            .Select(x => new CouponOptionViewModel
            {
                Code = x.MaCode,
                Name = x.TenKhuyenMai,
                DiscountText = x.LoaiGiamGia.Contains("%")
                    ? $"Giảm {x.GiaTriGiam:N0}%"
                    : $"Giảm {x.GiaTriGiam:N0}đ",
                MinimumOrder = x.GiaTriDonHangToiThieu
            })
            .ToListAsync();
    }

    private int? GetUserId()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

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