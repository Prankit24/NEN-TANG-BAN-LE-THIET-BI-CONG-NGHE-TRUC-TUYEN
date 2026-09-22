using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("PhanQuyen")]
[Index("TenQuyen", Name = "UQ__PhanQuye__5637EE792105E491", IsUnique = true)]
public partial class PhanQuyen
{
    [Key]
    public int MaQuyen { get; set; }

    [StringLength(50)]
    public string TenQuyen { get; set; } = null!;

    [StringLength(255)]
    public string? MoTa { get; set; }

    [InverseProperty("MaQuyenNavigation")]
    public virtual ICollection<NguoiDung> NguoiDungs { get; set; } = new List<NguoiDung>();
}
