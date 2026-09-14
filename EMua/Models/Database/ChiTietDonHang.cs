using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("ChiTietDonHang")]
[Index("MaBienThe", Name = "IX_ChiTietDonHang_MaBienThe")]
public partial class ChiTietDonHang
{
    [Key]
    public int MaChiTietDonHang { get; set; }

    public int MaDonHang { get; set; }

    public int SoLuong { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal DonGia { get; set; }

    public int MaBienThe { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal ThanhTien { get; set; }

    [ForeignKey("MaBienThe")]
    [InverseProperty("ChiTietDonHangs")]
    public virtual BienTheSanPham MaBienTheNavigation { get; set; } = null!;

    [ForeignKey("MaDonHang")]
    [InverseProperty("ChiTietDonHangs")]
    public virtual DonHang MaDonHangNavigation { get; set; } = null!;
}
