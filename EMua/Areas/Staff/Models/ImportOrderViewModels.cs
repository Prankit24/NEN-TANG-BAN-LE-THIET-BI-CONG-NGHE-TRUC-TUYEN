using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EMua.Areas.Staff.Models;

public class ImportOrderItemViewModel
{
    public int MaBienThe { get; set; }
    public string? TenSanPham { get; set; }
    public string? MauSac { get; set; }
    public string? PhienBan { get; set; }
    public string? HinhAnh { get; set; }
    public int TonKhoHienTai { get; set; }

    [Range(1, 100000, ErrorMessage = "Số lượng nhập tối thiểu là 1.")]
    public int SoLuongNhap { get; set; } = 10;

    // Giá nhập trước thuế VAT
    public decimal DonGiaChuaThue { get; set; }

    // Thuế suất VAT đầu vào (%)
    public decimal ThueSuatVat { get; set; } = 10.00m;

    // Tiền VAT đầu vào trên 1 đơn vị
    public decimal TienThueVat { get; set; }

    // Giá nhập sau VAT (dự kiến)
    [Range(0, 1000000000, ErrorMessage = "Đơn giá nhập không hợp lệ.")]
    public decimal DonGiaNhapDuKien { get; set; }

    // Chiết khấu đơn vị
    public decimal ChietKhau { get; set; } = 0.00m;

    // Giá bán niêm yết hiện tại
    public decimal GiaBanHienTai { get; set; }

    // Lợi nhuận gộp dự kiến trên 1 đơn vị
    public decimal LoiNhuanGopDuKien => Math.Max(0, GiaBanHienTai - DonGiaNhapDuKien);

    // Tỷ suất lợi nhuận dự kiến (%)
    public decimal TySuatLoiNhuan => GiaBanHienTai > 0 ? (LoiNhuanGopDuKien / GiaBanHienTai) * 100 : 0;

    public string? GhiChu { get; set; }
}

public class SupplierQuickCreateModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp.")]
    [StringLength(200)]
    public string TenNhaCungCap { get; set; } = string.Empty;

    [StringLength(20)]
    public string? SoDienThoai { get; set; }

    [StringLength(100)]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    public string? Email { get; set; }

    [StringLength(255)]
    public string? DiaChi { get; set; }

    [StringLength(50)]
    public string? MaSoThue { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }
}

public class ImportOrderCreateViewModel
{
    public int? MaNhaCungCap { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hoặc nhập tên nhà cung cấp.")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp tối đa 200 ký tự.")]
    public string NhaCungCap { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lý do đề nghị nhập hàng.")]
    [StringLength(255, ErrorMessage = "Lý do nhập tối đa 255 ký tự.")]
    public string LyDoNhap { get; set; } = "Bổ sung hàng tồn kho đã hết / sắp hết";

    [StringLength(500)]
    public string? DienGiai { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }

    // Chi phí & Thuế tổng quát của phiếu nhập
    public decimal TienHangChuaThue { get; set; }
    public decimal TienThueVat { get; set; }
    public decimal ChiPhiVanChuyen { get; set; }
    public decimal ChiPhiKhac { get; set; }
    public decimal ChietKhauTong { get; set; }
    public decimal TongThanhToanDuKien { get; set; }

    public List<ImportOrderItemViewModel> Items { get; set; } = new();
}
