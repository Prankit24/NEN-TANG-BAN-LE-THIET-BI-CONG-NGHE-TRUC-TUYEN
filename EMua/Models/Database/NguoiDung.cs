using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("NguoiDung")]
[Index("Email", Name = "IX_NguoiDung_Email")]
[Index("MaQuyen", Name = "IX_NguoiDung_MaQuyen")]
[Index("TenNguoiDung", Name = "IX_NguoiDung_TenNguoiDung")]
public partial class NguoiDung
{
    [Key]
    public int MaNguoiDung { get; set; }

    [StringLength(50)]
    public string? TenDangNhap { get; set; }

    [StringLength(255)]
    public string? MatKhau { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(15)]
    public string? SoDienThoai { get; set; }

    [StringLength(255)]
    public string? DiaChi { get; set; }

    public bool TrangThai { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? NgayTao { get; set; }

    [StringLength(100)]
    public string? TenNguoiDung { get; set; }

    public int MaQuyen { get; set; }

    [InverseProperty("MaNguoiDungNavigation")]
    public virtual ICollection<ChiTietGioHang> ChiTietGioHangs { get; set; } = new List<ChiTietGioHang>();

    [InverseProperty("MaNguoiDungNavigation")]
    public virtual ICollection<CuocHoiThoaiAi> CuocHoiThoaiAis { get; set; } = new List<CuocHoiThoaiAi>();

    [InverseProperty("MaNguoiDungNavigation")]
    public virtual ICollection<DanhGiaSanPham> DanhGiaSanPhams { get; set; } = new List<DanhGiaSanPham>();

    [InverseProperty("MaNguoiDungNavigation")]
    public virtual ICollection<DonHang> DonHangs { get; set; } = new List<DonHang>();

    [ForeignKey("MaQuyen")]
    [InverseProperty("NguoiDungs")]
    public virtual PhanQuyen MaQuyenNavigation { get; set; } = null!;

    [InverseProperty("MaNguoiDungNavigation")]
    public virtual ICollection<YeuThich> YeuThiches { get; set; } = new List<YeuThich>();
}
