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

    // =========================================================
    // STEP 1: ĐIỀN THÔNG TIN GIAO HÀNG
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        var model = await BuildCheckoutAsync(user);
        if (!model.Items.Any()) return RedirectToAction("Index", "Cart");

        if (TempData["Checkout_FullName"] != null)
        {
            model.FullName = TempData["Checkout_FullName"]?.ToString() ?? model.FullName;
            model.PhoneNumber = TempData["Checkout_PhoneNumber"]?.ToString() ?? model.PhoneNumber;
            model.Address = TempData["Checkout_Address"]?.ToString() ?? model.Address;
            model.Note = TempData["Checkout_Note"]?.ToString();
            TempData.Keep();
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckoutViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName) ||
            string.IsNullOrWhiteSpace(model.PhoneNumber) ||
            string.IsNullOrWhiteSpace(model.Address))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng điền đầy đủ họ tên, số điện thoại và địa chỉ nhận hàng.");

            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                var refreshed = await BuildCheckoutAsync(user);
                model.Items = refreshed.Items;
                model.Subtotal = refreshed.Subtotal;
                model.ShippingFee = refreshed.ShippingFee;
            }
            return View(model);
        }

        TempData["Checkout_FullName"] = model.FullName.Trim();
        TempData["Checkout_PhoneNumber"] = model.PhoneNumber.Trim();
        TempData["Checkout_Address"] = model.Address.Trim();
        TempData["Checkout_Note"] = model.Note?.Trim();

        return RedirectToAction(nameof(Payment));
    }

    // =========================================================
    // STEP 2: CHỌN PHƯƠNG THỨC THANH TOÁN & COUPON
    // =========================================================

    [HttpGet]
    public IActionResult Payment()
    {
        if (TempData["Checkout_FullName"] == null)
            return RedirectToAction(nameof(Index));

        ViewBag.PaymentMethod = TempData["Checkout_PaymentMethod"]?.ToString() ?? "COD";
        ViewBag.CouponCode = TempData["Checkout_CouponCode"]?.ToString();

        TempData.Keep();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Payment(string paymentMethod, string? couponCode)
    {
        var allowedMethods = new[] { "CARD", "MBBANK", "MOMO", "COD" };
        if (!allowedMethods.Contains(paymentMethod))
        {
            ModelState.AddModelError(string.Empty, "Phương thức thanh toán không hợp lệ.");
            TempData.Keep();
            return View();
        }

        TempData["Checkout_PaymentMethod"] = paymentMethod;
        TempData["Checkout_CouponCode"] = couponCode?.Trim();
        TempData.Keep();

        return RedirectToAction(nameof(Review));
    }

    // =========================================================
    // STEP 3: REVIEW ĐƠN HÀNG TRƯỚC KHIN ĐẶT HÀNG
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Review()
    {
        if (TempData["Checkout_FullName"] == null)
            return RedirectToAction(nameof(Index));

        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        var model = await BuildCheckoutAsync(user);
        if (!model.Items.Any()) return RedirectToAction("Index", "Cart");

        model.FullName = TempData["Checkout_FullName"]?.ToString() ?? string.Empty;
        model.PhoneNumber = TempData["Checkout_PhoneNumber"]?.ToString() ?? string.Empty;
        model.Address = TempData["Checkout_Address"]?.ToString() ?? string.Empty;
        model.Note = TempData["Checkout_Note"]?.ToString();

        var couponCode = TempData["Checkout_CouponCode"]?.ToString();
        var coupon = await FindValidCouponAsync(couponCode, model.Subtotal);
        if (coupon != null)
        {
            model.CouponCode = coupon.MaCode;
            model.Discount = CalculateDiscount(coupon, model.Subtotal);
        }

        ViewBag.PaymentMethod = TempData["Checkout_PaymentMethod"]?.ToString() ?? "COD";
        TempData.Keep();

        return View(model);
    }

    // =========================================================
    // STEP 3.5: THỰC THI CHỐT ĐƠN (POST)
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder()
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized(new { success = false, message = "Phiên đăng nhập đã hết hạn." });
        }

        var fullName = TempData["Checkout_FullName"]?.ToString();
        var phoneNumber = TempData["Checkout_PhoneNumber"]?.ToString();
        var address = TempData["Checkout_Address"]?.ToString();
        var note = TempData["Checkout_Note"]?.ToString();
        var paymentMethod = TempData["Checkout_PaymentMethod"]?.ToString();
        var couponCode = TempData["Checkout_CouponCode"]?.ToString();

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(address))
        {
            return BadRequest(new { success = false, message = "Thiếu thông tin giao hàng. Vui lòng quay lại bước 1." });
        }

        var allowedMethods = new[] { "CARD", "MBBANK", "MOMO", "COD" };
        if (string.IsNullOrWhiteSpace(paymentMethod) || !allowedMethods.Contains(paymentMethod))
        {
            return BadRequest(new { success = false, message = "Phương thức thanh toán không hợp lệ." });
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        try
        {
            var result = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                try
                {
                    // 1. Lấy giỏ hàng
                    var cartItems = await GetCartItemsAsync(user.MaNguoiDung);
                    if (cartItems.Count == 0)
                    {
                        return new { Success = false, StatusCode = 400, Data = (object)new { success = false, message = "Giỏ hàng đang trống." } };
                    }

                    // 2. Kiểm tra tồn kho
                    if (cartItems.Any(x => x.SoLuong > x.MaBienTheNavigation.SoLuong))
                    {
                        return new { Success = false, StatusCode = 400, Data = (object)new { success = false, message = "Một số sản phẩm không còn đủ số lượng trong kho." } };
                    }

                    // 3. Tính toán số tiền
                    var subtotal = cartItems.Sum(x => x.SoLuong * (x.MaBienTheNavigation.Gia));
                    var shipping = subtotal >= 500000m ? 0m : StandardShippingFee;
                    var coupon = await FindValidCouponAsync(couponCode, subtotal);
                    var discount = coupon == null ? 0m : CalculateDiscount(coupon, subtotal);
                    var total = Math.Max(0m, subtotal + shipping - discount);

                    // 4. Tạo DonHang
                    var order = new DonHang
                    {
                        MaNguoiDung = user.MaNguoiDung,
                        NgayDat = DateTime.Now,
                        TongTien = total,
                        TrangThaiDonHang = paymentMethod == "COD" ? "Chờ xác nhận" : "Chờ thanh toán",
                        HoTenNhanHang = fullName.Trim(),
                        SoDienThoaiNhanHang = phoneNumber.Trim(),
                        DiaChiNhanHang = address.Trim(),
                        GhiChu = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                        PhuongThucVanChuyen = "Giao hàng tiêu chuẩn",
                        MaKhuyenMai = coupon?.MaKhuyenMai
                    };

                    _db.DonHangs.Add(order);
                    await _db.SaveChangesAsync();

                    // 5. Thêm ChiTietDonHang & Trừ kho
                    foreach (var item in cartItems)
                    {
                        var variant = item.MaBienTheNavigation;
                        var price = variant.Gia;

                        _db.ChiTietDonHangs.Add(new ChiTietDonHang
                        {
                            MaDonHang = order.MaDonHang,
                            MaBienThe = variant.MaBienThe,
                            SoLuong = item.SoLuong,
                            DonGia = price,
                            ThanhTien = price * item.SoLuong
                        });

                        variant.SoLuong -= item.SoLuong;
                    }

                    // 6. Giảm lượt dùng Coupon
                    if (coupon != null && coupon.SoLuong > 0)
                    {
                        coupon.SoLuong--;
                    }

                    // 7. Xóa giỏ hàng
                    _db.ChiTietGioHangs.RemoveRange(cartItems);

                    // 8. Tạo bản ghi ThanhToan
                    var paymentName = paymentMethod switch
                    {
                        "CARD" => "Thẻ ngân hàng",
                        "MBBANK" => "MB Bank",
                        "MOMO" => "MoMo",
                        _ => "COD"
                    };

                    var payment = new ThanhToan
                    {
                        MaDonHang = order.MaDonHang,
                        PhuongThuc = paymentName,
                        SoTien = total,
                        TrangThai = paymentMethod == "COD" ? "Chưa thanh toán" : "Chờ thanh toán",
                        MaGiaoDich = $"EMUA{order.MaDonHang:D6}",
                        NgayTao = DateTime.Now
                    };

                    _db.ThanhToans.Add(payment);
                    await _db.SaveChangesAsync();

                    await transaction.CommitAsync();

                    return new
                    {
                        Success = true,
                        StatusCode = 200,
                        Data = (object)new
                        {
                            success = true,
                            orderId = order.MaDonHang,
                            redirectUrl = Url.Action("Success", "Checkout", new { id = order.MaDonHang })
                        }
                    };
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (!result.Success)
            {
                return StatusCode(result.StatusCode, result.Data);
            }

            TempData.Remove("Checkout_FullName");
            TempData.Remove("Checkout_PhoneNumber");
            TempData.Remove("Checkout_Address");
            TempData.Remove("Checkout_Note");
            TempData.Remove("Checkout_PaymentMethod");
            TempData.Remove("Checkout_CouponCode");

            return Json(result.Data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PLACE ORDER ERROR: {ex}");
            return StatusCode(500, new
            {
                success = false,
                message = "Không thể tạo đơn hàng. Vui lòng thử lại.",
                error = ex.Message
            });
        }
    }

    // =========================================================
    // STEP 4: TRANG THÀNH CÔNG & HIỂN THỊ QR THANH TOÁN
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        var order = await _db.DonHangs
            .Include(x => x.ThanhToans)
            .Include(x => x.ChiTietDonHangs)
                .ThenInclude(x => x.MaBienTheNavigation)
                    .ThenInclude(x => x.MaSanPhamNavigation)
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaNguoiDung == user.MaNguoiDung);

        if (order == null) return NotFound();

        var payment = order.ThanhToans.FirstOrDefault();
        var transferContent = $"EMUA{order.MaDonHang:D6}";
        var orderTotal = order.TongTien;

        // Cấu hình thông tin ngân hàng MB Bank
        string mbAccountNo = "0963453170"; // Thay bằng số tài khoản MB của bạn
        string mbAccountName = Uri.EscapeDataString("PHAN DANG PHUONG ANH"); // Thay bằng tên chủ tài khoản (viết hoa không dấu)

        var bankQrUrl = $"https://img.vietqr.io/image/MB-{mbAccountNo}-compact2.png?amount={(long)orderTotal}&addInfo={transferContent}&accountName={mbAccountName}";
        var momoQrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=260x260&data={Uri.EscapeDataString($"2|99|0963453170|PHUONG ANH||0|0|{(long)orderTotal}|{transferContent}|transfer_myqr")}";

        ViewBag.BankQrUrl = bankQrUrl;
        ViewBag.MomoQrUrl = momoQrUrl;
        ViewBag.TransferContent = transferContent;

        var viewModel = new CheckoutSuccessViewModel
        {
            OrderId = order.MaDonHang,
            TotalAmount = (decimal)orderTotal,
            PaymentMethod = payment?.PhuongThuc ?? "COD",
            PaymentStatus = payment?.TrangThai ?? "Chờ xác nhận",
            FullName = order.HoTenNhanHang ?? string.Empty,
            PhoneNumber = order.SoDienThoaiNhanHang ?? string.Empty,
            Address = order.DiaChiNhanHang ?? string.Empty,
            BankQrUrl = bankQrUrl,
            MomoQrUrl = momoQrUrl,
            TransferContent = transferContent,
            Items = order.ChiTietDonHangs.Select(x => new CheckoutItemViewModel
            {
                VariantId = x.MaBienThe,
                ProductName = x.MaBienTheNavigation?.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm",
                VariantName = string.Join(" · ", new[] { x.MaBienTheNavigation?.MauSac, x.MaBienTheNavigation?.PhienBan }.Where(s => !string.IsNullOrWhiteSpace(s))),
                ImageUrl = x.MaBienTheNavigation?.HinhAnh,
                UnitPrice = x.DonGia,
                Quantity = x.SoLuong
            }).ToList()
        };

        return View(viewModel);
    }

    // =========================================================
    // AJAX: ÁP DỤNG COUPON
    // =========================================================

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

        TempData["Checkout_CouponCode"] = coupon.MaCode;
        TempData.Keep();

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

    // =========================================================
    // HELPER METHODS
    // =========================================================

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
                VariantName = string.Join(" · ", new[] { x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan }.Where(s => !string.IsNullOrWhiteSpace(s))),
                ImageUrl = x.MaBienTheNavigation.HinhAnh,
                UnitPrice = x.MaBienTheNavigation.Gia,
                Quantity = x.SoLuong
            }).ToList()
        };
        model.Subtotal = model.Items.Sum(x => x.LineTotal);
        model.ShippingFee = model.Subtotal >= 500000m ? 0m : StandardShippingFee;
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
        if (coupon == null || !coupon.TrangThai || coupon.SoLuong <= 0 || coupon.NgayBatDau > now || coupon.NgayKetThuc < now || (coupon.GiaTriDonHangToiThieu) > subtotal)
            return null;
        return coupon;
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        (coupon.LoaiGiamGia ?? string.Empty).Contains("%") || (coupon.LoaiGiamGia ?? string.Empty).Contains("trăm", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(subtotal, subtotal * (coupon.GiaTriGiam) / 100m)
            : Math.Min(subtotal, coupon.GiaTriGiam);
}
