using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("KhuyenMai")]
[Index("MaCode", Name = "UQ__KhuyenMa__152C7C5CF41E5F9B", IsUnique = true)]
public partial class KhuyenMai
{
    [Key]
    public int MaKhuyenMai { get; set; }

    [StringLength(50)]
    public string MaCode { get; set; } = null!;

    [StringLength(150)]
    public string TenKhuyenMai { get; set; } = null!;

    [StringLength(30)]
    public string LoaiGiamGia { get; set; } = null!;

    [Column(TypeName = "decimal(12, 2)")]
    public decimal GiaTriGiam { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? GiaTriDonHangToiThieu { get; set; }

    public int? SoLuong { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? NgayBatDau { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? NgayKetThuc { get; set; }

    public bool TrangThai { get; set; }

    [StringLength(255)]
    public string? MoTa { get; set; }

    [InverseProperty("MaKhuyenMaiNavigation")]
    public virtual ICollection<DonHang> DonHangs { get; set; } = new List<DonHang>();
}
