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

    [Range(0, 1000000000, ErrorMessage = "Đơn giá nhập không hợp lệ.")]
    public decimal DonGiaNhapDuKien { get; set; }

    public string? GhiChu { get; set; }
}

public class ImportOrderCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp.")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp tối đa 200 ký tự.")]
    public string NhaCungCap { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập lý do đề nghị nhập hàng.")]
    [StringLength(255, ErrorMessage = "Lý do nhập tối đa 255 ký tự.")]
    public string LyDoNhap { get; set; } = "Bổ sung hàng tồn kho đã hết / sắp hết";

    [StringLength(500)]
    public string? DienGiai { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }

    public List<ImportOrderItemViewModel> Items { get; set; } = new();
}
