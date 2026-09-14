using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("YeuThich")]
[Index("MaNguoiDung", "MaSanPham", Name = "UQ_YeuThich", IsUnique = true)]
public partial class YeuThich
{
    [Key]
    public int MaYeuThich { get; set; }

    public int MaNguoiDung { get; set; }

    public int MaSanPham { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime NgayThem { get; set; }

    [ForeignKey("MaNguoiDung")]
    [InverseProperty("YeuThiches")]
    public virtual NguoiDung MaNguoiDungNavigation { get; set; } = null!;

    [ForeignKey("MaSanPham")]
    [InverseProperty("YeuThiches")]
    public virtual SanPham MaSanPhamNavigation { get; set; } = null!;
}
