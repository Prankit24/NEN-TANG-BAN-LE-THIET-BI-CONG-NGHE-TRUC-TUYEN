using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EMua.Areas.Staff.Controllers;

public class OrderController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private static readonly string[] Statuses = OrderRules.Statuses;
    private readonly OrderWorkflowService _workflow;

    public OrderController(EMuaDbContext db, IWebHostEnvironment webHostEnvironment, OrderWorkflowService workflow)
    {
        _db = db;
        _workflow = workflow;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<IActionResult> Index(string? q, string? status, int page = 1, int pageSize = 12)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var orders = _db.DonHangs.AsNoTracking()
            .Include(x => x.ThanhToans)
            .Include(x => x.MaNguoiDungNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var searchUpper = search.ToUpperInvariant();

            int? exactOrderId = null;
            if (searchUpper.StartsWith("#DH-") || searchUpper.StartsWith("DH-"))
            {
                var numPart = searchUpper.Replace("#DH-", "").Replace("DH-", "").Trim();
                if (int.TryParse(numPart, out var parsed)) exactOrderId = parsed;
            }
            else if (int.TryParse(search, out var parsedNum))
            {
                exactOrderId = parsedNum;
            }

            var pattern = $"%{search}%";

            orders = orders.Where(x =>
                (exactOrderId.HasValue && x.MaDonHang == exactOrderId.Value)
                || (x.HoTenNhanHang != null && EF.Functions.ILike(x.HoTenNhanHang, pattern))
                || (x.SoDienThoaiNhanHang != null && EF.Functions.ILike(x.SoDienThoaiNhanHang, pattern))
                || (x.MaNguoiDungNavigation != null && x.MaNguoiDungNavigation.TenNguoiDung != null && EF.Functions.ILike(x.MaNguoiDungNavigation.TenNguoiDung, pattern))
                || (x.MaNguoiDungNavigation != null && x.MaNguoiDungNavigation.Email != null && EF.Functions.ILike(x.MaNguoiDungNavigation.Email, pattern)));
        }

        // Thống kê nhanh theo trạng thái trên toàn bộ hoặc tập tìm kiếm
        var totalOrders = await orders.CountAsync();
        var pendingCount = await orders.CountAsync(o => OrderRules.PendingAliases.Contains(o.TrangThaiDonHang!) || string.IsNullOrWhiteSpace(o.TrangThaiDonHang));
        var returnCount = await orders.CountAsync(o => o.TrangThaiDonHang != null && (o.TrangThaiDonHang.Contains("đổi trả") || o.TrangThaiDonHang.Contains("Đổi trả")));
        var processingCount = await orders.CountAsync(o => OrderRules.ProcessingAliases.Contains(o.TrangThaiDonHang!));
        var shippingCount = await orders.CountAsync(o => OrderRules.ShippingAliases.Contains(o.TrangThaiDonHang!));
        var failedCount = await orders.CountAsync(o => OrderRules.DeliveryFailedAliases.Contains(o.TrangThaiDonHang!));
        var completedCount = await orders.CountAsync(o => OrderRules.CompletedAliases.Contains(o.TrangThaiDonHang!));

        ViewBag.TotalOrders = totalOrders;
        ViewBag.ProcessingCount = processingCount;
        ViewBag.FailedCount = failedCount;
        ViewBag.CompletedCount = completedCount;
        ViewBag.PendingCount = pendingCount;
        ViewBag.ReturnCount = returnCount;
        ViewBag.ShippingCount = shippingCount;

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var canonical = OrderRules.Normalize(status);
            var aliases = canonical == OrderRules.Pending ? OrderRules.PendingAliases :
                canonical == OrderRules.Processing ? OrderRules.ProcessingAliases :
                canonical == OrderRules.Shipping ? OrderRules.ShippingAliases :
                canonical == OrderRules.DeliveryFailed ? OrderRules.DeliveryFailedAliases :
                canonical == OrderRules.Completed ? OrderRules.CompletedAliases :
                canonical == OrderRules.Cancelled ? OrderRules.CancelledAliases : new[] { canonical };
            orders = orders.Where(x => aliases.Contains(x.TrangThaiDonHang!) ||
                (canonical == OrderRules.Pending && string.IsNullOrWhiteSpace(x.TrangThaiDonHang)));
        }

        int filteredTotal = await orders.CountAsync();
        int totalPages = (int)Math.Ceiling(filteredTotal / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.Statuses = Statuses;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = filteredTotal;
        ViewBag.PageSize = pageSize;

        // Sắp xếp ưu tiên:
        // 1: Yêu cầu đổi trả (nổi bật xử lý riêng)
        // 2: Chờ xác nhận, Chờ xử lý, Giao thất bại
        // 3: Đang xử lý, Đang giao
        // 4: Đã hoàn thành, Đã hủy xuống dưới cùng
        var resultList = await orders
            .OrderBy(x => 
                x.TrangThaiDonHang != null && x.TrangThaiDonHang.Contains("đổi trả") ? 1 :
                (OrderRules.PendingAliases.Contains(x.TrangThaiDonHang!) || OrderRules.DeliveryFailedAliases.Contains(x.TrangThaiDonHang!) || string.IsNullOrWhiteSpace(x.TrangThaiDonHang)) ? 2 :
                (OrderRules.ProcessingAliases.Contains(x.TrangThaiDonHang!) || OrderRules.ShippingAliases.Contains(x.TrangThaiDonHang!)) ? 3 :
                4
            )
            .ThenByDescending(x => x.NgayDat).ThenByDescending(x => x.MaDonHang)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return View(resultList);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (OrderRules.Normalize(status) == OrderRules.Completed || OrderRules.Normalize(status) == OrderRules.ReturnReceived)
        {
            TempData["ErrorMessage"] = "Mở chi tiết đơn để xác nhận hoàn thành/giao nhận kèm bằng chứng.";
            TempData["Error"] = "Mở chi tiết đơn để xác nhận hoàn thành/giao nhận kèm bằng chứng.";
        }
        else
        {
            var result = await _workflow.ChangeStateAsync(id, CurrentUserId(), status);
            TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            TempData[result.Success ? "Success" : "Error"] = result.Message;
        }
        return RedirectToAction(nameof(Index), new { status = "all" });
    }

    private int CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        ViewBag.Statuses = Statuses;

        var order = await _db.DonHangs.AsNoTracking()
            .Include(x => x.MaNguoiDungNavigation)
            .Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Include(x => x.ThanhToans)
            .Include(x => x.LichSuVanChuyens)
            .Include(x => x.HoaDon).ThenInclude(x => x!.ChiTietHoaDons)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.MaDonHang == id);

        if (order == null) return NotFound();
        var payment = order.ThanhToans.Count == 1 ? order.ThanhToans.Single() : null;
        ViewBag.Statuses = OrderRules.DetailTargets(order.TrangThaiDonHang, OrderRules.IsCod(payment?.PhuongThuc), OrderRules.IsPaid(payment?.TrangThai));
        return View(order);
    }

    [HttpGet]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        var invoice = await _db.HoaDons.Include(x => x.ChiTietHoaDons).FirstOrDefaultAsync(x => x.MaDonHang == id);
        if (invoice != null && (invoice.MaNguoiLap.HasValue || invoice.ChiTietHoaDons.Any()))
        {
            return RedirectToAction("Details", "Invoice", new { id = invoice.MaHoaDon });
        }
        return RedirectToAction("Create", "Invoice", new { orderId = id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateInvoicePost(int id)
    {
        return RedirectToAction("Create", "Invoice", new { orderId = id });
    }

    [HttpGet]
    public IActionResult PrintInvoice(int id)
    {
        return RedirectToAction("Print", "Invoice", new { id });
    }
    // =========================================================
    // QUẢN LÝ GIAO NHẬN
    // =========================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDeliveryStatus(int id, string status, DateTime? ngayDuKienGiao,
        IFormFile? anhXacNhanGiao, string? ghiChuGiaoHang, bool daThuTienCod = false, bool daNhanHangTra = false)
    {
        string? path = null;
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Dữ liệu giao nhận không hợp lệ.");
            if (anhXacNhanGiao is { Length: > 0 })
                path = await OrderEvidenceStorage.SaveAsync(_webHostEnvironment.WebRootPath, anhXacNhanGiao, "delivery-proof");
            DateTime? utc = ngayDuKienGiao.HasValue ? DateTime.SpecifyKind(ngayDuKienGiao.Value.AddHours(-7), DateTimeKind.Utc) : null;
            var result = await _workflow.ChangeStateAsync(id, CurrentUserId(), status, path, daThuTienCod, daNhanHangTra, utc, ghiChuGiaoHang);
            if (path != null && !result.ProofUsed && !result.Uncertain) OrderEvidenceStorage.Delete(_webHostEnvironment.WebRootPath, path);
            TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        }
        catch (ArgumentException ex) { if (path != null) OrderEvidenceStorage.Delete(_webHostEnvironment.WebRootPath, path); TempData["ErrorMessage"] = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { TempData["ErrorMessage"] = "Không lưu được ảnh giao hàng. Kiểm tra thư mục tải ảnh rồi thử lại."; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id, decimal amount, string? reference, bool verified)
    {
        if (!ModelState.IsValid) { TempData["ErrorMessage"] = "Số tiền không hợp lệ."; return RedirectToAction(nameof(Details), new { id }); }
        var result = await _workflow.ConfirmPaymentAsync(id, CurrentUserId(), amount, reference, verified);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessReturnAction(int id, string actionType, string? rejectReason)
    {
        var result = await _workflow.DecideReturnAsync(id, CurrentUserId(), actionType, rejectReason);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteReturn(int id, bool restock = false, bool refund = false, string? refundReference = null, string? note = null)
    {
        var result = await _workflow.CompleteReturnAsync(id, CurrentUserId(), restock, refund, refundReference, note);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Route("Staff/Order/GetNotificationCounts")]
    public async Task<IActionResult> GetNotificationCounts()
    {
        var pendingOrderCount = await _db.DonHangs
            .CountAsync(o => OrderRules.PendingAliases.Contains(o.TrangThaiDonHang!) || string.IsNullOrWhiteSpace(o.TrangThaiDonHang));

        var returnCount = await _db.DonHangs
            .CountAsync(o => o.TrangThaiDonHang != null && o.TrangThaiDonHang.Trim().ToLower() == "yêu cầu đổi trả");

        var lowStockCount = await _db.BienTheSanPhams
            .CountAsync(b => b.SoLuong <= 5 && b.TrangThai == "Còn hàng");

        return Json(new { pendingOrderCount, returnCount, lowStockCount });
    }

    // =========================================================
    // CHAT ĐỔI TRẢ - PHIÊN TẠM THỜI TRONG RAM
    // Staff chỉ được trả lời khi khách đã chủ động mở phiên.
    // =========================================================

    [HttpGet]
    [Route("Staff/Order/GetPendingReturnCount")]
    public async Task<IActionResult> GetPendingReturnCount()
    {
        int count = await _db.DonHangs
            .Where(x => x.TrangThaiDonHang != null && x.TrangThaiDonHang.Trim().ToLower() == "yêu cầu đổi trả")
            .CountAsync();
        return Json(new { count });
    }

    [HttpGet]
    [Route("Staff/Order/GetOrderChatStatus")]
    public IActionResult GetOrderChatStatus(int orderId)
    {
        var active = OrderChatSessionStore.TryGet(orderId, out var session);
        return Json(new { active, customerId = active ? session!.CustomerId : (int?)null });
    }

    [HttpGet]
    [Route("Staff/Order/GetOrderMessages")]
    public IActionResult GetOrderMessages(int orderId)
    {
        if (!OrderChatSessionStore.TryGet(orderId, out var session))
            return Json(Array.Empty<object>());

        OrderChatSessionStore.Touch(session!);
        return Json(session!.GetMessages());
    }

    [HttpPost]
    [Route("Staff/Order/SendOrderMessage")]
    [IgnoreAntiforgeryToken]
    public IActionResult SendOrderMessage([FromBody] SendMessageRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Message))
            return Json(new { success = false, message = "Nội dung tin nhắn không được để trống." });

        if (!OrderChatSessionStore.TryGet(req.OrderId, out var session))
            return Json(new { success = false, message = "Khách hàng chưa mở phiên trò chuyện hoặc phiên đã kết thúc." });

        var text = req.Message.Trim();
        session!.AddMessage("Nhân viên CSKH", text, true);
        OrderChatSessionStore.Touch(session);
        return Json(new { success = true, senderName = "Nhân viên CSKH", message = text, isStaff = true, time = DateTime.Now.ToString("HH:mm") });
    }

    [HttpPost]
    [Route("Staff/Order/EndOrderChat")]
    [IgnoreAntiforgeryToken]
    public IActionResult EndOrderChat([FromBody] EndChatRequest req)
    {
        OrderChatSessionStore.EndByStaff(req.OrderId);
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
}
