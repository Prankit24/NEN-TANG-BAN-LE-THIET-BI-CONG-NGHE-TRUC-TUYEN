using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("TinNhanAI")]
public partial class TinNhanAi
{
    [Key]
    public int MaTinNhan { get; set; }

    public int MaCuocHoiThoai { get; set; }

    [StringLength(20)]
    public string VaiTro { get; set; } = null!;

    public string NoiDung { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime NgayGui { get; set; }

    [ForeignKey("MaCuocHoiThoai")]
    [InverseProperty("TinNhanAis")]
    public virtual CuocHoiThoaiAi MaCuocHoiThoaiNavigation { get; set; } = null!;
}
