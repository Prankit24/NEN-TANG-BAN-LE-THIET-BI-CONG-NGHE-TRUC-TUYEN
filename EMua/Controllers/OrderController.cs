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
        var orders = await _db.DonHangs.AsNoTracking().Where(x => x.MaNguoiDung == CurrentUserId())
            .OrderByDescending(x => x.NgayDat).Select(x => new OrderListItemViewModel
            {
                OrderId = x.MaDonHang, OrderedAt = x.NgayDat, Total = x.TongTien ?? 0,
                Status = x.TrangThaiDonHang ?? "Chờ xử lý", ItemCount = x.ChiTietDonHangs.Sum(i => i.SoLuong)
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
            OrderId = order.MaDonHang, OrderedAt = order.NgayDat, Total = order.TongTien ?? 0,
            Status = order.TrangThaiDonHang ?? "Chờ xử lý", RecipientName = order.HoTenNhanHang ?? string.Empty,
            RecipientPhone = order.SoDienThoaiNhanHang ?? string.Empty, ShippingAddress = order.DiaChiNhanHang ?? string.Empty,
            PaymentMethod = payment?.PhuongThuc ?? "COD", PaymentStatus = payment?.TrangThai ?? "Chưa thanh toán",
            Items = order.ChiTietDonHangs.Select(x => new CartItemViewModel
            {
                ProductId = x.MaBienTheNavigation.MaSanPham, VariantId = x.MaBienThe,
                ProductName = x.MaBienTheNavigation.MaSanPhamNavigation.TenSanPham ?? "Sản phẩm",
                VariantLabel = string.Join(" / ", new[] { x.MaBienTheNavigation.MauSac, x.MaBienTheNavigation.PhienBan }.Where(v => !string.IsNullOrWhiteSpace(v))),
                ImageUrl = x.MaBienTheNavigation.HinhAnh, UnitPrice = x.DonGia, Quantity = x.SoLuong
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

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}