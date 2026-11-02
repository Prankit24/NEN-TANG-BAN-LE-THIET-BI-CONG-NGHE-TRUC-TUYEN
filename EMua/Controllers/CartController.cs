using EMua.Services;
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
        // CouponOptions loads only two eligible coupons per request.

        return View(viewModel);
    }

    private bool WantsJson => Request.Headers.Accept.ToString().Contains("application/json");

    private async Task<decimal> CartSubtotalAsync(int userId) =>
        await _db.ChiTietGioHangs.Where(x => x.MaNguoiDung == userId)
            .SumAsync(x => (decimal?)(x.SoLuong * x.MaBienTheNavigation.Gia)) ?? 0m;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CouponOptions(int page = 1)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
        var subtotal = await CartSubtotalAsync(userId.Value);
        var now = DateTime.Now;
        var query = _db.KhuyenMais.AsNoTracking().Where(x =>
            subtotal > 0 && x.TrangThai && x.GiaTriGiam > 0 &&
            (x.SoLuong == null || x.SoLuong > 0) &&
            (x.NgayBatDau == null || x.NgayBatDau <= now) &&
            (x.NgayKetThuc == null || x.NgayKetThuc >= now) &&
            (x.GiaTriDonHangToiThieu ?? 0) <= subtotal);
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / 2d));
        page = Math.Clamp(page, 1, pages);
        var coupons = await query.OrderBy(x => x.MaKhuyenMai)
            .Skip((page - 1) * 2).Take(2).ToListAsync();
        return Json(new
        {
            page, pages, total,
            items = coupons.Select(x => new
            {
                code = x.MaCode, name = x.TenKhuyenMai,
                discount = CalculateDiscount(x, subtotal),
                minimum = x.GiaTriDonHangToiThieu ?? 0m,
                expires = x.NgayKetThuc?.ToString("dd/MM/yyyy HH:mm") ?? "Không giới hạn",
                label = ((x.LoaiGiamGia ?? "").Contains("%") || (x.LoaiGiamGia ?? "").Contains("trăm", StringComparison.OrdinalIgnoreCase))
                    ? $"Giảm {x.GiaTriGiam:0.##}%" : $"Giảm {x.GiaTriGiam:N0}đ"
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string? code)
    {
        var userId = GetUserId();
        if (userId == null)
            return WantsJson ? Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." })
                : RedirectToAction("Login", "Auth");
        var subtotal = await CartSubtotalAsync(userId.Value);
        var check = await CheckCouponAsync(code, subtotal);
        if (check.Error != null)
        {
            if (WantsJson) return BadRequest(new { success = false, message = check.Error });
            TempData["CouponError"] = check.Error;
            return RedirectToAction(nameof(Index));
        }
        var coupon = check.Coupon!;
        HttpContext.Session.SetString(CouponSessionKey, coupon.MaCode);
        var discount = CalculateDiscount(coupon, subtotal);
        var message = $"Đã áp dụng {coupon.MaCode} — giảm {discount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}đ.";
        if (WantsJson) return CouponTotals(subtotal, discount, coupon.MaCode, message);
        TempData["CouponSuccess"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCoupon()
    {
        var userId = GetUserId();
        if (userId == null)
            return WantsJson ? Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." })
                : RedirectToAction("Login", "Auth");
        HttpContext.Session.Remove(CouponSessionKey);
        if (WantsJson) return CouponTotals(await CartSubtotalAsync(userId.Value), 0m, null, "Đã bỏ mã giảm giá.");
        TempData["CouponSuccess"] = "Đã bỏ mã giảm giá.";
        return RedirectToAction(nameof(Index));
    }

    private JsonResult CouponTotals(decimal subtotal, decimal discount, string? code, string message)
    {
        var shipping = subtotal <= 0 || subtotal >= 500000m ? 0m : 30000m;
        return Json(new { success = true, message, code, subtotal, discount, shipping,
            total = Math.Max(0m, subtotal + shipping - discount) });
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

    // Form chi tiết sản phẩm: xử lý "Thêm vào giỏ" và "Mua ngay" (khác endpoint AJAX Add).
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFromProduct(
        int productId, int variantId, int quantity, bool buyNow = false, string? returnUrl = null)
    {
        string backUrl = Url.IsLocalUrl(returnUrl)
            ? returnUrl!
            : Url.Action("Details", "Product", new { id = productId }) ?? "/Product";

        // Nếu chưa đăng nhập, đưa khách về trang sản phẩm để thao tác lại sau khi đăng nhập.
        var userId = GetUserId();
        if (userId == null)
            return RedirectToAction("Login", "Auth", new { returnUrl = backUrl });

        if (productId <= 0 || variantId <= 0 || quantity <= 0)
        {
            TempData["Error"] = "Vui lòng chọn phiên bản và số lượng hợp lệ.";
            return LocalRedirect(backUrl);
        }

        var variant = await _db.BienTheSanPhams
            .Include(x => x.MaSanPhamNavigation)
            .FirstOrDefaultAsync(x => x.MaBienThe == variantId && x.MaSanPham == productId);

        if (variant == null || variant.TrangThai != "Còn hàng" ||
            variant.MaSanPhamNavigation.TrangThaiSanPham == "Ẩn" ||
            variant.SoLuong < quantity || variant.Gia <= 0)
        {
            TempData["Error"] = "Phiên bản đã chọn không còn đủ hàng. Vui lòng chọn lại.";
            return LocalRedirect(backUrl);
        }

        if (buyNow)
        {
            // Không ghi vào bảng giỏ hàng: thanh toán riêng đúng phiên bản và số lượng đã chọn.
            TempData["Checkout_BuyNowExpected"] = true;
            HttpContext.Session.SetInt32("Checkout_BuyNowUserId", userId.Value);
            HttpContext.Session.SetInt32("Checkout_BuyNowVariantId", variantId);
            HttpContext.Session.SetInt32("Checkout_BuyNowQuantity", quantity);

            // Một lần mua mới không được kế thừa dữ liệu tạm từ lần thanh toán trước.
            foreach (var key in new[] { "Checkout_FullName", "Checkout_PhoneNumber", "Checkout_Address",
                                        "Checkout_Note", "Checkout_PaymentMethod", "Checkout_CouponCode" })
                TempData.Remove(key);

            return RedirectToAction("Index", "Checkout");
        }

        var cartItem = await _db.ChiTietGioHangs.FirstOrDefaultAsync(x =>
            x.MaNguoiDung == userId.Value && x.MaBienThe == variantId);

        var combinedQuantity = (long)(cartItem?.SoLuong ?? 0) + quantity;
        if (combinedQuantity > variant.SoLuong)
        {
            TempData["Error"] = "Số lượng trong giỏ hàng cộng với số lượng mới vượt quá tồn kho.";
            return LocalRedirect(backUrl);
        }

        if (cartItem == null)
        {
            _db.ChiTietGioHangs.Add(new ChiTietGioHang
            {
                MaNguoiDung = userId.Value,
                MaBienThe = variantId,
                SoLuong = quantity,
                NgayThem = DateTime.Now
            });
        }
        else
        {
            cartItem.SoLuong = (int)combinedQuantity;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã thêm sản phẩm vào giỏ hàng.";
        return LocalRedirect(backUrl);
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
            .Include(x => x.MaSanPhamNavigation)
            .FirstOrDefaultAsync(x => x.MaBienThe == request.VariantId);

        if (variant == null || variant.TrangThai != "Còn hàng" || variant.Gia <= 0 || variant.MaSanPhamNavigation?.TrangThaiSanPham == "Ẩn")
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

        var newQuantity = (long)(cartItem?.SoLuong ?? 0) + request.Quantity;

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
            cartItem.SoLuong = (int)newQuantity;
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
            else
            {
                TempData["Error"] = "Số lượng yêu cầu vượt quá tồn kho.";
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
                Quantity = (int)Math.Min(int.MaxValue, g.Sum(x => (long)x.Quantity))
            })
            .ToList();

        foreach (var guestItem in normalizedItems)
        {
            var variant = await _db.BienTheSanPhams
                .Include(x => x.MaSanPhamNavigation)
                .FirstOrDefaultAsync(x => x.MaBienThe == guestItem.VariantId);

            if (variant == null || variant.TrangThai != "Còn hàng" || variant.SoLuong <= 0 || variant.Gia <= 0 || variant.MaSanPhamNavigation?.TrangThaiSanPham == "Ẩn")
                continue;

            var dbItem = await _db.ChiTietGioHangs
                .FirstOrDefaultAsync(x =>
                    x.MaNguoiDung == userId &&
                    x.MaBienThe == guestItem.VariantId);

            var currentQuantity = dbItem?.SoLuong ?? 0;
            var desiredQuantity = (long)currentQuantity + guestItem.Quantity;
            var safeQuantity = (int)Math.Min(desiredQuantity, variant.SoLuong);

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

        var check = await CheckCouponAsync(code, model.Subtotal);
        var coupon = check.Coupon;

        if (coupon == null)
        {
            HttpContext.Session.Remove(CouponSessionKey);
            TempData["CouponError"] = check.Error + " Mã đã được gỡ khỏi giỏ hàng.";
            return;
        }

        model.CouponCode = coupon.MaCode;
        model.Discount = CalculateDiscount(coupon, model.Subtotal);
    }

    private async Task<(KhuyenMai? Coupon, string? Error)> CheckCouponAsync(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return (null, "Vui lòng nhập mã giảm giá.");
        if (code.Trim().Length > 50) return (null, "Mã giảm giá không hợp lệ (tối đa 50 ký tự).");
        if (subtotal <= 0) return (null, "Giỏ hàng chưa có sản phẩm để áp dụng mã.");
        var normalized = code.Trim().ToLower();
        var coupon = await _db.KhuyenMais.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaCode.Trim().ToLower() == normalized);
        if (coupon == null) return (null, "Không tìm thấy mã giảm giá này. Bạn kiểm tra lại mã nhé.");
        if (!coupon.TrangThai) return (null, "Mã giảm giá này đang tạm ngừng áp dụng.");
        var now = DateTime.Now;
        if (coupon.NgayBatDau > now) return (null, $"Mã bắt đầu áp dụng lúc {coupon.NgayBatDau:dd/MM/yyyy HH:mm}.");
        if (coupon.NgayKetThuc < now) return (null, "Mã giảm giá đã hết hạn.");
        if (coupon.SoLuong is <= 0) return (null, "Mã giảm giá đã hết lượt sử dụng.");
        var minimum = coupon.GiaTriDonHangToiThieu ?? 0m;
        if (minimum > subtotal)
            return (null, $"Mua thêm {(minimum - subtotal).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}đ để sử dụng mã này.");
        if (coupon.GiaTriGiam <= 0) return (null, "Mã này chưa có mức giảm hợp lệ. Vui lòng chọn mã khác.");
        return (coupon, null);
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        ShoppingRules.Discount(coupon, subtotal);

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
