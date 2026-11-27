using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models.Database;

[Table("LichSuKiemDuyetDanhGia")]
public class LichSuKiemDuyetDanhGia
{
    [Key]
    public int MaLichSu { get; set; }

    public int MaDanhGia { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hành động kiểm duyệt.")]
    [StringLength(50, ErrorMessage = "Hành động không được vượt quá 50 ký tự.")]
    public string HanhDong { get; set; } = "Ẩn"; // "Ẩn" hoặc "Khôi phục"

    [Required(ErrorMessage = "Vui lòng nhập lý do vi phạm.")]
    [StringLength(255, ErrorMessage = "Lý do vi phạm không được vượt quá 255 ký tự.")]
    public string LyDoViPham { get; set; } = string.Empty;

    [StringLength(500)]
    public string? GhiChuChiTiet { get; set; }

    public int? MaNguoiKiemDuyet { get; set; }

    [StringLength(100)]
    public string? TenNguoiKiemDuyet { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayThucHien { get; set; } = DateTime.Now;

    [ForeignKey("MaDanhGia")]
    public virtual DanhGiaSanPham? DanhGiaNavigation { get; set; }

    [ForeignKey("MaNguoiKiemDuyet")]
    public virtual NguoiDung? NguoiKiemDuyetNavigation { get; set; }
}
