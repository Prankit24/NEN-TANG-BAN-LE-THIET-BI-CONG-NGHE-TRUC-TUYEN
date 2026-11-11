using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models.Database;

[Table("NhaCungCap")]
public partial class NhaCungCap
{
    [Key]
    public int MaNhaCungCap { get; set; }

    [StringLength(200)]
    public string TenNhaCungCap { get; set; } = null!;

    [StringLength(50)]
    public string? MaSoThue { get; set; }

    [StringLength(20)]
    public string? SoDienThoai { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(255)]
    public string? DiaChi { get; set; }

    [StringLength(100)]
    public string? NguoiLienHe { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = "Đang hợp tác";

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;

    public virtual ICollection<BaoGiaNhapHang> BaoGiaNhapHangs { get; set; } = new List<BaoGiaNhapHang>();
    public virtual ICollection<PhieuNhapHang> PhieuNhapHangs { get; set; } = new List<PhieuNhapHang>();
}
