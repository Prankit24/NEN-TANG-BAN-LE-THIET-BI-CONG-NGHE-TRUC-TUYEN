using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("hoadon", Schema = "public")]
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

    // Các cột Snapshot bổ sung
    public int? MaNguoiLap { get; set; }

    [StringLength(100)]
    public string? TenNguoiLap { get; set; }

    [StringLength(100)]
    public string? HoTenKhachHang { get; set; }

    [StringLength(20)]
    public string? SoDienThoai { get; set; }

    [StringLength(255)]
    public string? DiaChiGiaoHang { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? TienTruocThue { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? TienThueVat { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? PhiVanChuyen { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? TienGiamGia { get; set; }

    [StringLength(50)]
    public string? MaKhm { get; set; }

    [StringLength(500)]
    public string? GhiChu { get; set; }

    [StringLength(50)]
    public string? PhuongThucThanhToan { get; set; }

    [ForeignKey("MaDonHang")]
    [InverseProperty("HoaDon")]
    public virtual DonHang MaDonHangNavigation { get; set; } = null!;

    [ForeignKey("MaNguoiLap")]
    [InverseProperty("HoaDons")]
    public virtual NguoiDung? MaNguoiLapNavigation { get; set; }

    [InverseProperty("MaHoaDonNavigation")]
    public virtual ICollection<ChiTietHoaDon> ChiTietHoaDons { get; set; } = new List<ChiTietHoaDon>();
}
