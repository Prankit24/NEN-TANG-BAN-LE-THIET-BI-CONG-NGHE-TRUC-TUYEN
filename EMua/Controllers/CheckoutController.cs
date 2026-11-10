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
public class CheckoutController : Controller
{
    private const decimal StandardShippingFee = 30000m;
    private const decimal ExpressShippingFee = 25000m;
    private const string BuyNowUserKey = "Checkout_BuyNowUserId";
    private const string BuyNowVariantKey = "Checkout_BuyNowVariantId";
    private const string BuyNowQuantityKey = "Checkout_BuyNowQuantity";
    private const string BuyNowExpectedKey = "Checkout_BuyNowExpected";
    private readonly EMuaDbContext _db;

    public CheckoutController(EMuaDbContext db) => _db = db;

    // Các phương thức thanh toán hợp lệ
    private static readonly string[] AllowedPaymentMethods = { "COD", "BANK_TRANSFER", "WEB3" };

    // =========================================================
    // STEP 1: ĐIỀN THÔNG TIN GIAO HÀNG
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index(bool fromCart = false)
    {
        // Bấm Thanh toán từ giỏ hàng => thanh toán tất cả món trong giỏ.
        if (fromCart)
        {
            ClearBuyNowSession();
            TempData.Remove(BuyNowExpectedKey);
            TempData["Checkout_CouponCode"] = HttpContext.Session.GetString("Cart_CouponCode") ?? "";
        }

        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        // Ghi lại chế độ thanh toán để tránh âm thầm chuyển sang giỏ hàng nếu session hết hạn.
        if (!TempData.ContainsKey(BuyNowExpectedKey))
            TempData[BuyNowExpectedKey] = IsBuyNowCheckout(user.MaNguoiDung);
        if (IsExpectedBuyNowLost(user.MaNguoiDung))
        {
            TempData["Error"] = "Phiên Mua ngay đã hết hạn. Vui lòng chọn mua lại sản phẩm.";
            TempData.Remove(BuyNowExpectedKey);
            return RedirectToAction("Index", "Cart");
        }

        var model = await BuildCheckoutAsync(user);
        if (!model.Items.Any())
        {
            TempData["Error"] = "Không có sản phẩm hợp lệ để thanh toán hoặc sản phẩm đã hết hàng.";
            return RedirectToAction("Index", "Cart");
        }

        if (TempData["Checkout_FullName"] != null)
        {
            model.FullName = TempData["Checkout_FullName"]?.ToString() ?? model.FullName;
            model.PhoneNumber = TempData["Checkout_PhoneNumber"]?.ToString() ?? model.PhoneNumber;
            model.Address = TempData["Checkout_Address"]?.ToString() ?? model.Address;
            model.Note = TempData["Checkout_Note"]?.ToString();
            var savedShipping = TempData["Checkout_ShippingMethod"]?.ToString();
            if (savedShipping == "EXPRESS") model.ShippingMethod = "EXPRESS";
            TempData.Keep();
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckoutViewModel model)
    {
        var submittingUser = await GetCurrentUserAsync();
        if (submittingUser == null) return RedirectToAction("Login", "Auth");
        if (IsExpectedBuyNowLost(submittingUser.MaNguoiDung))
        {
            TempData["Error"] = "Phiên Mua ngay đã hết hạn. Vui lòng chọn mua lại sản phẩm.";
            return RedirectToAction("Index", "Cart");
        }

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
        TempData["Checkout_ShippingMethod"] = model.ShippingMethod == "EXPRESS" ? "EXPRESS" : "STANDARD";

        return RedirectToAction(nameof(Payment));
    }

    // =========================================================
    // STEP 2: CHỌN PHƯƠNG THỨC THANH TOÁN & COUPON
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Payment()
    {
        if (TempData["Checkout_FullName"] == null)
            return RedirectToAction(nameof(Index));

        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        if (IsExpectedBuyNowLost(user.MaNguoiDung))
        {
            TempData["Error"] = "Phiên Mua ngay đã hết hạn. Vui lòng chọn mua lại sản phẩm.";
            return RedirectToAction("Index", "Cart");
        }

        var model = await BuildCheckoutAsync(user);
        if (!model.Items.Any()) return RedirectToAction("Index", "Cart");

        model.FullName = TempData["Checkout_FullName"]?.ToString() ?? string.Empty;
        model.PhoneNumber = TempData["Checkout_PhoneNumber"]?.ToString() ?? string.Empty;
        model.Address = TempData["Checkout_Address"]?.ToString() ?? string.Empty;
        model.Note = TempData["Checkout_Note"]?.ToString();

        model.PaymentMethod = TempData["Checkout_PaymentMethod"]?.ToString() ?? "COD";
        model.CouponCode = TempData["Checkout_CouponCode"]?.ToString();

        var coupon = await FindValidCouponAsync(model.CouponCode, model.Subtotal);
        if (coupon != null)
        {
            model.Discount = CalculateDiscount(coupon, model.Subtotal);
        }

        ViewBag.PaymentMethod = model.PaymentMethod;
        ViewBag.CouponCode = model.CouponCode;

        TempData.Keep();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Payment(string paymentMethod, string? couponCode)
    {
        if (!AllowedPaymentMethods.Contains(paymentMethod))
        {
            ModelState.AddModelError(string.Empty, "Phương thức thanh toán không hợp lệ.");

            var user = await GetCurrentUserAsync();
            if (user != null)
            {
                var model = await BuildCheckoutAsync(user);
                model.FullName = TempData["Checkout_FullName"]?.ToString() ?? string.Empty;
                model.PhoneNumber = TempData["Checkout_PhoneNumber"]?.ToString() ?? string.Empty;
                model.Address = TempData["Checkout_Address"]?.ToString() ?? string.Empty;
                model.Note = TempData["Checkout_Note"]?.ToString();

                TempData.Keep();
                return View(model);
            }
            return RedirectToAction(nameof(Index));
        }

        var customer = await GetCurrentUserAsync();
        if (customer == null) return RedirectToAction("Login", "Auth");
        var checkout = await BuildCheckoutAsync(customer);
        if (!checkout.Items.Any()) return RedirectToAction("Index", "Cart");
        var appliedCoupon = await FindValidCouponAsync(couponCode, checkout.Subtotal);
        if (!string.IsNullOrWhiteSpace(couponCode) && appliedCoupon == null)
        {
            ModelState.AddModelError(string.Empty, "Mã giảm giá không hợp lệ, hết lượt, hết hạn hoặc đơn hàng chưa đạt giá trị tối thiểu.");
            checkout.Discount = 0m;
            checkout.CouponCode = couponCode;
            checkout.PaymentMethod = paymentMethod;
            TempData.Keep();
            return View(checkout);
        }
        couponCode = appliedCoupon?.MaCode;
        TempData["Checkout_PaymentMethod"] = paymentMethod;
        TempData["Checkout_CouponCode"] = couponCode?.Trim();
        TempData.Keep();

        return RedirectToAction(nameof(Review));
    }

    // =========================================================
    // STEP 3: REVIEW ĐƠN HÀNG TRƯỚC KHI ĐẶT HÀNG
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Review()
    {
        if (TempData["Checkout_FullName"] == null)
            return RedirectToAction(nameof(Index));

        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        if (IsExpectedBuyNowLost(user.MaNguoiDung))
        {
            TempData["Error"] = "Phiên Mua ngay đã hết hạn. Vui lòng chọn mua lại sản phẩm.";
            return RedirectToAction("Index", "Cart");
        }

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

        var fullName = TempData.Peek("Checkout_FullName")?.ToString();
        var phoneNumber = TempData.Peek("Checkout_PhoneNumber")?.ToString();
        var address = TempData.Peek("Checkout_Address")?.ToString();
        var note = TempData.Peek("Checkout_Note")?.ToString();
        var paymentMethod = TempData.Peek("Checkout_PaymentMethod")?.ToString();
        var couponCode = TempData.Peek("Checkout_CouponCode")?.ToString();
        var shippingMethod = TempData.Peek("Checkout_ShippingMethod")?.ToString();

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(address))
        {
            return BadRequest(new { success = false, message = "Thiếu thông tin giao hàng. Vui lòng quay lại bước 1." });
        }

        if (string.IsNullOrWhiteSpace(paymentMethod) || !AllowedPaymentMethods.Contains(paymentMethod))
        {
            return BadRequest(new { success = false, message = "Phương thức thanh toán không hợp lệ." });
        }

        // Xác định chế độ trước khi tạo đơn, tránh xóa giỏ hàng khi người dùng chọn Mua ngay.
        bool isBuyNow = IsBuyNowCheckout(user.MaNguoiDung);
        if (IsExpectedBuyNowLost(user.MaNguoiDung))
            return BadRequest(new { success = false, message = "Phiên Mua ngay đã hết hạn. Vui lòng chọn lại sản phẩm." });

        var strategy = _db.Database.CreateExecutionStrategy();

        try
        {
            var result = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                try
                {
                    // 1. Lấy danh sách thanh toán (Mua ngay hoặc giỏ hàng).
                    var cartItems = await GetCheckoutItemsAsync(user.MaNguoiDung);
                    if (cartItems.Count == 0)
                    {
                        return new { Success = false, StatusCode = 400, Data = (object)new { success = false, message = "Không có sản phẩm hợp lệ để đặt hàng." } };
                    }

                    // 2. Kiểm tra tồn kho
                    if (cartItems.Any(x => x.SoLuong <= 0 ||
                        x.SoLuong > x.MaBienTheNavigation.SoLuong ||
                        x.MaBienTheNavigation.TrangThai != "Còn hàng" ||
                        x.MaBienTheNavigation.Gia <= 0 ||
                        x.MaBienTheNavigation.MaSanPhamNavigation?.TrangThaiSanPham == "Ẩn"))
                    {
                        return new { Success = false, StatusCode = 400, Data = (object)new { success = false, message = "Một số sản phẩm không còn đủ số lượng trong kho." } };
                    }

                    // 3. Tính toán số tiền
                    var subtotal = cartItems.Sum(x => x.SoLuong * (x.MaBienTheNavigation.Gia));

                    // Phí vận chuyển theo phương thức đã chọn
                    decimal shipping;
                    bool isExpress = shippingMethod == "EXPRESS";
                    if (isExpress)
                    {
                        shipping = ExpressShippingFee;
                    }
                    else
                    {
                        shipping = subtotal >= 500000m ? 0m : StandardShippingFee;
                    }

                    var coupon = await FindValidCouponAsync(couponCode, subtotal);
                    if (!string.IsNullOrWhiteSpace(couponCode) && coupon == null)
                    {
                        return new { Success = false, StatusCode = 400, Data = (object)new { success = false, message = "Mã giảm giá không còn hợp lệ. Vui lòng quay lại bước thanh toán để chọn lại hoặc bỏ mã." } };
                    }
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
                        PhuongThucVanChuyen = isExpress ? "Giao hàng nhanh" : "Giao hàng tiêu chuẩn",
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

                    // 7. Chỉ xóa giỏ sau khi đặt hàng từ giỏ. "Mua ngay" không thay đổi giỏ.
                    if (!isBuyNow)
                        _db.ChiTietGioHangs.RemoveRange(cartItems);

                    // 8. Đặt tên phương thức hiển thị chuẩn
                    var paymentName = paymentMethod switch
                    {
                        "BANK_TRANSFER" => "Chuyển khoản ngân hàng (QR)",
                        "WEB3" => "Ví Web3 (Crypto)",
                        _ => "Thanh toán khi nhận hàng (COD)"
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

                    // =========================================================
                    // 9. BỔ SUNG TỰ ĐỘNG TẠO HÓA ĐƠN ĐỂ HIỂN THỊ BÊN TRANG NHÂN VIÊN
                    // =========================================================
                    var hoaDonMoi = new HoaDon
                    {
                        MaDonHang = order.MaDonHang,
                        NgayLapHoaDon = DateTime.Now,
                        TongTien = total,
                        TrangThaiHoaDon = paymentMethod == "COD" ? "Đã lập" : "Chờ thanh toán"
                    };
                    _db.HoaDons.Add(hoaDonMoi);
                    // =========================================================

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
            TempData.Remove("Checkout_ShippingMethod");
            TempData.Remove(BuyNowExpectedKey);
            if (isBuyNow) ClearBuyNowSession();
            else HttpContext.Session.Remove("Cart_CouponCode");

            return Json(result.Data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PLACE ORDER ERROR: {ex}");
            return StatusCode(500, new
            {
                success = false,
                message = "Không thể tạo đơn hàng. Vui lòng thử lại."
            });
        }
    }

    // =========================================================
    // STEP 4: TRANG THÀNH CÔNG & THÔNG TIN THANH TOÁN
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return RedirectToAction("Login", "Auth");

        var order = await _db.DonHangs
            .Include(x => x.ThanhToans)
            .Include(x => x.HoaDon)
            .Include(x => x.ChiTietDonHangs)
                .ThenInclude(x => x.MaBienTheNavigation)
                    .ThenInclude(x => x.MaSanPhamNavigation)
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaNguoiDung == user.MaNguoiDung);

        if (order == null) return NotFound();

        var payment = order.ThanhToans.OrderByDescending(x => x.NgayTao).FirstOrDefault();
        var transferContent = $"EMUA{order.MaDonHang:D6}";
        var orderTotal = order.TongTien;

        string mbAccountNo = "0963453170";
        string mbAccountName = Uri.EscapeDataString("PHAN DANG PHUONG ANH");
        var bankQrUrl = $"https://img.vietqr.io/image/MB-{mbAccountNo}-compact2.png?amount={(long)orderTotal}&addInfo={transferContent}&accountName={mbAccountName}";
        string web3WalletAddress = "0x71C7656EC7ab88b098defB751B7401B5f6d8976F";

        ViewBag.BankQrUrl = bankQrUrl;
        ViewBag.Web3WalletAddress = web3WalletAddress;
        ViewBag.TransferContent = transferContent;

        var realPaymentStatus = payment?.TrangThai ?? "Chờ xác nhận";
        if (order.TrangThaiDonHang == "Đã hoàn thành" || order.TrangThaiDonHang == "Đã giao")
        {
            realPaymentStatus = "Đã thanh toán";
        }

        var viewModel = new CheckoutSuccessViewModel
        {
            OrderId = order.MaDonHang,
            TotalAmount = (decimal)orderTotal,
            PaymentMethod = payment?.PhuongThuc ?? "COD",
            PaymentStatus = realPaymentStatus,
            FullName = order.HoTenNhanHang ?? string.Empty,
            PhoneNumber = order.SoDienThoaiNhanHang ?? string.Empty,
            Address = order.DiaChiNhanHang ?? string.Empty,
            BankQrUrl = bankQrUrl,
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

        // Truyền thông tin hóa đơn nếu tồn tại thực sự trong CSDL
        if (order.HoaDon != null)
        {
            ViewBag.InvoiceId = order.HoaDon.MaHoaDon;
        }

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
        var coupon = await FindValidCouponAsync(request?.CouponCode, checkout.Subtotal);
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
        var cartItems = await GetCheckoutItemsAsync(user.MaNguoiDung);
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

        // Xác định phương thức vận chuyển từ TempData hoặc mặc định
        var shippingMethod = TempData.Peek("Checkout_ShippingMethod")?.ToString();
        model.ShippingMethod = shippingMethod == "EXPRESS" ? "EXPRESS" : "STANDARD";

        // Tính phí vận chuyển theo phương thức đã chọn
        if (model.ShippingMethod == "EXPRESS")
        {
            model.ShippingFee = ExpressShippingFee;
        }
        else
        {
            // Tiêu chuẩn: miễn phí nếu đơn >= 500k, ngược lại phí chuẩn
            model.ShippingFee = model.Subtotal <= 0 || model.Subtotal >= 500000m ? 0m : StandardShippingFee;
        }

        var couponCode = TempData.Peek("Checkout_CouponCode")?.ToString();
        var coupon = await FindValidCouponAsync(couponCode, model.Subtotal);
        if (coupon != null)
        {
            model.CouponCode = coupon.MaCode;
            model.Discount = CalculateDiscount(coupon, model.Subtotal);
        }
        return model;
    }

    private bool IsExpectedBuyNowLost(int userId) =>
        string.Equals(TempData.Peek(BuyNowExpectedKey)?.ToString(), "True", StringComparison.OrdinalIgnoreCase) &&
        !IsBuyNowCheckout(userId);

    private bool IsBuyNowCheckout(int userId) =>
        HttpContext.Session.GetInt32(BuyNowUserKey) == userId &&
        HttpContext.Session.GetInt32(BuyNowVariantKey) is > 0 &&
        HttpContext.Session.GetInt32(BuyNowQuantityKey) is > 0;

    private void ClearBuyNowSession()
    {
        HttpContext.Session.Remove(BuyNowUserKey);
        HttpContext.Session.Remove(BuyNowVariantKey);
        HttpContext.Session.Remove(BuyNowQuantityKey);
    }

    // Giữ giỏ hàng nguyên trạng khi Mua ngay; sử dụng sản phẩm ảo chỉ để tính và tạo đơn.
    private async Task<List<ChiTietGioHang>> GetCheckoutItemsAsync(int userId)
    {
        if (IsBuyNowCheckout(userId))
        {
            var variantId = HttpContext.Session.GetInt32(BuyNowVariantKey)!.Value;
            var quantity = HttpContext.Session.GetInt32(BuyNowQuantityKey)!.Value;

            var variant = await _db.BienTheSanPhams
                .Include(x => x.MaSanPhamNavigation)
                .FirstOrDefaultAsync(x => x.MaBienThe == variantId);

            if (variant == null || variant.TrangThai != "Còn hàng" ||
                variant.MaSanPhamNavigation.TrangThaiSanPham == "Ẩn" ||
                variant.SoLuong < quantity)
            {
                return new List<ChiTietGioHang>();
            }

            return new List<ChiTietGioHang>
            {
                new ChiTietGioHang
                {
                    MaNguoiDung = userId,
                    MaBienThe = variantId,
                    SoLuong = quantity,
                    MaBienTheNavigation = variant
                }
            };
        }

        // Nếu session mua ngay không khớp tài khoản đang dùng thì hủy session cũ.
        if (HttpContext.Session.GetInt32(BuyNowVariantKey).HasValue)
            ClearBuyNowSession();

        return await GetCartItemsAsync(userId);
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
        var coupon = await _db.KhuyenMais.FirstOrDefaultAsync(x => x.MaCode.Trim().ToLower() == code.Trim().ToLower());
        if (coupon == null || !coupon.TrangThai || coupon.SoLuong <= 0 || coupon.NgayBatDau > now || coupon.NgayKetThuc < now || (coupon.GiaTriDonHangToiThieu) > subtotal)
            return null;
        return coupon;
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        ShoppingRules.Discount(coupon, subtotal);
}