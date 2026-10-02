using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EMua.Controllers
{
    public class ReviewController : Controller
    {
        private readonly EMuaDbContext _context;

        public ReviewController(EMuaDbContext context)
        {
            _context = context;
        }

        // =========================================================================
        // 1. GET: /Review/Create?productId=...&orderId=...
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> Create(int productId, int orderId)
        {
            var donHang = await _context.DonHangs
                .FirstOrDefaultAsync(o => o.MaDonHang == orderId);

            if (donHang == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "Order");
            }

            var thanhToan = await _context.ThanhToans
                .FirstOrDefaultAsync(t => t.MaDonHang == orderId);

            string trangThaiThanhToan = thanhToan?.TrangThai ?? "Chưa thanh toán";

            bool isEligible = (donHang.TrangThaiDonHang == "Đã hoàn thành" || donHang.TrangThaiDonHang == "Đã giao")
                              && trangThaiThanhToan == "Đã thanh toán";

            if (!isEligible)
            {
                TempData["Error"] = "Đơn hàng chưa giao thành công hoặc chưa hoàn tất thanh toán.";
                return RedirectToAction("Details", "Order", new { id = orderId });
            }

            var model = new ReviewCreateViewModel
            {
                ProductId = productId,
                OrderId = orderId,
                Rating = 5
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewCreateViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // KIỂM TRA: Đã đánh giá sản phẩm này trong đơn hàng này chưa?
            bool daDanhGia = await _context.DanhGiaSanPhams
                .AnyAsync(r => r.MaDonHang == model.OrderId && r.MaSanPham == model.ProductId);

            if (daDanhGia)
            {
                TempData["Error"] = "Bạn đã gửi đánh giá cho sản phẩm này trong đơn hàng rồi.";
                return RedirectToAction("Details", "Order", new { id = model.OrderId });
            }

            var donHang = await _context.DonHangs
                .FirstOrDefaultAsync(o => o.MaDonHang == model.OrderId);

            if (donHang == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Details", "Order", new { id = model.OrderId });
            }

            var danhGia = new DanhGiaSanPham
            {
                MaSanPham = model.ProductId,
                MaDonHang = model.OrderId,
                MaNguoiDung = donHang.MaNguoiDung,
                SoLuongSao = model.Rating,
                BinhLuan = model.Comment,
                NgayDanhGia = DateTime.Now,
                TrangThai = true
            };

            _context.DanhGiaSanPhams.Add(danhGia);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Cảm ơn bạn đã gửi đánh giá cho sản phẩm!";
            return RedirectToAction("Details", "Order", new { id = model.OrderId });
        }

        // =========================================================================
        // 3. POST: /Review/SubmitReview (Đánh giá nhanh từ Chi tiết đơn hàng)
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReview(int OrderId, int Rating, string Comment)
        {
            if (OrderId <= 0)
            {
                TempData["Error"] = "Mã đơn hàng không hợp lệ.";
                return RedirectToAction("Index", "Order");
            }

            // KIỂM TRA: Đơn hàng này đã có bất kỳ đánh giá nào chưa?
            bool daDanhGiaDonHang = await _context.DanhGiaSanPhams
                .AnyAsync(r => r.MaDonHang == OrderId);

            if (daDanhGiaDonHang)
            {
                TempData["Error"] = "Đơn hàng này đã được đánh giá trước đó.";
                return RedirectToAction("Details", "Order", new { id = OrderId });
            }

            if (Rating < 1 || Rating > 5 || string.IsNullOrWhiteSpace(Comment))
            {
                TempData["Error"] = "Vui lòng chọn mức độ hài lòng (sao) và nhập nội dung đánh giá.";
                return RedirectToAction("Details", "Order", new { id = OrderId });
            }

            var donHang = await _context.DonHangs
                .Include(o => o.ChiTietDonHangs)
                    .ThenInclude(ct => ct.MaBienTheNavigation)
                .FirstOrDefaultAsync(o => o.MaDonHang == OrderId);

            if (donHang == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "Order");
            }

            var thanhToan = await _context.ThanhToans
                .FirstOrDefaultAsync(t => t.MaDonHang == OrderId);

            string trangThaiThanhToan = thanhToan?.TrangThai ?? "Chưa thanh toán";

            if ((donHang.TrangThaiDonHang != "Đã hoàn thành" && donHang.TrangThaiDonHang != "Đã giao") || trangThaiThanhToan != "Đã thanh toán")
            {
                TempData["Error"] = "Chỉ đơn hàng đã giao thành công và đã thanh toán mới được gửi đánh giá.";
                return RedirectToAction("Details", "Order", new { id = OrderId });
            }

            if (donHang.ChiTietDonHangs != null && donHang.ChiTietDonHangs.Any())
            {
                var dsMaSanPham = donHang.ChiTietDonHangs
                    .Select(ct => ct.MaBienTheNavigation.MaSanPham)
                    .Distinct();

                foreach (var maSanPham in dsMaSanPham)
                {
                    var danhGia = new DanhGiaSanPham
                    {
                        MaSanPham = maSanPham,
                        MaDonHang = OrderId,
                        MaNguoiDung = donHang.MaNguoiDung,
                        SoLuongSao = Rating,
                        BinhLuan = Comment,
                        NgayDanhGia = DateTime.Now,
                        TrangThai = true
                    };

                    _context.DanhGiaSanPhams.Add(danhGia);
                }

                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "Cảm ơn bạn đã gửi đánh giá trải nghiệm dịch vụ!";
            return RedirectToAction("Details", "Order", new { id = OrderId });
        }
    }
}