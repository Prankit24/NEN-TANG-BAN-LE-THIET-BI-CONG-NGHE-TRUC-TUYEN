using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("HoaDon")]
[Index("MaDonHang", Name = "UQ_HoaDon_MaDonHang", IsUnique = true)]
public partial class HoaDon
{
    [Key]
    public int MaHoaDon { get; set; }

    public int MaDonHang { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayLapHoaDon { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? TongTien { get; set; }

    [StringLength(50)]
    public string? TrangThaiHoaDon { get; set; }

    [ForeignKey("MaDonHang")]
    [InverseProperty("HoaDon")]
    public virtual DonHang MaDonHangNavigation { get; set; } = null!;
}
