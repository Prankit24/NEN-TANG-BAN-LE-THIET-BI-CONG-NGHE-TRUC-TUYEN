using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("CuocHoiThoaiAI")]
public partial class CuocHoiThoaiAi
{
    [Key]
    public int MaCuocHoiThoai { get; set; }

    public int MaNguoiDung { get; set; }

    [StringLength(255)]
    public string? TieuDe { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime NgayTao { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayCapNhat { get; set; }

    [ForeignKey("MaNguoiDung")]
    [InverseProperty("CuocHoiThoaiAis")]
    public virtual NguoiDung MaNguoiDungNavigation { get; set; } = null!;

    [InverseProperty("MaCuocHoiThoaiNavigation")]
    public virtual ICollection<TinNhanAi> TinNhanAis { get; set; } = new List<TinNhanAi>();
}
