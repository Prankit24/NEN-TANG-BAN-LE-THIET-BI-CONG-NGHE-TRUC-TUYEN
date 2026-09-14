using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("ChiTietGioHang")]
[Index("MaNguoiDung", "MaBienThe", Name = "UQ_ChiTietGioHang", IsUnique = true)]
public partial class ChiTietGioHang
{
    [Key]
    public int MaChiTietGioHang { get; set; }

    public int MaNguoiDung { get; set; }

    public int MaBienThe { get; set; }

    public int SoLuong { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime NgayThem { get; set; }

    [ForeignKey("MaBienThe")]
    [InverseProperty("ChiTietGioHangs")]
    public virtual BienTheSanPham MaBienTheNavigation { get; set; } = null!;

    [ForeignKey("MaNguoiDung")]
    [InverseProperty("ChiTietGioHangs")]
    public virtual NguoiDung MaNguoiDungNavigation { get; set; } = null!;
}
