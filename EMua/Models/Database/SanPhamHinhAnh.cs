using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("SanPhamHinhAnh")]
public partial class SanPhamHinhAnh
{
    [Key]
    public int MaAnh { get; set; }

    public int MaSanPham { get; set; }

    [StringLength(255)]
    public string DuongDanAnh { get; set; } = null!;

    public bool LaAnhChinh { get; set; }

    public int ThuTu { get; set; }

    [ForeignKey("MaSanPham")]
    [InverseProperty("SanPhamHinhAnhs")]
    public virtual SanPham MaSanPhamNavigation { get; set; } = null!;
}
