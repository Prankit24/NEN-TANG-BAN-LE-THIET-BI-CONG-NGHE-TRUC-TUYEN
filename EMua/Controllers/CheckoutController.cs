using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private readonly EMuaDbContext _db;

    public CheckoutController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(string? couponCode)
    {
        var model = await BuildModelAsync(couponCode);
        return model.Items.Count == 0 ? RedirectToAction("Index", "Cart") : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(CheckoutViewModel model)
    {
        model.CouponCode = NormalizeCouponCode(model.CouponCode);
        var refreshed = await BuildModelAsync(model.CouponCode, model);
        ModelState.Clear();
        if (!string.IsNullOrWhiteSpace(model.CouponCode) && refreshed.Discount == 0)
            ModelState.AddModelError(nameof(model.CouponCode), "Mã giảm giá không hợp lệ hoặc chưa đủ điều kiện.");
        else if (refreshed.Discount > 0)
            TempData["Success"] = $"Đã áp dụng mã giảm giá {model.CouponCode}.";
        return View(nameof(Index), refreshed);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(CheckoutViewModel model)
    {
        model.CouponCode = NormalizeCouponCode(model.CouponCode);
        NormalizePayment(model);
        ValidatePaymentProvider(model);
        if (!ModelState.IsValid)
            return View(nameof(Index), await BuildModelAsync(model.CouponCode, model));

        return View(nameof(Review), await BuildModelAsync(model.CouponCode, model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Payment(CheckoutViewModel model)
    {
        model.CouponCode = NormalizeCouponCode(model.CouponCode);
        NormalizePayment(model);
        ValidatePaymentProvider(model);
        if (!ModelState.IsValid)
            return View(nameof(Review), model);

        return View(await BuildModelAsync(model.CouponCode, model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteOrder(CheckoutViewModel model)
    {
        model.CouponCode = NormalizeCouponCode(model.CouponCode);
        NormalizePayment(model);
        ValidatePaymentProvider(model);
        if (!ModelState.IsValid)
            return View(nameof(Payment), model);

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        var orderId = await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var cartItems = await _db.ChiTietGioHangs.Include(x => x.MaBienTheNavigation)
                .Where(x => x.MaNguoiDung == CurrentUserId()).ToListAsync();
            if (cartItems.Count == 0) return (int?)null;
            if (cartItems.Any(x => x.SoLuong <= 0 || x.SoLuong > x.MaBienTheNavigation.SoLuong))
                return -1;

            var subtotal = cartItems.Sum(x => x.MaBienTheNavigation.Gia * x.SoLuong);
            var promotion = await FindPromotionAsync(model.CouponCode, subtotal);
            var discount = CalculateDiscount(promotion, subtotal);
            var shippingMethod = NormalizeShippingMethod(model.ShippingMethod);
            var total = Math.Max(0, subtotal - discount + (shippingMethod == "EXPRESS" ? 25000 : 0));
            var order = new DonHang
            {
                MaNguoiDung = CurrentUserId(), NgayDat = DateTime.Now,
                TongTien = total, TrangThaiDonHang = "Chờ xử lý",
                HoTenNhanHang = model.RecipientName.Trim(), SoDienThoaiNhanHang = model.RecipientPhone.Trim(),
                DiaChiNhanHang = model.ShippingAddress.Trim(), GhiChu = model.Note?.Trim(),
                PhuongThucVanChuyen = shippingMethod == "EXPRESS" ? "Giao hàng nhanh" : "Giao hàng tiêu chuẩn",
                MaKhuyenMai = promotion?.MaKhuyenMai
            };
            _db.DonHangs.Add(order);

            foreach (var item in cartItems)
            {
                item.MaBienTheNavigation.SoLuong -= item.SoLuong;
                order.ChiTietDonHangs.Add(new ChiTietDonHang
                {
                    MaBienThe = item.MaBienThe, SoLuong = item.SoLuong,
                    DonGia = item.MaBienTheNavigation.Gia,
                    ThanhTien = item.MaBienTheNavigation.Gia * item.SoLuong
                });
            }

            if (promotion?.SoLuong is > 0)
                promotion.SoLuong--;

            order.ThanhToans.Add(new ThanhToan
            {
                PhuongThuc = model.PaymentMethod == "COD" ? "COD" : $"{model.PaymentMethod} - {model.PaymentProvider}",
                SoTien = total,
                TrangThai = model.PaymentMethod == "COD" ? "Chưa thanh toán" : "Đã thanh toán",
                MaGiaoDich = model.PaymentMethod == "COD" ? null : $"LOCAL-{Guid.NewGuid():N}"[..20],
                NgayTao = DateTime.Now,
                NgayThanhToan = model.PaymentMethod == "COD" ? null : DateTime.Now
            });
            order.LichSuVanChuyens.Add(new LichSuVanChuyen
            {
                TrangThai = "Chờ xử lý", MoTa = "Đơn hàng đã được tiếp nhận.", ThoiGian = DateTime.Now
            });
            _db.ChiTietGioHangs.RemoveRange(cartItems);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (int?)order.MaDonHang;
        });

        if (orderId == null) return RedirectToAction("Index", "Cart");
        if (orderId == -1)
        {
            ModelState.AddModelError(string.Empty, "Tồn kho đã thay đổi. Vui lòng kiểm tra lại giỏ hàng.");
            return View(nameof(Payment), model);
        }
        return RedirectToAction(nameof(Success), new { id = orderId.Value });
    }

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var payment = await _db.ThanhToans.AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaDonHangNavigation.MaNguoiDung == CurrentUserId());
        if (payment == null) return NotFound();
        return View(new CheckoutSuccessViewModel
        {
            OrderId = id,
            Total = payment.SoTien,
            IsPaid = payment.TrangThai == "Đã thanh toán"
        });
    }

    private async Task<CheckoutViewModel> BuildModelAsync(string? code, CheckoutViewModel? posted = null)
    {
        var user = await _db.NguoiDungs.AsNoTracking().FirstOrDefaultAsync(x => x.MaNguoiDung == CurrentUserId());
        var entities = await _db.ChiTietGioHangs.AsNoTracking()
            .Include(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Where(x => x.MaNguoiDung == CurrentUserId()).OrderBy(x => x.NgayThem).ToListAsync();
        var items = entities.Select(x => new CartItemViewModel
        {
            CartItemId = x.MaChiTietGioHang, ProductId = x.MaBienTheNavigation.MaSanPham, VariantId = x.MaBienThe,
            ProductName = x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
            VariantLabel = string.Join(" / ", new[] { x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan }.Where(v => !string.IsNullOrWhiteSpace(v))),
            ImageUrl = x.MaBienTheNavigation.HinhAnh, UnitPrice = x.MaBienTheNavigation.Gia,
            Quantity = x.SoLuong, Stock = x.MaBienTheNavigation.SoLuong
        }).ToList();
        var promotion = await FindPromotionAsync(code, items.Sum(x => x.LineTotal));
        var model = posted ?? new CheckoutViewModel();
        model.Items = items; model.CouponCode = code;
        model.Discount = CalculateDiscount(promotion, model.Subtotal);
        model.AvailableCoupons = await GetAvailableCouponsAsync(model.Subtotal);
        if (string.IsNullOrWhiteSpace(model.RecipientName)) model.RecipientName = user?.TenNguoiDung ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model.RecipientPhone)) model.RecipientPhone = user?.SoDienThoai ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model.ShippingAddress)) model.ShippingAddress = user?.DiaChi ?? string.Empty;
        return model;
    }

    private async Task<List<CouponOptionViewModel>> GetAvailableCouponsAsync(decimal subtotal)
    {
        var now = DateTime.Now;
        var promotions = await _db.KhuyenMais.AsNoTracking()
            .Where(x => x.TrangThai && (x.SoLuong == null || x.SoLuong > 0) &&
                (!x.NgayBatDau.HasValue || x.NgayBatDau <= now) &&
                (!x.NgayKetThuc.HasValue || x.NgayKetThuc >= now) &&
                (!x.GiaTriDonHangToiThieu.HasValue || subtotal >= x.GiaTriDonHangToiThieu))
            .OrderBy(x => x.GiaTriDonHangToiThieu)
            .ThenBy(x => x.MaCode)
            .ToListAsync();

        return promotions.Select(x => new CouponOptionViewModel
        {
            Code = x.MaCode,
            Name = x.TenKhuyenMai,
            DiscountText = IsPercentagePromotion(x.LoaiGiamGia)
                ? $"Giảm {x.GiaTriGiam:0.##}%"
                : $"Giảm {x.GiaTriGiam:N0} đ",
            MinimumOrder = x.GiaTriDonHangToiThieu
        }).ToList();
    }

    private async Task<KhuyenMai?> FindPromotionAsync(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var now = DateTime.Now;
        var normalizedCode = NormalizeCouponCode(code);
        return await _db.KhuyenMais.FirstOrDefaultAsync(x => x.MaCode != null &&
            x.MaCode.ToLower() == normalizedCode.ToLower() && x.TrangThai &&
            (x.SoLuong == null || x.SoLuong > 0) && (!x.NgayBatDau.HasValue || x.NgayBatDau <= now) &&
            (!x.NgayKetThuc.HasValue || x.NgayKetThuc >= now) &&
            (!x.GiaTriDonHangToiThieu.HasValue || subtotal >= x.GiaTriDonHangToiThieu));
    }

    private static decimal CalculateDiscount(KhuyenMai? promotion, decimal subtotal)
    {
        if (promotion == null) return 0;
        var discountType = promotion.LoaiGiamGia?.Trim().ToLowerInvariant() ?? string.Empty;
        var discount = IsPercentagePromotion(discountType)
            ? subtotal * promotion.GiaTriGiam / 100 : promotion.GiaTriGiam;
        return Math.Min(subtotal, Math.Max(0, discount));
    }

    private static bool IsPercentagePromotion(string? type)
    {
        var normalizedType = type?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalizedType.Contains('%') || normalizedType.Contains("phần trăm") ||
               normalizedType.Contains("phan tram") || normalizedType.Contains("percent");
    }

    private static string NormalizeCouponCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpperInvariant();

    private static string NormalizePaymentSelection(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "BANK" => "BANK", "MOMO" => "MOMO", _ => "COD"
    };

    private static void NormalizePayment(CheckoutViewModel model)
    {
        model.PaymentMethod = NormalizePaymentSelection(model.PaymentMethod);
        model.PaymentProvider = NormalizePaymentProvider(model.PaymentMethod, model.PaymentProvider);
    }

    private void ValidatePaymentProvider(CheckoutViewModel model)
    {
        if (model.PaymentMethod != "COD" && string.IsNullOrWhiteSpace(model.PaymentProvider))
            ModelState.AddModelError(nameof(model.PaymentProvider), "Vui lòng chọn ví điện tử hoặc ngân hàng.");
    }

    private static string? NormalizePaymentProvider(string paymentMethod, string? provider)
    {
        var normalizedProvider = provider?.Trim().ToUpperInvariant();
        return paymentMethod switch
        {
            "MOMO" when normalizedProvider is "MOMO" or "ZALOPAY" or "APPLEPAY" => normalizedProvider,
            "BANK" when normalizedProvider is "VIETCOMBANK" or "VIETINBANK" or "MB" or "TPBANK" => normalizedProvider,
            _ => null
        };
    }

    private static string NormalizeShippingMethod(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "EXPRESS" => "EXPRESS", _ => "STANDARD"
    };

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}