using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EMua.Controllers;

[Authorize]
public class OrderController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly OrderWorkflowService _workflow;

    public OrderController(EMuaDbContext db, IWebHostEnvironment env, OrderWorkflowService workflow)
    {
        _db = db;
        _env = env;
        _workflow = workflow;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId();

        var orders = await _db.DonHangs
            .AsNoTracking()
            .Where(x => x.MaNguoiDung == userId)
            .OrderByDescending(x => x.NgayDat)
            .Select(x => new OrderListItemViewModel
            {
                OrderId = x.MaDonHang,
                OrderedAt = x.NgayDat,
                Total = x.TongTien ?? 0,
                Status = x.TrangThaiDonHang ?? "Chờ xử lý",
                ItemCount = x.ChiTietDonHangs.Sum(i => i.SoLuong),
                Items = x.ChiTietDonHangs.Select(i => new OrderItemSummaryViewModel
                {
                    ProductId = i.MaBienTheNavigation.MaSanPham,
                    ProductName = i.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",

                    ProductImageUrl = !string.IsNullOrEmpty(i.MaBienTheNavigation.HinhAnh)
                        ? i.MaBienTheNavigation.HinhAnh
                        : i.MaBienTheNavigation.MaSanPhamNavigation.SanPhamHinhAnhs.Select(a => a.DuongDanAnh).FirstOrDefault() ?? "/images/no-image.png",

                    Quantity = i.SoLuong,
                    Price = i.DonGia
                }).ToList()
            }).ToListAsync();

        foreach (var item in orders) item.Status = OrderRules.Normalize(item.Status);
        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.DonHangs.AsNoTracking()
            .Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Include(x => x.ThanhToans).Include(x => x.LichSuVanChuyens)
            .Include(x => x.HoaDon).ThenInclude(x => x!.ChiTietHoaDons)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaNguoiDung == CurrentUserId());

        if (order == null) return NotFound();

        var payment = order.ThanhToans.OrderByDescending(x => x.NgayTao).FirstOrDefault();

        var reviewedProductIds = await _db.DanhGiaSanPhams
            .AsNoTracking()
            .Where(r => r.MaDonHang == id && r.MaSanPham.HasValue)
            .Select(r => r.MaSanPham!.Value)
            .Distinct()
            .ToListAsync();

        return View("~/Views/Order/Details.cshtml", new OrderDetailsViewModel
        {
            OrderId = order.MaDonHang,
            OrderedAt = order.NgayDat,
            Total = order.TongTien ?? 0,
            Status = OrderRules.Normalize(order.TrangThaiDonHang),
            Subtotal = order.ChiTietDonHangs.Sum(x => x.ThanhTien),
            ShippingFee = order.PhiVanChuyenDaChot,
            Discount = order.TienGiamGiaDaChot,
            CanEdit = OrderWorkflowService.EditBlockedReason(order) == null,
            CanCancel = OrderRules.IsPending(order.TrangThaiDonHang),
            EditBlockedReason = OrderWorkflowService.EditBlockedReason(order),
            RecipientName = order.HoTenNhanHang ?? string.Empty,
            RecipientPhone = order.SoDienThoaiNhanHang ?? string.Empty,
            ShippingAddress = order.DiaChiNhanHang ?? string.Empty,
            PaymentMethod = payment?.PhuongThuc ?? "COD",
            PaymentStatus = payment?.TrangThai ?? "Chưa xác định",
            EstimatedDeliveryDate = order.NgayDuKienGiao?.AddHours(7),
            DeliveryProofImageUrl = order.AnhXacNhanGiao,
            Items = order.ChiTietDonHangs.Select(x => new CartItemViewModel
            {
                ProductId = x.MaBienTheNavigation.MaSanPham,
                VariantId = x.MaBienThe,
                ProductName = x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
                VariantLabel = string.Join(" / ", new[] { x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan }.Where(v => !string.IsNullOrWhiteSpace(v))),
                ImageUrl = x.MaBienTheNavigation.HinhAnh,
                UnitPrice = x.DonGia,
                Quantity = x.SoLuong
            }).ToList(),
            StatusHistory = order.LichSuVanChuyens.OrderBy(x => x.ThoiGian).Select(x => new OrderStatusViewModel
            {
                Status = x.TrangThai,
                Description = x.MoTa,
                Time = x.ThoiGian
            }).ToList(),

            ReviewedProductIds = reviewedProductIds,
            IsOrderReviewed = reviewedProductIds.Any()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await _workflow.CancelAsync(id, CurrentUserId());
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var result = await _workflow.ReportPaymentAsync(id, CurrentUserId());
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReturn(int orderId, List<int>? selectedVariantIds,
        string? reason, string? description, string? contactPhone, List<IFormFile>? evidenceImages)
    {
        var paths = new List<string>();
        try
        {
            if (evidenceImages?.Count > 5) throw new ArgumentException("Tối đa 5 ảnh bằng chứng.");
            if (!await _db.DonHangs.AnyAsync(x => x.MaDonHang == orderId && x.MaNguoiDung == CurrentUserId())) return NotFound();
            foreach (var image in evidenceImages ?? new List<IFormFile>())
                paths.Add(await OrderEvidenceStorage.SaveAsync(_env.WebRootPath, image, "returns"));
            var result = await _workflow.RequestReturnAsync(orderId, CurrentUserId(), (selectedVariantIds ?? new()).Distinct().ToArray(),
                reason ?? "", description ?? "", contactPhone ?? "", paths);
            if (!result.ProofUsed && !result.Uncertain) foreach (var path in paths) OrderEvidenceStorage.Delete(_env.WebRootPath, path);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
        }
        catch (ArgumentException ex)
        {
            foreach (var path in paths) OrderEvidenceStorage.Delete(_env.WebRootPath, path);
            TempData["Error"] = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var path in paths) OrderEvidenceStorage.Delete(_env.WebRootPath, path);
            TempData["Error"] = "Không lưu được ảnh bằng chứng. Kiểm tra thư mục tải ảnh rồi thử lại.";
        }
        return RedirectToAction(nameof(Details), new { id = orderId });
    }

    public class UpdateOrderItemRequest
    {
        public int OrderId { get; set; }
        public int VariantId { get; set; }
        public int Quantity { get; set; }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOrderItem([FromBody] UpdateOrderItemRequest? req)
    {
        if (req == null || !ModelState.IsValid) return BadRequest(new { success = false, message = "Dữ liệu số lượng không hợp lệ." });
        var result = await _workflow.EditItemAsync(req.OrderId, CurrentUserId(), req.VariantId, req.Quantity);
        return Json(new { success = result.Success, message = result.Message, orderTotal = result.Totals?.Total,
            subtotal = result.Totals?.Subtotal, shippingFee = result.Totals?.Shipping, discount = result.Totals?.Discount, orderStatus = result.Status });
    }

    // =========================================================
    // CHAT ĐỔI TRẢ - CHỈ LƯU TRONG RAM, KHÔNG LƯU DATABASE
    // Khách phải chủ động bấm "Bắt đầu trò chuyện" mới tạo phiên.
    // Khi khách rời trang/đóng phiên, JS gọi EndOrderChat và toàn bộ lịch sử bị xoá.
    // =========================================================

    [HttpPost]
    [Route("Order/StartOrderChat")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> StartOrderChat([FromBody] StartChatRequest req)
    {
        var orderId = req.OrderId;
        var userId = CurrentUserId();
        var order = await _db.DonHangs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaDonHang == orderId && x.MaNguoiDung == userId);

        if (order == null)
            return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

        var allowed = new[] { "Yêu cầu đổi trả", "Đang đổi trả", "Đã nhận đổi trả" };
        if (!allowed.Contains(order.TrangThaiDonHang ?? ""))
            return Json(new { success = false, message = "Đơn hàng hiện không trong phiên hỗ trợ đổi trả." });

        var session = OrderChatSessionStore.Start(orderId, userId)!;
        return Json(new { success = true, active = true, messageCount = session.GetMessages().Count });
    }

    [HttpGet]
    [Route("Order/GetOrderChatStatus")]
    public async Task<IActionResult> GetOrderChatStatus(int orderId)
    {
        var userId = CurrentUserId();
        var ownsOrder = await _db.DonHangs.AsNoTracking()
            .AnyAsync(x => x.MaDonHang == orderId && x.MaNguoiDung == userId);

        if (!ownsOrder) return NotFound();

        var active = OrderChatSessionStore.TryGet(orderId, out var session)
                     && session!.CustomerId == userId;
        return Json(new { active });
    }

    [HttpGet]
    [Route("Order/GetOrderMessages")]
    public async Task<IActionResult> GetOrderMessages(int orderId)
    {
        var userId = CurrentUserId();
        var ownsOrder = await _db.DonHangs.AsNoTracking()
            .AnyAsync(x => x.MaDonHang == orderId && x.MaNguoiDung == userId);
        if (!ownsOrder) return NotFound();

        if (!OrderChatSessionStore.TryGet(orderId, out var session) || session!.CustomerId != userId)
            return Json(Array.Empty<object>());

        OrderChatSessionStore.Touch(session);
        return Json(session.GetMessages());
    }

    [HttpPost]
    [Route("Order/SendOrderMessage")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SendOrderMessage([FromBody] SendMessageRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Message))
            return Json(new { success = false, message = "Nội dung tin nhắn không được để trống." });

        var userId = CurrentUserId();
        var ownsOrder = await _db.DonHangs.AsNoTracking()
            .AnyAsync(x => x.MaDonHang == req.OrderId && x.MaNguoiDung == userId);
        if (!ownsOrder)
            return Json(new { success = false, message = "Bạn không có quyền nhắn tin cho đơn hàng này." });

        if (!OrderChatSessionStore.TryGet(req.OrderId, out var session) || session!.CustomerId != userId)
            return Json(new { success = false, message = "Bạn chưa bắt đầu phiên trò chuyện." });

        var text = req.Message.Trim();
        session.AddMessage("Bạn", text, false);
        OrderChatSessionStore.Touch(session);

        return Json(new { success = true, senderName = "Bạn", message = text, isStaff = false, time = DateTime.Now.ToString("HH:mm") });
    }

    [HttpPost]
    [Route("Order/EndOrderChat")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> EndOrderChat([FromBody] EndChatRequest req)
    {
        var userId = CurrentUserId();
        var ownsOrder = await _db.DonHangs.AsNoTracking()
            .AnyAsync(x => x.MaDonHang == req.OrderId && x.MaNguoiDung == userId);
        if (!ownsOrder) return NotFound();

        OrderChatSessionStore.End(req.OrderId, userId);
        return Json(new { success = true });
    }

    public class SendMessageRequest
    {
        public int OrderId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class EndChatRequest
    {
        public int OrderId { get; set; }
    }

    public class StartChatRequest
    {
        public int OrderId { get; set; }
    }

    private int CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    // XEM / IN PHIẾU TẠM TÍNH (PROFORMA INVOICE) CHO KHÁCH HÀNG HOẶC STAFF
    [HttpGet]
    public async Task<IActionResult> ProformaInvoice(int id)
    {
        var userId = CurrentUserId();
        var order = await _db.DonHangs.AsNoTracking()
            .Include(d => d.ChiTietDonHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
            .Include(d => d.MaKhuyenMaiNavigation)
            .Include(d => d.ThanhToans)
            .Include(d => d.HoaDon)
             .FirstOrDefaultAsync(d => d.MaDonHang == id);
        if (order != null && order.MaNguoiDung != userId && !await _db.NguoiDungs.AsNoTracking().AnyAsync(u => u.MaNguoiDung == userId && u.TrangThai &&
            new[] { "Admin", "Quản trị viên", "NhanVien", "Nhân viên" }.Contains(u.MaQuyenNavigation.TenQuyen))) return Forbid();

        if (order == null) return NotFound("Không tìm thấy đơn hàng hoặc bạn không có quyền truy cập.");

        var payment = order.ThanhToans.OrderByDescending(p => p.NgayTao).FirstOrDefault();
        decimal itemsGrossTotal = 0m;
        decimal itemsNetTotal = 0m;
        decimal estimatedVatTotal = 0m;
        var itemsList = new List<ProformaInvoiceItemViewModel>();

        foreach (var ct in order.ChiTietDonHangs)
        {
            var sp = ct.MaBienTheNavigation?.MaSanPhamNavigation;
            var bt = ct.MaBienTheNavigation;
            var catName = sp?.MaDanhMucNavigation?.TenDanhMuc ?? "";
            var prodName = sp?.TenSanPham ?? "Sản phẩm";

            var vatRule = VatConfigService.DetermineVatRate(catName, prodName, order.NgayDat ?? DateTime.Now);
            decimal grossTotal = ct.ThanhTien;
            decimal vatAmt = VatConfigService.CalculateVatAmount(grossTotal, vatRule.RatePercent);
            decimal netTotal = grossTotal - vatAmt;
            decimal netUnit = Math.Round(netTotal / Math.Max(1, ct.SoLuong), 2, MidpointRounding.AwayFromZero);

            itemsGrossTotal += grossTotal;
            itemsNetTotal += netTotal;
            estimatedVatTotal += vatAmt;

            itemsList.Add(new ProformaInvoiceItemViewModel
            {
                ProductId = sp?.MaSanPham ?? 0,
                VariantId = ct.MaBienThe,
                ProductName = prodName,
                VariantLabel = $"{bt?.MauSac ?? ""} {bt?.PhienBan ?? ""}".Trim(),
                ImageUrl = bt?.HinhAnh,
                Quantity = ct.SoLuong,
                GrossUnitPrice = ct.DonGia,
                NetUnitPrice = netUnit,
                VatRate = vatRule.RatePercent,
                LineVatAmount = vatAmt,
                LineTotal = grossTotal
            });
        }

        var finalTotal = order.TongTien ?? 0m;
        decimal? fee = order.PhiVanChuyenDaChot;
        decimal? discount = order.TienGiamGiaDaChot;
        if (!fee.HasValue || !discount.HasValue)
        {
            try { OrderWorkflowService.ValidateExistingPricing(order); fee = OrderRules.ShippingFee(itemsGrossTotal, OrderRules.IsExpress(order.PhuongThucVanChuyen)); discount = 0m; }
            catch (ArgumentException) { return Conflict("Đơn cũ thiếu dữ liệu phí giao và giảm giá đã chốt. Cần nhân viên đối chiếu trước khi in phiếu chi tiết tiền."); }
        }
        decimal voucherDiscount = discount.Value;
        decimal shippingFee = fee.Value;
        string? voucherCode = order.MaKhuyenMaiNavigation?.MaCode;

        var vm = new ProformaInvoiceViewModel
        {
            OrderId = order.MaDonHang,
            OrderDate = order.NgayDat ?? DateTime.Now,
            OrderStatus = OrderRules.Normalize(order.TrangThaiDonHang),
            CustomerName = order.HoTenNhanHang ?? "Khách hàng",
            CustomerPhone = order.SoDienThoaiNhanHang ?? "",
            ShippingAddress = order.DiaChiNhanHang ?? "",
            PaymentMethod = payment?.PhuongThuc ?? "Thanh toán khi nhận hàng (COD)",
            PaymentStatus = payment?.TrangThai ?? "Chưa xác định",
            ShippingMethod = order.PhuongThucVanChuyen ?? "Giao hàng tiêu chuẩn",
            Note = order.GhiChu,
            ItemsGrossTotal = itemsGrossTotal,
            ItemsNetTotal = itemsNetTotal,
            EstimatedVatTotal = estimatedVatTotal,
            ShippingFee = shippingFee,
            VoucherDiscount = voucherDiscount,
            VoucherCode = voucherCode,
            FinalTotal = finalTotal,
            OfficialInvoiceId = order.HoaDon?.MaHoaDon,
            Items = itemsList
        };

        return View("~/Views/Order/ProformaInvoice.cshtml", vm);
    }

    // KHÁCH HÀNG XEM HÓA ĐƠN THUẾ CHÍNH THỨC CỦA CHÍNH MÌNH (BẢO VỆ PHÂN QUYỀN)
    [HttpGet]
    public async Task<IActionResult> ViewOfficialInvoice(int orderId)
    {
        var userId = CurrentUserId();
        var invoice = await _db.HoaDons.AsNoTracking()
            .Include(h => h.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(h => h.MaNguoiLapNavigation)
            .Include(h => h.ChiTietHoaDons)
            .FirstOrDefaultAsync(h => h.MaDonHang == orderId && h.MaDonHangNavigation.MaNguoiDung == userId);

        if (invoice == null)
        {
            return NotFound("Không tìm thấy hóa đơn hoặc bạn không có quyền xem hóa đơn này.");
        }

        ViewBag.DigitalSignature = DigitalSignatureInfoService.InspectAvailableDigitalCertificate();
        return View("~/Areas/Staff/Views/Invoice/Print.cshtml", invoice);
    }
}