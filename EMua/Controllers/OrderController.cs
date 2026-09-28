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

    public OrderController(EMuaDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
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
                        : i.MaBienTheNavigation.MaSanPhamNavigation.SanPhamHinhAnhs.FirstOrDefault().DuongDanAnh,

                    Quantity = i.SoLuong,
                    Price = i.DonGia
                }).ToList()
            }).ToListAsync();

        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.DonHangs.AsNoTracking()
            .Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Include(x => x.ThanhToans).Include(x => x.LichSuVanChuyens)
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
            Status = order.TrangThaiDonHang ?? "Chờ xử lý",
            RecipientName = order.HoTenNhanHang ?? string.Empty,
            RecipientPhone = order.SoDienThoaiNhanHang ?? string.Empty,
            ShippingAddress = order.DiaChiNhanHang ?? string.Empty,
            PaymentMethod = payment?.PhuongThuc ?? "COD",
            PaymentStatus = payment?.TrangThai ?? "Chưa thanh toán",
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
        var order = await _db.DonHangs.Include(x => x.ChiTietDonHangs)
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaNguoiDung == CurrentUserId());
        if (order == null) return NotFound();
        if (order.TrangThaiDonHang != "Chờ xử lý")
        {
            TempData["Error"] = "Đơn hàng chỉ được hủy khi đang chờ xử lý.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var ids = order.ChiTietDonHangs.Select(x => x.MaBienThe).ToList();
        var variants = await _db.BienTheSanPhams.Where(x => ids.Contains(x.MaBienThe)).ToListAsync();
        foreach (var item in order.ChiTietDonHangs) variants.First(x => x.MaBienThe == item.MaBienThe).SoLuong += item.SoLuong;
        order.TrangThaiDonHang = "Đã hủy";
        _db.LichSuVanChuyens.Add(new LichSuVanChuyen { MaDonHang = id, TrangThai = "Đã hủy", MoTa = "Khách hàng đã hủy đơn.", ThoiGian = DateTime.Now });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã hủy đơn hàng.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var payment = await _db.ThanhToans.Include(x => x.MaDonHangNavigation)
            .FirstOrDefaultAsync(x => x.MaDonHang == id && x.MaDonHangNavigation.MaNguoiDung == CurrentUserId());
        if (payment == null) return NotFound();
        if (payment.PhuongThuc != "COD")
        {
            payment.TrangThai = "Đã thanh toán";
            payment.MaGiaoDich = $"LOCAL-{Guid.NewGuid():N}"[..20];
            payment.NgayThanhToan = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Thanh toán đã được ghi nhận.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReturn(
        int orderId,
        List<int> selectedVariantIds,
        string reason,
        string description,
        string contactPhone,
        List<IFormFile>? evidenceImages)
    {
        var userId = CurrentUserId();
        var order = await _db.DonHangs
            .Include(x => x.ChiTietDonHangs)
            .FirstOrDefaultAsync(x => x.MaDonHang == orderId && x.MaNguoiDung == userId);

        if (order == null) return NotFound();

        if (order.TrangThaiDonHang != "Đã hoàn thành" && order.TrangThaiDonHang != "Đã giao")
        {
            TempData["Error"] = "Đơn hàng chưa hoàn thành nên không thể yêu cầu đổi trả.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        if (selectedVariantIds == null || !selectedVariantIds.Any())
        {
            TempData["Error"] = "Vui lòng chọn ít nhất một sản phẩm cần đổi trả.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        var savedImagePaths = new List<string>();
        if (evidenceImages != null && evidenceImages.Count > 0)
        {
            string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "returns");
            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            foreach (var file in evidenceImages)
            {
                if (file.Length > 0)
                {
                    string fileName = $"return_{orderId}_{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
                    string filePath = Path.Combine(uploadFolder, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    savedImagePaths.Add($"/uploads/returns/{fileName}");
                }
            }
        }

        order.TrangThaiDonHang = "Yêu cầu đổi trả";

        string imageEvidenceText = savedImagePaths.Any()
            ? $"\n- Ảnh bằng chứng: {string.Join(", ", savedImagePaths)}"
            : "";

        string returnDescription = $"[YÊU CẦU ĐỔI TRẢ/HOÀN TIỀN]\n" +
                                  $"- Sản phẩm Variant IDs: {string.Join(", ", selectedVariantIds)}\n" +
                                  $"- Lý do: {reason}\n" +
                                  $"- Mô tả: {description}\n" +
                                  $"- SĐT Hotline cửa hàng hỗ trợ: {contactPhone}" +
                                  imageEvidenceText;

        _db.LichSuVanChuyens.Add(new LichSuVanChuyen
        {
            MaDonHang = orderId,
            TrangThai = "Yêu cầu đổi trả",
            MoTa = returnDescription,
            ThoiGian = DateTime.Now
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = "Yêu cầu đổi trả của bạn đã được gửi thành công. EMUA sẽ liên hệ hỗ trợ bạn sớm nhất!";
        return RedirectToAction(nameof(Details), new { id = orderId });
    }

    public class UpdateOrderItemRequest
    {
        public int OrderId { get; set; }
        public int VariantId { get; set; }
        public int Quantity { get; set; }
    }

    private static readonly string[] EditableOrderStatuses = { "Chờ xử lý", "Chờ thanh toán" };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOrderItem([FromBody] UpdateOrderItemRequest req)
    {
        var order = await _db.DonHangs
            .Include(x => x.ChiTietDonHangs)
            .FirstOrDefaultAsync(x => x.MaDonHang == req.OrderId && x.MaNguoiDung == CurrentUserId());

        if (order == null)
            return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

        if (!EditableOrderStatuses.Contains(order.TrangThaiDonHang))
            return Json(new { success = false, message = "Đơn hàng này không còn ở trạng thái cho phép chỉnh sửa." });

        var line = order.ChiTietDonHangs.FirstOrDefault(x => x.MaBienThe == req.VariantId);
        if (line == null)
            return Json(new { success = false, message = "Không tìm thấy sản phẩm trong đơn hàng." });

        var variant = await _db.BienTheSanPhams.FirstOrDefaultAsync(x => x.MaBienThe == req.VariantId);
        int oldQty = line.SoLuong;

        if (req.Quantity <= 0)
        {
            if (order.ChiTietDonHangs.Count <= 1)
                return Json(new { success = false, message = "Đơn hàng phải có ít nhất 1 sản phẩm. Nếu muốn huỷ toàn bộ đơn, vui lòng dùng nút \"Hủy đơn hàng\"." });

            if (variant != null) variant.SoLuong += oldQty;
            _db.ChiTietDonHangs.Remove(line);
        }
        else
        {
            int delta = req.Quantity - oldQty;
            if (delta > 0 && variant != null && variant.SoLuong < delta)
                return Json(new { success = false, message = $"Chỉ còn {variant.SoLuong} sản phẩm trong kho." });

            if (variant != null) variant.SoLuong -= delta;
            line.SoLuong = req.Quantity;
            line.ThanhTien = line.DonGia * req.Quantity;
        }

        await _db.SaveChangesAsync();

        var remaining = await _db.ChiTietDonHangs.Where(x => x.MaDonHang == order.MaDonHang).ToListAsync();
        order.TongTien = remaining.Sum(x => x.ThanhTien);
        await _db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            orderTotal = order.TongTien,
            orderStatus = order.TrangThaiDonHang
        });
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

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}