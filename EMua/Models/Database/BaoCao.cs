using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMua.Models.Database;

[Table("BaoCao")]
public partial class BaoCao
{
    [Key]
    public int MaBaoCao { get; set; }

    [StringLength(50)]
    public string? LoaiBaoCao { get; set; }

    [Column(TypeName = "timestamp without time zone")]
    public DateTime? NgayBaoCao { get; set; }

    public string? DuLieu { get; set; }
}
