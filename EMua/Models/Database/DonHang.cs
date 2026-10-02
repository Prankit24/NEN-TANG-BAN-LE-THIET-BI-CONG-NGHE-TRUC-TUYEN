using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("DonHang")]
public partial class DonHang
{
    [Key]
    public int MaDonHang { get; set; }

    public int? MaNguoiDung { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayDat { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? TongTien { get; set; }

    [StringLength(50)]
    public string? TrangThaiDonHang { get; set; }

    [StringLength(100)]
    public string? HoTenNhanHang { get; set; }

    [StringLength(15)]
    public string? SoDienThoaiNhanHang { get; set; }

    [StringLength(255)]
    public string? DiaChiNhanHang { get; set; }

    [StringLength(255)]
    public string? GhiChu { get; set; }

    [StringLength(50)]
    public string? PhuongThucVanChuyen { get; set; }

    public int? MaKhuyenMai { get; set; }

    [InverseProperty("MaDonHangNavigation")]
    public virtual ICollection<ChiTietDonHang> ChiTietDonHangs { get; set; } = new List<ChiTietDonHang>();

    [InverseProperty("MaDonHangNavigation")]
    public virtual ICollection<DanhGiaSanPham> DanhGiaSanPhams { get; set; } = new List<DanhGiaSanPham>();

    [InverseProperty("MaDonHangNavigation")]
    public virtual HoaDon? HoaDon { get; set; }

    [InverseProperty("MaDonHangNavigation")]
    public virtual ICollection<LichSuVanChuyen> LichSuVanChuyens { get; set; } = new List<LichSuVanChuyen>();

    [ForeignKey("MaKhuyenMai")]
    [InverseProperty("DonHangs")]
    public virtual KhuyenMai? MaKhuyenMaiNavigation { get; set; }

    [ForeignKey("MaNguoiDung")]
    [InverseProperty("DonHangs")]
    public virtual NguoiDung? MaNguoiDungNavigation { get; set; }

    [InverseProperty("MaDonHangNavigation")]
    public virtual ICollection<ThanhToan> ThanhToans { get; set; } = new List<ThanhToan>();
    
    /// <summary>Ngày dự kiến giao hàng, nhân viên nhập khi đơn đang đóng gói/vận chuyển.</summary>
    public DateTime? NgayDuKienGiao { get; set; }

    /// <summary>Đường dẫn ảnh xác nhận đã giao hàng (do nhân viên upload khi hoàn tất).</summary>
    public string? AnhXacNhanGiao { get; set; }
}

