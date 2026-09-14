using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("ThanhToan")]
public partial class ThanhToan
{
    [Key]
    public int MaThanhToan { get; set; }

    public int MaDonHang { get; set; }

    [StringLength(50)]
    public string PhuongThuc { get; set; } = null!;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal SoTien { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = null!;

    [StringLength(255)]
    public string? MaGiaoDich { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime NgayTao { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? NgayThanhToan { get; set; }

    [ForeignKey("MaDonHang")]
    [InverseProperty("ThanhToans")]
    public virtual DonHang MaDonHangNavigation { get; set; } = null!;
}
