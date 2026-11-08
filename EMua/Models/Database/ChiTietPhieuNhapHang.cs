using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models.Database;

[Table("ChiTietPhieuNhapHang")]
public partial class ChiTietPhieuNhapHang
{
    [Key]
    public int MaChiTietPhieuNhap { get; set; }

    public int MaPhieuNhap { get; set; }

    public int MaBienThe { get; set; }

    public int SoLuongNhap { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGiaNhapDuKien { get; set; }

    [StringLength(255)]
    public string? GhiChu { get; set; }

    [ForeignKey("MaPhieuNhap")]
    public virtual PhieuNhapHang MaPhieuNhapNavigation { get; set; } = null!;

    [ForeignKey("MaBienThe")]
    public virtual BienTheSanPham MaBienTheNavigation { get; set; } = null!;
}
