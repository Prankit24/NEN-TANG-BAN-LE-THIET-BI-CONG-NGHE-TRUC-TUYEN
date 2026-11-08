using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("chitiethoadon", Schema = "public")]
[Index("MaHoaDon", Name = "idx_chitiethoadon_mahoadon")]
public partial class ChiTietHoaDon
{
    [Key]
    public int MaChiTietHoaDon { get; set; }

    public int MaHoaDon { get; set; }

    public int? MaBienThe { get; set; }

    [StringLength(255)]
    public string TenSanPham { get; set; } = string.Empty;

    [StringLength(100)]
    public string? BienThe { get; set; }

    public int SoLuong { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal DonGiaChuaThue { get; set; }

    [Column(TypeName = "decimal(5, 2)")]
    public decimal ThueSuatVat { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal TienThueVat { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal DonGiaSauThue { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal ChietKhau { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal ThanhTien { get; set; }

    [ForeignKey("MaHoaDon")]
    [InverseProperty("ChiTietHoaDons")]
    public virtual HoaDon MaHoaDonNavigation { get; set; } = null!;
}
