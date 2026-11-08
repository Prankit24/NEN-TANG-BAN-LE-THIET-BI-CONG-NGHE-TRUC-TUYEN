using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models.Database;

[Table("PhieuNhapHang")]
public partial class PhieuNhapHang
{
    [Key]
    public int MaPhieuNhap { get; set; }

    [StringLength(50)]
    public string MaChungTu { get; set; } = null!;

    [StringLength(200)]
    public string NhaCungCap { get; set; } = null!;

    [StringLength(255)]
    public string LyDoNhap { get; set; } = null!;

    [StringLength(500)]
    public string? DienGiai { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = "Chờ duyệt";

    public int MaNguoiTao { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayTao { get; set; } = DateTime.UtcNow;

    public int? MaNguoiDuyet { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayDuyet { get; set; }

    [StringLength(500)]
    public string? LyDoTuChoi { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TongTienDuKien { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayNhapKho { get; set; }

    public int? MaNguoiNhapKho { get; set; }

    [ForeignKey("MaNguoiTao")]
    public virtual NguoiDung MaNguoiTaoNavigation { get; set; } = null!;

    [ForeignKey("MaNguoiDuyet")]
    public virtual NguoiDung? MaNguoiDuyetNavigation { get; set; }

    [ForeignKey("MaNguoiNhapKho")]
    public virtual NguoiDung? MaNguoiNhapKhoNavigation { get; set; }

    public virtual ICollection<ChiTietPhieuNhapHang> ChiTietPhieuNhapHangs { get; set; } = new List<ChiTietPhieuNhapHang>();
}
