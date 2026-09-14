using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("BienTheSanPham")]
[Index("MaSanPham", Name = "IX_BienTheSanPham_MaSanPham")]
public partial class BienTheSanPham
{
    [Key]
    public int MaBienThe { get; set; }

    public int MaSanPham { get; set; }

    [StringLength(50)]
    public string? MauSac { get; set; }

    [StringLength(100)]
    public string? PhienBan { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Gia { get; set; }

    public int SoLuong { get; set; }

    [StringLength(255)]
    public string? HinhAnh { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = null!;

    [InverseProperty("MaBienTheNavigation")]
    public virtual ICollection<ChiTietDonHang> ChiTietDonHangs { get; set; } = new List<ChiTietDonHang>();

    [InverseProperty("MaBienTheNavigation")]
    public virtual ICollection<ChiTietGioHang> ChiTietGioHangs { get; set; } = new List<ChiTietGioHang>();

    [ForeignKey("MaSanPham")]
    [InverseProperty("BienTheSanPhams")]
    public virtual SanPham MaSanPhamNavigation { get; set; } = null!;
}
