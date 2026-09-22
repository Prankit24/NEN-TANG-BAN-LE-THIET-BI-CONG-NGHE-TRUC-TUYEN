using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("DanhGiaSanPham")]
public partial class DanhGiaSanPham
{
    [Key]
    public int MaDanhGia { get; set; }

    public int? MaNguoiDung { get; set; }

    public int? MaSanPham { get; set; }

    public int? SoLuongSao { get; set; }

    [StringLength(255)]
    public string? BinhLuan { get; set; }

    public int? MaDonHang { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayDanhGia { get; set; }

    public bool? TrangThai { get; set; }

    [ForeignKey("MaDonHang")]
    [InverseProperty("DanhGiaSanPhams")]
    public virtual DonHang? MaDonHangNavigation { get; set; }

    [ForeignKey("MaNguoiDung")]
    [InverseProperty("DanhGiaSanPhams")]
    public virtual NguoiDung? MaNguoiDungNavigation { get; set; }

    [ForeignKey("MaSanPham")]
    [InverseProperty("DanhGiaSanPhams")]
    public virtual SanPham? MaSanPhamNavigation { get; set; }
}
