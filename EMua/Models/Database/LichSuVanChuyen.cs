using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("LichSuVanChuyen")]
public partial class LichSuVanChuyen
{
    [Key]
    public int MaLichSu { get; set; }

    public int MaDonHang { get; set; }

    [StringLength(50)]
    public string TrangThai { get; set; } = null!;

    [StringLength(500)]
    public string? MoTa { get; set; }

    [StringLength(255)]
    public string? DiaDiem { get; set; }

    [StringLength(255)]
    public string? HinhAnhNguoiNhan { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime ThoiGian { get; set; }

    [ForeignKey("MaDonHang")]
    [InverseProperty("LichSuVanChuyens")]
    public virtual DonHang MaDonHangNavigation { get; set; } = null!;
}
