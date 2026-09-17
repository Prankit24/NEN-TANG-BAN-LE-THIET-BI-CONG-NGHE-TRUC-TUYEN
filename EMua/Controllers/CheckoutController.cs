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
    private const decimal StandardShippingFee = 30000m;
    private readonly EMuaDbContext _db;

    public CheckoutController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        return View(await BuildCheckoutAsync(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon([FromBody] ApplyCouponRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });

        var checkout = await BuildCheckoutAsync(user);
        var coupon = await FindValidCouponAsync(request.CouponCode, checkout.Subtotal);
        if (coupon == null)
            return BadRequest(new { success = false, message = "Mã giảm giá không hợp lệ, đã hết hạn hoặc chưa đủ giá trị đơn tối thiểu." });

        checkout.Discount = CalculateDiscount(coupon, checkout.Subtotal);
        return Json(new
        {
            success = true,
            message = $"Đã áp dụng mã {coupon.MaCode}.",
            couponCode = coupon.MaCode,
            discount = checkout.Discount,
            shippingFee = checkout.ShippingFee,
            total = checkout.Total
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Address))
            return BadRequest(new { success = false, message = "Vui lòng nhập đầy đủ họ tên, số điện thoại và địa chỉ nhận hàng." });

        var allowedMethods = new[] { "CARD", "MBBANK", "MOMO", "COD" };
        if (!allowedMethods.Contains(request.PaymentMethod))
            return BadRequest(new { success = false, message = "Phương thức thanh toán không hợp lệ." });

        var cartItems = await GetCartItemsAsync(user.MaNguoiDung);
        if (cartItems.Count == 0)
            return BadRequest(new { success = false, message = "Giỏ hàng đang trống." });

        if (cartItems.Any(x => x.SoLuong > x.MaBienTheNavigation.SoLuong))
            return BadRequest(new { success = false, message = "Một số sản phẩm không còn đủ số lượng trong kho." });

        var subtotal = cartItems.Sum(x => x.SoLuong * x.MaBienTheNavigation.Gia);
        var shipping = subtotal >= 500000m ? 0 : StandardShippingFee;
        var coupon = await FindValidCouponAsync(request.CouponCode, subtotal);
        var discount = coupon == null ? 0 : CalculateDiscount(coupon, subtotal);
        var total = Math.Max(0, subtotal + shipping - discount);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = new DonHang
            {
                MaNguoiDung = user.MaNguoiDung,
                NgayDat = DateTime.Now,
                TongTien = total,
                TrangThaiDonHang = request.PaymentMethod == "COD" ? "Chờ xác nhận" : "Chờ thanh toán",
                HoTenNhanHang = request.FullName.Trim(),
                SoDienThoaiNhanHang = request.PhoneNumber.Trim(),
                DiaChiNhanHang = request.Address.Trim(),
                GhiChu = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                PhuongThucVanChuyen = "Giao hàng tiêu chuẩn",
                MaKhuyenMai = coupon?.MaKhuyenMai
            };
            _db.DonHangs.Add(order);
            await _db.SaveChangesAsync();

            foreach (var item in cartItems)
            {
                var variant = item.MaBienTheNavigation;
                _db.ChiTietDonHangs.Add(new ChiTietDonHang
                {
                    MaDonHang = order.MaDonHang,
                    MaBienThe = variant.MaBienThe,
                    SoLuong = item.SoLuong,
                    DonGia = variant.Gia,
                    ThanhTien = variant.Gia * item.SoLuong
                });
                variant.SoLuong -= item.SoLuong;
            }

            if (coupon?.SoLuong is > 0) coupon.SoLuong--;
            _db.ChiTietGioHangs.RemoveRange(cartItems);

            var paymentName = request.PaymentMethod switch
            {
                "CARD" => "Thẻ ngân hàng",
                "MBBANK" => "MB Bank",
                "MOMO" => "MoMo",
                _ => "COD"
            };
            _db.ThanhToans.Add(new ThanhToan
            {
                MaDonHang = order.MaDonHang,
                PhuongThuc = paymentName,
                SoTien = total,
                TrangThai = request.PaymentMethod == "COD" ? "Chưa thanh toán" : "Chờ thanh toán",
                MaGiaoDich = $"EMUA{order.MaDonHang:D6}",
                NgayTao = DateTime.Now
            });
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            var transferContent = $"EMUA{order.MaDonHang:D6}";
            var bankQrUrl = $"https://img.vietqr.io/image/MB-22224032005-compact2.png?amount={total:0}&addInfo={transferContent}&accountName=TECH";
            var momoQrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=260x260&data={Uri.EscapeDataString("https://momo.vn/0963453170")}";

            return Json(new
            {
                success = true,
                orderId = order.MaDonHang,
                paymentMethod = request.PaymentMethod,
                amount = total,
                transferContent,
                bankQrUrl,
                momoQrUrl,
                message = request.PaymentMethod == "COD"
                    ? "Đặt hàng thành công. Đơn hàng đang chờ xác nhận."
                    : "Đã tạo đơn hàng. Vui lòng hoàn tất thanh toán theo hướng dẫn."
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Không thể tạo đơn hàng. Vui lòng thử lại." });
        }
    }

    private async Task<CheckoutViewModel> BuildCheckoutAsync(NguoiDung user)
    {
        var cartItems = await GetCartItemsAsync(user.MaNguoiDung);
        var model = new CheckoutViewModel
        {
            FullName = user.TenNguoiDung ?? string.Empty,
            PhoneNumber = user.SoDienThoai ?? string.Empty,
            Address = user.DiaChi ?? string.Empty,
            Items = cartItems.Select(x => new CheckoutItemViewModel
            {
                VariantId = x.MaBienThe,
                ProductName = x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
                VariantName = string.Join(" · ", new[] { x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan }.Where(x => !string.IsNullOrWhiteSpace(x))),
                ImageUrl = x.MaBienTheNavigation.HinhAnh,
                UnitPrice = x.MaBienTheNavigation.Gia,
                Quantity = x.SoLuong
            }).ToList()
        };
        model.Subtotal = model.Items.Sum(x => x.LineTotal);
        model.ShippingFee = model.Subtotal >= 500000m ? 0 : StandardShippingFee;
        return model;
    }

    private Task<List<ChiTietGioHang>> GetCartItemsAsync(int userId) => _db.ChiTietGioHangs
        .Include(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
        .Where(x => x.MaNguoiDung == userId).ToListAsync();

    private async Task<NguoiDung?> GetCurrentUserAsync()
    {
        var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idText, out var id) ? await _db.NguoiDungs.FindAsync(id) : null;
    }

    private async Task<KhuyenMai?> FindValidCouponAsync(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var now = DateTime.Now;
        var coupon = await _db.KhuyenMais.FirstOrDefaultAsync(x => x.MaCode.ToLower() == code.Trim().ToLower());
        if (coupon == null || !coupon.TrangThai || coupon.SoLuong is <= 0 || coupon.NgayBatDau > now || coupon.NgayKetThuc < now || (coupon.GiaTriDonHangToiThieu ?? 0) > subtotal)
            return null;
        return coupon;
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        coupon.LoaiGiamGia.Contains("%") || coupon.LoaiGiamGia.Contains("trăm", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(subtotal, subtotal * coupon.GiaTriGiam / 100m)
            : Math.Min(subtotal, coupon.GiaTriGiam);
}
