using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models.Database;

[Table("BaoGiaNhapHang")]
public partial class BaoGiaNhapHang
{
    [Key]
    public int MaBaoGia { get; set; }

    public int MaNhaCungCap { get; set; }

    public int MaBienThe { get; set; }

    public int SoLuongToiThieu { get; set; } = 1;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGiaChuaThue { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal ThueSuatVat { get; set; } = 10.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TienThueVat { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGiaSauThue { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal ChietKhau { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal ChiPhiVanChuyenUocTinh { get; set; } = 0.00m;

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayHieuLuc { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayHetHan { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = "Hiệu lực";

    [StringLength(500)]
    public string? GhiChu { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayCapNhat { get; set; } = DateTime.UtcNow;

    [ForeignKey("MaNhaCungCap")]
    public virtual NhaCungCap MaNhaCungCapNavigation { get; set; } = null!;

    [ForeignKey("MaBienThe")]
    public virtual BienTheSanPham MaBienTheNavigation { get; set; } = null!;
}
