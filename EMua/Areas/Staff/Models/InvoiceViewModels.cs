using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EMua.Areas.Staff.Models;

public class InvoiceItemViewModel
{
    public int? MaBienThe { get; set; }
    public string TenSanPham { get; set; } = string.Empty;
    public string? BienThe { get; set; }
    public int SoLuong { get; set; }

    /// <summary>Đơn giá bán niêm yết từ đơn hàng (đã gồm VAT theo quy định bán lẻ thương mại điện tử)</summary>
    public decimal DonGiaGoc { get; set; }

    /// <summary>Thuế suất VAT (%) xác định từ căn cứ pháp lý</summary>
    public decimal? ThueSuatVat { get; set; }

    /// <summary>Đơn giá trước thuế</summary>
    public decimal? DonGiaChuaThue { get; set; }

    /// <summary>Tiền thuế VAT cho 1 đơn vị sản phẩm</summary>
    public decimal? TienThueVat { get; set; }

    /// <summary>Đơn giá sau thuế (đã gồm VAT)</summary>
    public decimal DonGiaSauThue { get; set; }

    /// <summary>Chiết khấu riêng cho dòng sản phẩm</summary>
    public decimal ChietKhau { get; set; }

    /// <summary>Thành tiền = SoLuong * DonGiaGoc - ChietKhau</summary>
    public decimal ThanhTien { get; set; }

    public string? HinhAnh { get; set; }
    public string? DanhMuc { get; set; }
    public string? CanCuPhapLyThue { get; set; }
}

public class VatRateSummaryItem
{
    public decimal ThueSuatVat { get; set; }
    public decimal TienChuaThue { get; set; }
    public decimal TienThueVat { get; set; }
    public decimal TongTienSauThue { get; set; }
}

public class InvoiceCreateViewModel
{
    public int MaDonHang { get; set; }
    public DateTime NgayDatHang { get; set; }
    public string TrangThaiDonHang { get; set; } = string.Empty;

    // Thông tin người bán (EMUA demo)
    public string TenNguoiBan { get; set; } = "CÔNG TY CỔ PHẦN CÔNG NGHỆ EMUA (DEMO)";
    public string MaSoThueNguoiBan { get; set; } = "0109999999-DEMO";
    public string DiaChiNguoiBan { get; set; } = "Số 1 Đại Cồ Việt, P. Bách Khoa, Q. Hai Bà Trưng, Hà Nội";

    // Thông tin khách hàng & giao nhận
    [Display(Name = "Họ tên người mua / nhận hàng")]
    [Required(ErrorMessage = "Vui lòng nhập họ tên khách hàng")]
    public string HoTenKhachHang { get; set; } = string.Empty;

    [Display(Name = "Số điện thoại")]
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
    public string SoDienThoai { get; set; } = string.Empty;

    [Display(Name = "Địa chỉ nhận hàng")]
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ nhận hàng")]
    public string DiaChiGiaoHang { get; set; } = string.Empty;

    [Display(Name = "Mã số thuế người mua (nếu có)")]
    public string? MaSoThueNguoiMua { get; set; }

    public string? PhuongThucThanhToan { get; set; }
    public string? PhuongThucVanChuyen { get; set; }

    // Người lập hóa đơn (Staff)
    public int? MaNguoiLap { get; set; }
    public string? TenNguoiLap { get; set; }

    // Tổng hợp tài chính
    public decimal? TongTienTruocThue { get; set; }
    public decimal? TongTienThueVat { get; set; }
    public decimal TongTienHang { get; set; }
    public decimal PhiVanChuyen { get; set; }
    public decimal GiamGiaVoucher { get; set; }
    public string? MaVoucher { get; set; }
    public decimal TongThanhToan { get; set; }

    [Display(Name = "Ghi chú hóa đơn")]
    [StringLength(500)]
    public string? GhiChu { get; set; }

    // Danh sách mặt hàng
    public List<InvoiceItemViewModel> Items { get; set; } = new();

    // Bảng tổng hợp theo từng mức thuế suất
    public List<VatRateSummaryItem> VatSummaries { get; set; } = new();
}

public class InvoiceReportFilterViewModel
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Status { get; set; }
    public string? Staff { get; set; }

    // Thống kê tổng hợp
    public int TotalInvoices { get; set; }
    public decimal TotalNetRevenue { get; set; }
    public decimal TotalVatAmount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalGrossRevenue { get; set; }
    public Dictionary<decimal, decimal> VatAmountByRate { get; set; } = new();
}
