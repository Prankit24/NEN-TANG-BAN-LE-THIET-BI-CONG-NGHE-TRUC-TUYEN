using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("ThuongHieu")]
[Index("TenThuongHieu", Name = "UQ__ThuongHi__98D6A834D059DBAD", IsUnique = true)]
public partial class ThuongHieu
{
    [Key]
    public int MaThuongHieu { get; set; }

    [StringLength(100)]
    public string TenThuongHieu { get; set; } = null!;

    [StringLength(255)]
    public string? Logo { get; set; }

    [StringLength(500)]
    public string? MoTa { get; set; }

    public bool TrangThai { get; set; }

    [InverseProperty("MaThuongHieuNavigation")]
    public virtual ICollection<SanPham> SanPhams { get; set; } = new List<SanPham>();
}
