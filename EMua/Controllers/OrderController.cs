using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class OrderController : Controller
{
    private readonly EMuaDbContext _db;
    public OrderController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var orders = await _db.DonHangs
            .AsNoTracking()
            .Where(x => x.MaNguoiDung == CurrentUserId())
            .OrderByDescending(x => x.NgayDat)
            .Select(x => new OrderListItemViewModel
            {
                OrderId = x.MaDonHang,
                OrderedAt = x.NgayDat,
                Total = x.TongTien ?? 0,
                Status = x.TrangThaiDonHang ?? "Chờ xử lý",
                ItemCount = x.ChiTietDonHangs.Sum(i => i.SoLuong),
                // Map danh sách sản phẩm cùng hình ảnh
                Items = x.ChiTietDonHangs.Select(i => new OrderItemSummaryViewModel
                {
                    ProductId = i.MaBienTheNavigation.MaSanPham,
                    ProductName = i.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
                    ProductImageUrl = i.MaBienTheNavigation.HinhAnh,
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
        return View(new OrderDetailsViewModel
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
            { Status = x.TrangThai, Description = x.MoTa, Time = x.ThoiGian }).ToList()
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
            payment.TrangThai = "Đã thanh toán"; payment.MaGiaoDich = $"LOCAL-{Guid.NewGuid():N}"[..20]; payment.NgayThanhToan = DateTime.Now;
            await _db.SaveChangesAsync(); TempData["Success"] = "Thanh toán đã được ghi nhận.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    public class UpdateOrderItemRequest
    {
        public int OrderId { get; set; }
        public int VariantId { get; set; }
        // Số lượng mới; 0 hoặc âm nghĩa là xoá sản phẩm khỏi đơn
        public int Quantity { get; set; }
    }

    // Các trạng thái đơn còn được phép chỉnh số lượng / xoá sản phẩm.
    // Khớp với điều kiện hiện có của nút "Hủy đơn hàng".
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
            // Không cho xoá sản phẩm cuối cùng — muốn huỷ cả đơn phải chủ động bấm "Hủy đơn hàng",
            // không để việc xoá từng sản phẩm ngầm biến thành huỷ đơn.
            if (order.ChiTietDonHangs.Count <= 1)
                return Json(new { success = false, message = "Đơn hàng phải có ít nhất 1 sản phẩm. Nếu muốn huỷ toàn bộ đơn, vui lòng dùng nút \"Hủy đơn hàng\"." });

            // Trả lại tồn kho rồi xoá dòng sản phẩm khỏi đơn
            if (variant != null) variant.SoLuong += oldQty;
            _db.ChiTietDonHangs.Remove(line);
        }
        else
        {
            int delta = req.Quantity - oldQty; // >0: cần trừ thêm kho, <0: trả lại kho
            if (delta > 0 && variant != null && variant.SoLuong < delta)
                return Json(new { success = false, message = $"Chỉ còn {variant.SoLuong} sản phẩm trong kho." });

            if (variant != null) variant.SoLuong -= delta;
            line.SoLuong = req.Quantity;
            line.ThanhTien = line.DonGia * req.Quantity;
        }

        await _db.SaveChangesAsync();

        // Tính lại tổng tiền đơn hàng theo các dòng còn lại
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

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}