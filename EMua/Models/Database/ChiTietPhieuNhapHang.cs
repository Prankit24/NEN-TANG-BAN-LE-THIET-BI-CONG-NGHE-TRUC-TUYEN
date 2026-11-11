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

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGiaChuaThue { get; set; } = 0.00m;

    [Column(TypeName = "decimal(5, 2)")]
    public decimal ThueSuatVat { get; set; } = 10.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TienThueVat { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGiaSauThue { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal ChietKhau { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal ThanhTien { get; set; } = 0.00m;

    [StringLength(255)]
    public string? GhiChu { get; set; }

    [ForeignKey("MaPhieuNhap")]
    public virtual PhieuNhapHang MaPhieuNhapNavigation { get; set; } = null!;

    [ForeignKey("MaBienThe")]
    public virtual BienTheSanPham MaBienTheNavigation { get; set; } = null!;
}
