using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using EMua.Data;
using EMua.Models;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services;

public class EmailVerificationService
{
    private readonly EMuaDbContext _db;
    private readonly IAppEmailSender _sender;
    private readonly IConfiguration _config;

    public EmailVerificationService(EMuaDbContext db, IAppEmailSender sender, IConfiguration config)
    {
        _db = db; _sender = sender; _config = config;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Chỉ tài khoản mới có bản ghi chờ xác minh bị chặn.
    // Tài khoản cũ không có bản ghi được giữ nguyên trạng thái.
    public async Task<bool> IsPendingAsync(int userId) =>
        await _db.EmailVerifications.AnyAsync(x => x.UserId == userId && x.VerifiedAtUtc == null);

    public async Task SendAsync(int userId, string email)
    {
        var baseUrl = _config["App:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || root.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("App:BaseUrl phải là URL HTTPS hợp lệ.");

        var normalized = email.Trim().ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var existing = await _db.EmailVerifications
            .Where(x => x.UserId == userId && x.VerifiedAtUtc == null).ToListAsync();
        _db.EmailVerifications.RemoveRange(existing);
        _db.EmailVerifications.Add(new EmailVerification
        {
            UserId = userId,
            Email = normalized,
            TokenHash = Hash(token),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var url = new Uri(root, "/Auth/VerifyEmail?token=" + Uri.EscapeDataString(token)).AbsoluteUri;
        var safeUrl = HtmlEncoder.Default.Encode(url);
        var html = $"<h2>Xác minh email EMUA</h2><p>Nhấn vào liên kết sau để kích hoạt tài khoản:</p>" +
                   $"<p><a href=\"{safeUrl}\">Xác minh email</a></p>" +
                   "<p>Liên kết có hiệu lực 24 giờ. Nếu không đăng ký, hãy bỏ qua email này.</p>";
        await _sender.SendAsync(normalized, "EMUA - Xác minh email", html);
    }

    public async Task<bool> VerifyAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64 ||
            !System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Fa-f0-9]{64}$"))
            return false;

        var hash = Hash(token);
        var row = await _db.EmailVerifications.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (row == null || row.VerifiedAtUtc != null || row.ExpiresAtUtc <= DateTime.UtcNow) return false;

        var user = await _db.NguoiDungs.AsNoTracking().FirstOrDefaultAsync(x => x.MaNguoiDung == row.UserId);
        if (user == null || !string.Equals(user.Email?.Trim(), row.Email, StringComparison.OrdinalIgnoreCase))
            return false;

        // Atomic update: hai lần bấm đồng thời chỉ một lần thành công.
        var changed = await _db.EmailVerifications
            .Where(x => x.Id == row.Id && x.VerifiedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VerifiedAtUtc, DateTime.UtcNow));
        return changed == 1;
    }
}
