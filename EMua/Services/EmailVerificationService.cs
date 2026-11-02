using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMua.Models;

[Table("EmailVerifications", Schema = "public")]
public class EmailVerification
{
    [Key] public long Id { get; set; }
    public int UserId { get; set; }
    [Required, MaxLength(320)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
