using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("DanhMucSanPham")]
public partial class DanhMucSanPham
{
    [Key]
    public int MaDanhMuc { get; set; }

    [StringLength(100)]
    public string? TenDanhMuc { get; set; }

    [StringLength(255)]
    public string? MoTa { get; set; }

    [StringLength(20)]
    public string? TrangThaiDanhMuc { get; set; }

    [InverseProperty("MaDanhMucNavigation")]
    public virtual ICollection<SanPham> SanPhams { get; set; } = new List<SanPham>();
}
