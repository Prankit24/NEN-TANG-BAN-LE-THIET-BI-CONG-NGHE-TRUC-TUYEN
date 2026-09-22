using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("SanPham")]
[Index("MaThuongHieu", Name = "IX_SanPham_MaThuongHieu")]
public partial class SanPham
{
    [Key]
    public int MaSanPham { get; set; }

    public int? MaDanhMuc { get; set; }

    [StringLength(100)]
    public string? TenSanPham { get; set; }

    [StringLength(255)]
    public string? MoTa { get; set; }

    [StringLength(20)]
    public string? TrangThaiSanPham { get; set; }

    [StringLength(50)]
    public string? BaoHanh { get; set; }

    public string? ThongSoKyThuat { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal? GiaGoc { get; set; }

    public int MaThuongHieu { get; set; }

    [InverseProperty("MaSanPhamNavigation")]
    public virtual ICollection<BienTheSanPham> BienTheSanPhams { get; set; } = new List<BienTheSanPham>();

    [InverseProperty("MaSanPhamNavigation")]
    public virtual ICollection<DanhGiaSanPham> DanhGiaSanPhams { get; set; } = new List<DanhGiaSanPham>();

    [ForeignKey("MaDanhMuc")]
    [InverseProperty("SanPhams")]
    public virtual DanhMucSanPham? MaDanhMucNavigation { get; set; }

    [ForeignKey("MaThuongHieu")]
    [InverseProperty("SanPhams")]
    public virtual ThuongHieu MaThuongHieuNavigation { get; set; } = null!;

    [InverseProperty("MaSanPhamNavigation")]
    public virtual ICollection<SanPhamHinhAnh> SanPhamHinhAnhs { get; set; } = new List<SanPhamHinhAnh>();

    [InverseProperty("MaSanPhamNavigation")]
    public virtual ICollection<YeuThich> YeuThiches { get; set; } = new List<YeuThich>();
}
