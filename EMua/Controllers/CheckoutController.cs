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
    private const decimal StandardShippingFee = OrderRules.StandardFee;
    private const decimal ExpressShippingFee = OrderRules.ExpressFee;
    private const string BuyNowUserKey = "Checkout_BuyNowUserId";
    private const string BuyNowVariantKey = "Checkout_BuyNowVariantId";
    private const string BuyNowQuantityKey = "Checkout_BuyNowQuantity";
    private const string BuyNowExpectedKey = "Checkout_BuyNowExpected";
    private readonly EMuaDbContext _db;

    private readonly IConfiguration _configuration;
    public CheckoutController(EMuaDbContext db, IConfiguration configuration) { _db = db; _configuration = configuration; }

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
            TempData.Remove("Checkout_RequestKey");
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

        if ((model.Note?.Trim().Length ?? 0) > 255) ModelState.AddModelError(nameof(model.Note), "Ghi chú tối đa 255 ký tự.");
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.FullName) ||
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
        if (TempData.Peek("Checkout_RequestKey") == null) TempData["Checkout_RequestKey"] = Guid.NewGuid().ToString("N");
        ViewBag.CheckoutRequestKey = TempData.Peek("Checkout_RequestKey");
        TempData["Checkout_ReviewTotal"] = model.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TempData["Checkout_ReviewItems"] = Fingerprint(model.Items.Select(x => (x.VariantId, (long)x.Quantity, x.UnitPrice)));
        TempData.Keep();

        return View(model);
    }

    // =========================================================
    // STEP 3.5: THỰC THI CHỐT ĐƠN (POST)
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder([FromForm] string? checkoutToken)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized(new { success = false, message = "Phiên đăng nhập không hợp lệ hoặc tài khoản đã khóa." });
        if (!Guid.TryParseExact(checkoutToken, "N", out _)) return BadRequest(new { success = false, message = "Vui lòng tải lại trang xác nhận đặt hàng." });
        var fullName = TempData.Peek("Checkout_FullName")?.ToString()?.Trim();
        var phone = TempData.Peek("Checkout_PhoneNumber")?.ToString()?.Trim();
        var address = TempData.Peek("Checkout_Address")?.ToString()?.Trim();
        var note = TempData.Peek("Checkout_Note")?.ToString()?.Trim();
        var method = TempData.Peek("Checkout_PaymentMethod")?.ToString();
        var couponCode = TempData.Peek("Checkout_CouponCode")?.ToString();
        var shippingMethod = TempData.Peek("Checkout_ShippingMethod")?.ToString();
        var expectedKey = TempData.Peek("Checkout_RequestKey")?.ToString();
        var reviewTotalText = TempData.Peek("Checkout_ReviewTotal")?.ToString();
        var reviewItems = TempData.Peek("Checkout_ReviewItems")?.ToString();
        var isBuyNow = IsBuyNowCheckout(user.MaNguoiDung);
        var lostBuyNow = IsExpectedBuyNowLost(user.MaNguoiDung);
        try
        {
            var orderId = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                // Cùng một tài khoản chỉ chốt một yêu cầu tại một thời điểm.
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({739451}, {user.MaNguoiDung})");
                var existing = await _db.DonHangs.AsNoTracking().FirstOrDefaultAsync(x => x.MaNguoiDung == user.MaNguoiDung && x.YeuCauDatHang == checkoutToken);
                if (existing != null) return existing.MaDonHang;
                if (expectedKey != checkoutToken || lostBuyNow) throw new ArgumentException("Phiên đặt hàng đã thay đổi hoặc hết hạn. Vui lòng kiểm tra lại giỏ hàng.");
                if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100 || string.IsNullOrWhiteSpace(address) || address.Length > 255 ||
                    string.IsNullOrWhiteSpace(phone) || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^0[35789]\d{8}$") || note?.Length > 255)
                    throw new ArgumentException("Thông tin nhận hàng không hợp lệ. Kiểm tra họ tên, địa chỉ, số điện thoại và ghi chú tối đa 255 ký tự.");
                if (method == null || !AllowedPaymentMethods.Contains(method) || shippingMethod is not ("STANDARD" or "EXPRESS"))
                    throw new ArgumentException("Vui lòng chọn lại phương thức giao hàng và thanh toán.");
                if (!await _db.NguoiDungs.AnyAsync(x => x.MaNguoiDung == user.MaNguoiDung && x.TrangThai)) throw new ArgumentException("Tài khoản đã bị khóa.");
                var cart = await GetCheckoutItemsAsync(user.MaNguoiDung);
                if (cart.Count == 0 || cart.Any(x => x.SoLuong <= 0 || x.SoLuong > 100000 || x.MaBienTheNavigation.Gia <= 0))
                    throw new ArgumentException("Giỏ hàng không có sản phẩm hợp lệ.");
                var lines = cart.GroupBy(x => x.MaBienThe).Select(g => new { Id = g.Key, Qty = g.Sum(x => (long)x.SoLuong), Price = g.First().MaBienTheNavigation.Gia }).OrderBy(x => x.Id).ToList();
                if (lines.Any(x => x.Qty > 100000)) throw new ArgumentException("Số lượng một mặt hàng tối đa 100.000.");
                if (reviewItems != Fingerprint(lines.Select(x => (x.Id, x.Qty, x.Price)))) throw new ArgumentException("Giỏ hàng hoặc đơn giá đã thay đổi. Tải lại trang xác nhận trước khi đặt.");
                var subtotal = lines.Sum(x => x.Qty * x.Price);
                var coupon = await FindValidCouponAsync(couponCode, subtotal);
                if (!string.IsNullOrWhiteSpace(couponCode) && coupon == null) throw new ArgumentException("Mã giảm giá không còn hợp lệ. Vui lòng chọn lại.");
                var totals = OrderRules.Calculate(subtotal, shippingMethod == "EXPRESS", coupon?.LoaiGiamGia, coupon?.GiaTriGiam ?? 0m, coupon?.GiaTriDonHangToiThieu ?? 0m);
                if (!decimal.TryParse(reviewTotalText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var reviewedTotal) || reviewedTotal != totals.Total)
                    throw new ArgumentException("Giá hoặc ưu đãi đã thay đổi. Tải lại trang xác nhận để xem tổng tiền mới trước khi đặt.");
                if (coupon != null)
                {
                    var now = OrderRules.VietnamNow();
                    var reserved = await _db.KhuyenMais.Where(x => x.MaKhuyenMai == coupon.MaKhuyenMai && x.TrangThai &&
                        (x.SoLuong == null || x.SoLuong > 0) && (x.NgayBatDau == null || x.NgayBatDau <= now) && (x.NgayKetThuc == null || x.NgayKetThuc >= now) &&
                        x.GiaTriGiam == coupon.GiaTriGiam && x.LoaiGiamGia == coupon.LoaiGiamGia && x.GiaTriDonHangToiThieu == coupon.GiaTriDonHangToiThieu)
                        .ExecuteUpdateAsync(set => set.SetProperty(x => x.SoLuong, x => x.SoLuong == null ? (int?)null : x.SoLuong - 1));
                    if (reserved != 1) throw new ArgumentException("Mã giảm giá vừa hết lượt hoặc thay đổi. Vui lòng kiểm tra lại.");
                }
                foreach (var line in lines)
                {
                    var qty = (int)line.Qty;
                    var changed = await _db.BienTheSanPhams.Where(v => v.MaBienThe == line.Id && v.SoLuong >= qty && v.TrangThai == "Còn hàng" &&
                        v.Gia == line.Price && v.MaSanPhamNavigation.TrangThaiSanPham != "Ẩn")
                        .ExecuteUpdateAsync(set => set.SetProperty(v => v.SoLuong, v => v.SoLuong - qty)
                            .SetProperty(v => v.TrangThai, v => v.SoLuong - qty == 0 ? "Hết hàng" : v.TrangThai));
                    if (changed != 1) throw new ArgumentException("Sản phẩm vừa hết hàng, đổi giá hoặc ngừng bán. Chưa tạo đơn; hãy kiểm tra lại giỏ.");
                }
                var order = new DonHang { MaNguoiDung = user.MaNguoiDung, NgayDat = OrderRules.VietnamNow(), TongTien = totals.Total,
                    TrangThaiDonHang = method == "COD" || totals.Total == 0 ? OrderRules.Pending : OrderRules.AwaitingPayment,
                    HoTenNhanHang = fullName, SoDienThoaiNhanHang = phone, DiaChiNhanHang = address, GhiChu = note,
                    PhuongThucVanChuyen = shippingMethod == "EXPRESS" ? "Giao hàng nhanh" : "Giao hàng tiêu chuẩn",
                    MaKhuyenMai = coupon?.MaKhuyenMai, PhiVanChuyenDaChot = totals.Shipping, TienGiamGiaDaChot = totals.Discount,
                    LoaiKhuyenMaiDaChot = coupon?.LoaiGiamGia, GiaTriKhuyenMaiDaChot = coupon?.GiaTriGiam ?? 0m,
                    DonToiThieuKhuyenMaiDaChot = coupon?.GiaTriDonHangToiThieu ?? 0m, YeuCauDatHang = checkoutToken };
                _db.DonHangs.Add(order); await _db.SaveChangesAsync();
                foreach (var line in lines) _db.ChiTietDonHangs.Add(new ChiTietDonHang { MaDonHang = order.MaDonHang, MaBienThe = line.Id,
                    SoLuong = (int)line.Qty, DonGia = line.Price, ThanhTien = OrderRules.Money(line.Price * line.Qty) });
                _db.ThanhToans.Add(new ThanhToan { MaDonHang = order.MaDonHang, SoTien = totals.Total,
                    PhuongThuc = method == "COD" ? "Thanh toán khi nhận hàng (COD)" : method == "WEB3" ? "Ví Web3 (Crypto)" : "Chuyển khoản ngân hàng (QR)",
                    TrangThai = totals.Total == 0 ? OrderRules.Paid : method == "COD" ? "Chưa thanh toán" : OrderRules.AwaitingPayment,
                    MaGiaoDich = $"EMUA{order.MaDonHang:D6}", NgayTao = OrderRules.VietnamNow() });
                _db.LichSuVanChuyens.Add(new LichSuVanChuyen { MaDonHang = order.MaDonHang, TrangThai = order.TrangThaiDonHang,
                    MoTa = "Đã chốt đơn, lưu phí giao và điều kiện giảm giá; giữ tồn kho.", ThoiGian = OrderRules.VietnamNow() });
                if (!isBuyNow) _db.ChiTietGioHangs.RemoveRange(cart);
                await _db.SaveChangesAsync(); await tx.CommitAsync(); return order.MaDonHang;
            });
            foreach (var key in new[] { "Checkout_FullName", "Checkout_PhoneNumber", "Checkout_Address", "Checkout_Note", "Checkout_PaymentMethod", "Checkout_CouponCode", "Checkout_ShippingMethod", "Checkout_RequestKey", "Checkout_ReviewTotal", "Checkout_ReviewItems", BuyNowExpectedKey }) TempData.Remove(key);
            if (isBuyNow) ClearBuyNowSession(); else HttpContext.Session.Remove("Cart_CouponCode");
            return Json(new { success = true, orderId, redirectUrl = Url.Action("Success", "Checkout", new { id = orderId }) });
        }
        catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<CheckoutController>>().LogError(ex, "Lỗi chốt đơn cho người dùng {UserId}", user.MaNguoiDung);
            return StatusCode(500, new { success = false, message = "Chưa xác định được kết quả đặt hàng. Kiểm tra mục Đơn hàng trước khi tạo yêu cầu mới." });
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
        var bankQrUrl = $"https://img.vietqr.io/image/MB-{mbAccountNo}-compact2.webp?amount={(long)orderTotal}&addInfo={transferContent}&accountName={mbAccountName}";
        string web3WalletAddress = _configuration["Payments:Web3WalletAddress"] ?? "";
        ViewBag.Web3Network = _configuration["Payments:Web3Network"] ?? "";

        ViewBag.BankQrUrl = bankQrUrl;
        ViewBag.Web3WalletAddress = web3WalletAddress;
        ViewBag.TransferContent = transferContent;

        var realPaymentStatus = payment?.TrangThai ?? "Chờ xác nhận";
        if (OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.Cancelled || payment?.TrangThai == OrderRules.PaymentReported) return RedirectToAction("Details", "Order", new { id });

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

    private static string Fingerprint(IEnumerable<(int Id, long Quantity, decimal Price)> items) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join(";", items.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Quantity}:{x.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)}")))));

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
        return int.TryParse(idText, out var id) ? await _db.NguoiDungs.FirstOrDefaultAsync(x => x.MaNguoiDung == id && x.TrangThai) : null;
    }

    private async Task<KhuyenMai?> FindValidCouponAsync(string? code, decimal subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var now = OrderRules.VietnamNow();
        var coupon = await _db.KhuyenMais.AsNoTracking().FirstOrDefaultAsync(x => x.MaCode.Trim().ToLower() == code.Trim().ToLower());
        if (coupon == null || !coupon.TrangThai || coupon.GiaTriGiam <= 0 || coupon.SoLuong <= 0 || coupon.NgayBatDau > now || coupon.NgayKetThuc < now || (coupon.GiaTriDonHangToiThieu) > subtotal)
            return null;
        return coupon;
    }

    private static decimal CalculateDiscount(KhuyenMai coupon, decimal subtotal) =>
        OrderRules.Money(Math.Clamp(OrderRules.IsPercent(coupon.LoaiGiamGia) ? subtotal * coupon.GiaTriGiam / 100m : coupon.GiaTriGiam, 0m, subtotal));
}