using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("TuCam")]
[Index("NoiDung", Name = "UQ__TuCam__C69AFF423679873D", IsUnique = true)]
public partial class TuCam
{
    [Key]
    public int MaTuCam { get; set; }

    [StringLength(255)]
    public string NoiDung { get; set; } = null!;

    public bool TrangThai { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime NgayTao { get; set; }
}
