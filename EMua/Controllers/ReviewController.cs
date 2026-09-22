using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class ReviewController : Controller
{
    private readonly EMuaDbContext _db;
    public ReviewController(EMuaDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Create(int productId, int orderId)
    {
        if (!await CanReviewAsync(productId, orderId)) return Forbid();
        return View(new ReviewCreateViewModel { ProductId = productId, OrderId = orderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReviewCreateViewModel model)
    {
        if (!await CanReviewAsync(model.ProductId, model.OrderId)) return Forbid();
        if (!ModelState.IsValid) return View(model);
        _db.DanhGiaSanPhams.Add(new DanhGiaSanPham
        {
            MaNguoiDung = CurrentUserId(), MaSanPham = model.ProductId, MaDonHang = model.OrderId,
            SoLuongSao = model.Rating, BinhLuan = model.Comment.Trim(), NgayDanhGia = DateTime.Now, TrangThai = true
        });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError(string.Empty, "Bạn đã đánh giá sản phẩm này rồi."); return View(model); }
        TempData["Success"] = "Cảm ơn bạn đã đánh giá sản phẩm.";
        return RedirectToAction("Details", "Product", new { id = model.ProductId });
    }

    private async Task<bool> CanReviewAsync(int productId, int orderId) =>
        !await _db.DanhGiaSanPhams.AnyAsync(x => x.MaNguoiDung == CurrentUserId() && x.MaSanPham == productId) &&
        await _db.ChiTietDonHangs.AnyAsync(x => x.MaDonHang == orderId && x.MaBienTheNavigation.MaSanPham == productId &&
            x.MaDonHangNavigation.MaNguoiDung == CurrentUserId() &&
            (x.MaDonHangNavigation.TrangThaiDonHang == "Đã hoàn thành" || x.MaDonHangNavigation.TrangThaiDonHang == "Đã giao"));

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}