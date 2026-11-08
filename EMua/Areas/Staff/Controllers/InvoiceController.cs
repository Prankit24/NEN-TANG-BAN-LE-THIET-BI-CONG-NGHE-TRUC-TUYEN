using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EMua.Areas.Staff.Controllers;

public class InvoiceController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public InvoiceController(EMuaDbContext db) => _db = db;

    // 1. DANH SÁCH HÓA ĐƠN
    public async Task<IActionResult> Index(string? q, string? status, int page = 1, int pageSize = 10)
    {
        var invoicesQuery = _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .Include(x => x.MaNguoiLapNavigation)
            .AsQueryable();

        // Tìm kiếm theo mã hóa đơn, mã đơn hàng hoặc tên khách hàng
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var searchClean = search.Replace("#HD-", "").Replace("#DH-", "").Replace("HD-", "").Replace("DH-", "");
            invoicesQuery = invoicesQuery.Where(x =>
                x.MaHoaDon.ToString().Contains(searchClean) ||
                x.MaDonHang.ToString().Contains(searchClean) ||
                (x.HoTenKhachHang != null && x.HoTenKhachHang.ToLower().Contains(search.ToLower())) ||
                (x.SoDienThoai != null && x.SoDienThoai.Contains(search)));
        }

        var allInvoices = await invoicesQuery
            .OrderByDescending(x => x.MaHoaDon)
            .ToListAsync();

        // Đồng bộ trạng thái hiển thị
        foreach (var inv in allInvoices)
        {
            var orderStatus = inv.MaDonHangNavigation?.TrangThaiDonHang;
            if (string.IsNullOrEmpty(inv.TrangThaiHoaDon) || inv.TrangThaiHoaDon == "Đã lập")
            {
                if (orderStatus == "Đã hoàn thành" || orderStatus == "Đã giao")
                {
                    inv.TrangThaiHoaDon = "Đã thanh toán";
                }
            }
        }

        // Lấy danh sách các trạng thái có thật
        ViewBag.StatusList = allInvoices
            .Where(x => !string.IsNullOrWhiteSpace(x.TrangThaiHoaDon))
            .Select(x => x.TrangThaiHoaDon!)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // Lọc theo trạng thái
        if (!string.IsNullOrWhiteSpace(status))
        {
            allInvoices = allInvoices.Where(x => x.TrangThaiHoaDon == status).ToList();
        }

        int totalItems = allInvoices.Count;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = allInvoices
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.Search = q;
        ViewBag.SelectedStatus = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.PageSize = pageSize;

        return View(items);
    }

    // 2. XEM TRƯỚC VÀ LẬP HÓA ĐƠN CHO ĐƠN HÀNG (GET)
    [HttpGet]
    public async Task<IActionResult> Create(int orderId)
    {
        var order = await _db.DonHangs
            .AsNoTracking()
            .Include(d => d.ChiTietDonHangs)
                .ThenInclude(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                        .ThenInclude(sp => sp.MaDanhMucNavigation)
            .Include(d => d.MaKhuyenMaiNavigation)
            .Include(d => d.ThanhToans)
            .FirstOrDefaultAsync(d => d.MaDonHang == orderId);

        if (order == null)
        {
            TempData["Error"] = $"Không tìm thấy đơn hàng #DH-{orderId:D5}.";
            return RedirectToAction("Index", "Order");
        }

        // Kiểm tra chống lập trùng hóa đơn
        var existingInvoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == orderId);
        if (existingInvoice != null)
        {
            TempData["Warning"] = $"Đơn hàng #DH-{orderId:D5} đã có hóa đơn #HD-{existingInvoice.MaHoaDon:D5}. Không thể lập hóa đơn trùng.";
            return RedirectToAction(nameof(Details), new { id = existingInvoice.MaHoaDon });
        }

        // Kiểm tra điều kiện đơn hàng không bị hủy
        if (order.TrangThaiDonHang != null && order.TrangThaiDonHang.Contains("hủy", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = $"Đơn hàng #DH-{orderId:D5} đã bị hủy, không thể lập hóa đơn bán hàng.";
            return RedirectToAction("Details", "Order", new { id = orderId });
        }

        // Kiểm tra và lấy thông tin nhân viên/Admin đang đăng nhập
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(currentUserIdStr, out int currentUserId) || currentUserId <= 0)
        {
            TempData["Error"] = "Không thể xác thực thông tin tài khoản nhân viên. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Order");
        }

        var currentUser = await _db.NguoiDungs
            .Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == currentUserId);

        if (currentUser == null || !currentUser.TrangThai)
        {
            TempData["Error"] = "Tài khoản nhân viên không hợp lệ hoặc đã bị vô hiệu hóa.";
            return RedirectToAction("Index", "Order");
        }

        var staffName = !string.IsNullOrWhiteSpace(currentUser.TenNguoiDung)
            ? currentUser.TenNguoiDung.Trim()
            : (!string.IsNullOrWhiteSpace(currentUser.TenDangNhap) ? currentUser.TenDangNhap.Trim() : User.Identity?.Name?.Trim());

        if (string.IsNullOrWhiteSpace(staffName))
        {
            TempData["Error"] = "Không thể xác định tên nhân viên lập hóa đơn từ tài khoản hiện tại.";
            return RedirectToAction("Index", "Order");
        }

        // Lấy phương thức thanh toán
        var payment = order.ThanhToans.OrderByDescending(p => p.NgayTao).FirstOrDefault();
        var paymentMethod = payment?.PhuongThuc ?? "Thanh toán khi nhận hàng (COD)";

        decimal itemsSubtotal = 0m;
        var itemsList = new List<InvoiceItemViewModel>();

        foreach (var ct in order.ChiTietDonHangs)
        {
            var sp = ct.MaBienTheNavigation?.MaSanPhamNavigation;
            var bt = ct.MaBienTheNavigation;
            var categoryName = sp?.MaDanhMucNavigation?.TenDanhMuc ?? "";

            // Đơn giá bán niêm yết từ đơn hàng (đã bao gồm các loại thuế theo quy chế bán lẻ e-commerce)
            decimal unitGrossPrice = ct.DonGia;
            decimal lineTotal = ct.ThanhTien;
            itemsSubtotal += lineTotal;

            // Chưa có cấu hình bảng thuế xác nhận từ CSDL -> không tự gán thuế suất giả
            itemsList.Add(new InvoiceItemViewModel
            {
                MaBienThe = ct.MaBienThe,
                TenSanPham = sp?.TenSanPham ?? "Sản phẩm",
                BienThe = $"{bt?.MauSac ?? ""} {bt?.PhienBan ?? ""}".Trim(),
                SoLuong = ct.SoLuong,
                DonGiaGoc = unitGrossPrice,
                ThueSuatVat = null,
                DonGiaChuaThue = null,
                TienThueVat = null,
                DonGiaSauThue = unitGrossPrice,
                ChietKhau = 0m,
                ThanhTien = lineTotal,
                HinhAnh = bt?.HinhAnh,
                DanhMuc = categoryName
            });
        }

        // Tính toán chiết khấu coupon
        decimal voucherDiscount = 0m;
        string? voucherCode = order.MaKhuyenMaiNavigation?.MaCode;

        if (order.MaKhuyenMaiNavigation != null)
        {
            var km = order.MaKhuyenMaiNavigation;
            if (km.LoaiGiamGia != null && (km.LoaiGiamGia.Contains("%") || km.LoaiGiamGia.Contains("trăm", StringComparison.OrdinalIgnoreCase)))
            {
                voucherDiscount = Math.Round(itemsSubtotal * km.GiaTriGiam / 100m, 2);
            }
            else
            {
                voucherDiscount = km.GiaTriGiam;
            }
            voucherDiscount = Math.Clamp(voucherDiscount, 0m, itemsSubtotal);
        }

        // Tính toán phí vận chuyển thực tế từ đơn hàng:
        // Đơn hàng lưu tổng thanh toán tại TongTien = Tiền hàng + Phí ship - Giảm giá
        // Do đó Phí ship thực tế = order.TongTien - Tiền hàng + Giảm giá
        decimal finalTotal = order.TongTien ?? Math.Max(0m, itemsSubtotal - voucherDiscount);
        decimal shippingFee = Math.Max(0m, finalTotal + voucherDiscount - itemsSubtotal);

        var viewModel = new InvoiceCreateViewModel
        {
            MaDonHang = order.MaDonHang,
            NgayDatHang = order.NgayDat ?? DateTime.Now,
            TrangThaiDonHang = order.TrangThaiDonHang ?? "Chờ xử lý",
            HoTenKhachHang = order.HoTenNhanHang ?? "Khách hàng",
            SoDienThoai = order.SoDienThoaiNhanHang ?? "",
            DiaChiGiaoHang = order.DiaChiNhanHang ?? "",
            PhuongThucThanhToan = paymentMethod,
            PhuongThucVanChuyen = order.PhuongThucVanChuyen ?? "Giao hàng tiêu chuẩn",
            MaNguoiLap = currentUserId,
            TenNguoiLap = staffName,
            TongTienHang = itemsSubtotal,
            TongTienTruocThue = null,
            TongTienThueVat = null,
            PhiVanChuyen = shippingFee,
            GiamGiaVoucher = voucherDiscount,
            MaVoucher = voucherCode,
            TongThanhToan = finalTotal,
            GhiChu = order.GhiChu,
            Items = itemsList
        };

        return View(viewModel);
    }

    // 3. XÁC NHẬN LẬP HÓA ĐƠN VÀ LƯU SNAPSHOT (POST)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InvoiceCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu hóa đơn không hợp lệ. Vui lòng kiểm tra lại.";
            return View(model);
        }

        // Kiểm tra chống trùng hóa đơn
        var existingInvoice = await _db.HoaDons.FirstOrDefaultAsync(x => x.MaDonHang == model.MaDonHang);
        if (existingInvoice != null)
        {
            TempData["Warning"] = $"Đơn hàng #DH-{model.MaDonHang:D5} đã có hóa đơn #HD-{existingInvoice.MaHoaDon:D5}.";
            return RedirectToAction(nameof(Details), new { id = existingInvoice.MaHoaDon });
        }

        var order = await _db.DonHangs
            .Include(d => d.ChiTietDonHangs)
            .FirstOrDefaultAsync(d => d.MaDonHang == model.MaDonHang);

        if (order == null)
        {
            TempData["Error"] = "Không tìm thấy đơn hàng tương ứng.";
            return RedirectToAction("Index", "Order");
        }

        // Kiểm tra nghiêm ngặt tài khoản nhân viên/Admin đang đăng nhập trực tiếp
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(currentUserIdStr, out int currentUserId) || currentUserId <= 0)
        {
            TempData["Error"] = "Phiên làm việc đã hết hạn hoặc không xác thực được tài khoản. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Order");
        }

        var currentUser = await _db.NguoiDungs
            .Include(u => u.MaQuyenNavigation)
            .FirstOrDefaultAsync(u => u.MaNguoiDung == currentUserId);

        if (currentUser == null || !currentUser.TrangThai)
        {
            TempData["Error"] = "Tài khoản thực hiện không tồn tại hoặc đã bị khóa.";
            return RedirectToAction("Index", "Order");
        }

        var staffName = !string.IsNullOrWhiteSpace(currentUser.TenNguoiDung)
            ? currentUser.TenNguoiDung.Trim()
            : (!string.IsNullOrWhiteSpace(currentUser.TenDangNhap) ? currentUser.TenDangNhap.Trim() : User.Identity?.Name?.Trim());

        if (string.IsNullOrWhiteSpace(staffName))
        {
            TempData["Error"] = "Không xác định được danh tính nhân viên lập hóa đơn.";
            return RedirectToAction("Index", "Order");
        }

        // 8. KIỂM TRA ĐỐI SOÁT TỔNG HÓA ĐƠN VỚI TỔNG ĐƠN HÀNG
        // Giá bán trên đơn hàng đã bao gồm thuế, không cộng dồn VAT làm sai lệch tổng tiền
        if (order.TongTien.HasValue && Math.Abs(order.TongTien.Value - model.TongThanhToan) > 100m)
        {
            TempData["Error"] = $"Tổng tiền hóa đơn ({model.TongThanhToan:N0} đ) không khớp với số tiền thanh toán của đơn hàng ({order.TongTien.Value:N0} đ). Vui lòng kiểm tra lại.";
            return View(model);
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        int createdInvoiceId = 0;

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var invoice = new HoaDon
                {
                    MaDonHang = model.MaDonHang,
                    NgayLapHoaDon = DateTime.Now,
                    TongTien = model.TongThanhToan,
                    TrangThaiHoaDon = (order.TrangThaiDonHang == "Đã hoàn thành" || order.TrangThaiDonHang == "Đã giao")
                        ? "Đã thanh toán" : "Đã lập",
                    MaNguoiLap = currentUserId,
                    TenNguoiLap = staffName,
                    HoTenKhachHang = model.HoTenKhachHang?.Trim(),
                    SoDienThoai = model.SoDienThoai?.Trim(),
                    DiaChiGiaoHang = model.DiaChiGiaoHang?.Trim(),
                    TienTruocThue = model.TongTienTruocThue,
                    TienThueVat = model.TongTienThueVat,
                    PhiVanChuyen = model.PhiVanChuyen,
                    TienGiamGia = model.GiamGiaVoucher,
                    MaKhm = model.MaVoucher,
                    GhiChu = model.GhiChu?.Trim(),
                    PhuongThucThanhToan = model.PhuongThucThanhToan
                };

                _db.HoaDons.Add(invoice);
                await _db.SaveChangesAsync();
                createdInvoiceId = invoice.MaHoaDon;

                // Thêm từng dòng chi tiết snapshot từ dữ liệu đơn hàng
                if (order.ChiTietDonHangs != null && order.ChiTietDonHangs.Count > 0)
                {
                    foreach (var ct in order.ChiTietDonHangs)
                    {
                        var variant = await _db.BienTheSanPhams
                            .Include(b => b.MaSanPhamNavigation)
                            .FirstOrDefaultAsync(b => b.MaBienThe == ct.MaBienThe);

                        var detail = new ChiTietHoaDon
                        {
                            MaHoaDon = invoice.MaHoaDon,
                            MaBienThe = ct.MaBienThe,
                            TenSanPham = variant?.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm",
                            BienThe = $"{variant?.MauSac ?? ""} {variant?.PhienBan ?? ""}".Trim(),
                            SoLuong = ct.SoLuong,
                            DonGiaChuaThue = ct.DonGia, // Lưu đơn giá thực tế từ đơn
                            ThueSuatVat = 0m,          // Chưa có xác nhận bảng thuế suất nghiệp vụ
                            TienThueVat = 0m,
                            DonGiaSauThue = ct.DonGia,
                            ChietKhau = 0m,
                            ThanhTien = ct.ThanhTien
                        };
                        _db.ChiTietHoaDons.Add(detail);
                    }
                    await _db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception("Lỗi khi lưu snapshot hóa đơn: " + ex.Message, ex);
            }
        });

        TempData["Success"] = $"Đã lập hóa đơn bán hàng #HD-{createdInvoiceId:D5} thành công!";
        return RedirectToAction(nameof(Details), new { id = createdInvoiceId });
    }

    // 4. XEM CHI TIẾT HÓA ĐƠN TỪ DỮ LIỆU SNAPSHOT
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
                .ThenInclude(d => d.ThanhToans)
            .Include(x => x.MaNguoiLapNavigation)
            .Include(x => x.ChiTietHoaDons)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);

        if (invoice == null)
        {
            TempData["Error"] = $"Không tìm thấy hóa đơn #HD-{id:D5}.";
            return RedirectToAction(nameof(Index));
        }

        // Nếu hóa đơn cũ chưa có chi tiết snapshot trong ChiTietHoaDon, tự động nạp fallback từ DonHang
        if (!invoice.ChiTietHoaDons.Any() && invoice.MaDonHangNavigation != null)
        {
            var orderDetails = await _db.ChiTietDonHangs
                .AsNoTracking()
                .Include(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                .Where(ct => ct.MaDonHang == invoice.MaDonHang)
                .ToListAsync();

            ViewBag.LegacyDetails = orderDetails;
        }

        return View(invoice);
    }

    // 5. IN & XUẤT PDF HÓA ĐƠN BÁN HÀNG NỘI BỘ TỪ SNAPSHOT
    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var invoice = await _db.HoaDons
            .AsNoTracking()
            .Include(x => x.MaDonHangNavigation)
            .Include(x => x.MaNguoiLapNavigation)
            .Include(x => x.ChiTietHoaDons)
            .FirstOrDefaultAsync(x => x.MaHoaDon == id);

        if (invoice == null)
        {
            return NotFound($"Không tìm thấy hóa đơn #HD-{id:D5}");
        }

        // Nạp fallback nếu là hóa đơn cũ chưa có snapshot
        if (!invoice.ChiTietHoaDons.Any() && invoice.MaDonHangNavigation != null)
        {
            var orderDetails = await _db.ChiTietDonHangs
                .AsNoTracking()
                .Include(ct => ct.MaBienTheNavigation)
                    .ThenInclude(bt => bt.MaSanPhamNavigation)
                .Where(ct => ct.MaDonHang == invoice.MaDonHang)
                .ToListAsync();

            ViewBag.LegacyDetails = orderDetails;
        }

        return View(invoice);
    }
}
