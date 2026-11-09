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
    private static readonly string[] Statuses = [
        "Chờ xử lý",
        "Đang xử lý",
        "Đã hoàn thành",
        "Đã hủy",
        "Yêu cầu đổi trả",
        "Đang đổi trả",
        "Đã nhận đổi trả",
        "Đã hoàn tiền"
    ];

    public OrderController(EMuaDbContext db, IWebHostEnvironment webHostEnvironment)
    {
        _db = db;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<IActionResult> Index(string? q, string? status, int page = 1, int pageSize = 12)
    {
        var orders = _db.DonHangs.AsNoTracking()
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
        var pendingCount = await orders.CountAsync(o => o.TrangThaiDonHang == "Chờ xử lý" || o.TrangThaiDonHang == "Chờ xác nhận" || string.IsNullOrWhiteSpace(o.TrangThaiDonHang));
        var returnCount = await orders.CountAsync(o => o.TrangThaiDonHang != null && (o.TrangThaiDonHang.Contains("đổi trả") || o.TrangThaiDonHang.Contains("Đổi trả")));
        var shippingCount = await orders.CountAsync(o => o.TrangThaiDonHang == "Đang xử lý" || o.TrangThaiDonHang == "Đang giao" || o.TrangThaiDonHang == "Đang giao hàng");

        ViewBag.TotalOrders = totalOrders;
        ViewBag.PendingCount = pendingCount;
        ViewBag.ReturnCount = returnCount;
        ViewBag.ShippingCount = shippingCount;

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            orders = orders.Where(x => x.TrangThaiDonHang == status);

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
                (x.TrangThaiDonHang == "Chờ xác nhận" || x.TrangThaiDonHang == "Chờ xử lý" || x.TrangThaiDonHang == "Giao thất bại" || string.IsNullOrWhiteSpace(x.TrangThaiDonHang)) ? 2 :
                (x.TrangThaiDonHang == "Đang xử lý" || x.TrangThaiDonHang == "Đang giao" || x.TrangThaiDonHang == "Đang giao hàng") ? 3 :
                4
            )
            .ThenByDescending(x => x.NgayDat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return View(resultList);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (!Statuses.Contains(status)) return BadRequest();
        var order = await _db.DonHangs.FindAsync(id);
        if (order == null) return NotFound();
        order.TrangThaiDonHang = status;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Đã cập nhật đơn hàng #DH-{id:D5}.";
        return RedirectToAction(nameof(Index), new { status = "all" });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        ViewBag.Statuses = Statuses;

        var order = await _db.DonHangs.AsNoTracking()
            .Include(x => x.MaNguoiDungNavigation)
            .Include(x => x.ChiTietDonHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .Include(x => x.ThanhToans)
            .Include(x => x.LichSuVanChuyens)
            .FirstOrDefaultAsync(x => x.MaDonHang == id);

        return order == null ? NotFound() : View(order);
    }

    [HttpGet]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        var invoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == id);
        if (invoice != null)
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
    public async Task<IActionResult> UpdateDeliveryStatus(
        int id,
        string status,
        DateTime? ngayDuKienGiao,
        IFormFile? anhXacNhanGiao,
        string? ghiChuGiaoHang)
    {
        if (!Statuses.Contains(status)) return BadRequest();

        var order = await _db.DonHangs
            .Include(o => o.ChiTietDonHangs)
            .FirstOrDefaultAsync(o => o.MaDonHang == id);

        if (order == null) return NotFound();

        order.TrangThaiDonHang = status;
        order.NgayDuKienGiao = ngayDuKienGiao.HasValue
            ? DateTime.SpecifyKind(ngayDuKienGiao.Value, DateTimeKind.Utc)
            : null;

        if (!string.IsNullOrWhiteSpace(ghiChuGiaoHang))
        {
            order.GhiChu = string.IsNullOrWhiteSpace(order.GhiChu)
                ? ghiChuGiaoHang
                : order.GhiChu + " | " + ghiChuGiaoHang;
        }

        var isDelivered = status == "Đã hoàn thành" || status == "Đã giao";
        if (isDelivered)
        {
            if (anhXacNhanGiao == null || anhXacNhanGiao.Length == 0)
            {
                TempData["ErrorMessage"] = "Vui lòng chọn ảnh xác nhận đã giao hàng.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "delivery-proof");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"order-{id}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(anhXacNhanGiao.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await anhXacNhanGiao.CopyToAsync(stream);
            }

            order.AnhXacNhanGiao = $"/uploads/delivery-proof/{fileName}";

            // Đồng bộ trạng thái thanh toán thành "Đã thanh toán" nếu đơn hàng đã giao thành công
            var payment = await _db.ThanhToans.FirstOrDefaultAsync(x => x.MaDonHang == id);
            if (payment != null && payment.TrangThai != "Đã thanh toán")
            {
                payment.TrangThai = "Đã thanh toán";
                payment.NgayThanhToan = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Cập nhật giao nhận thành công.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessReturnAction(int id, string actionType, string? rejectReason)
    {
        var order = await _db.DonHangs.FirstOrDefaultAsync(x => x.MaDonHang == id);
        if (order == null) return NotFound();

        if (actionType == "Accept")
        {
            // Chuyển trạng thái đơn hàng sang "Đã nhận đổi trả" để đồng bộ giao diện tiến trình bên khách hàng
            order.TrangThaiDonHang = "Đã nhận đổi trả";
            _db.LichSuVanChuyens.Add(new LichSuVanChuyen
            {
                MaDonHang = id,
                TrangThai = "Đã nhận đổi trả",
                MoTa = "Nhân viên đã duyệt yêu cầu đổi trả. Đang tiến hành xử lý thu hồi sản phẩm và hoàn tiền.",
                ThoiGian = DateTime.Now
            });
            TempData["SuccessMessage"] = "Đã CHẤP NHẬN yêu cầu đổi trả. Đơn hàng chuyển sang trạng thái xử lý đổi trả.";
        }
        else if (actionType == "Reject")
        {
            order.TrangThaiDonHang = "Đã hoàn thành";
            _db.LichSuVanChuyens.Add(new LichSuVanChuyen
            {
                MaDonHang = id,
                TrangThai = "Từ chối đổi trả",
                MoTa = $"Từ chối đổi trả. Lý do: {rejectReason ?? "Không phù hợp điều kiện đổi trả"}",
                ThoiGian = DateTime.Now
            });
            TempData["SuccessMessage"] = "Đã TỪ CHỐI yêu cầu đổi trả và đưa đơn hàng về trạng thái hoàn thành.";
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
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
