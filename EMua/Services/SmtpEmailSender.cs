using System.Net;
using System.Net.Mail;
using System.Text;

namespace EMua.Services;

/// <summary>
/// Gửi mail bằng SMTP Gmail (STARTTLS, cổng 587).
/// Cấu hình bí mật (EmailSettings:Password) phải nằm trong User Secrets.
/// </summary>
public sealed class SmtpEmailSender : IAppEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        string GetRequired(string key) =>
            !string.IsNullOrWhiteSpace(_configuration[key])
                ? _configuration[key]!
                : throw new InvalidOperationException($"Thiếu cấu hình {key}.");

        var host = GetRequired("EmailSettings:Host");
        var username = GetRequired("EmailSettings:UserName");
        var password = _configuration["EmailSettings:Password"]
                    ?? _configuration["GeeTest:EmailSettings:Password"]
                    ?? throw new InvalidOperationException("Thiếu cấu hình EmailSettings:Password.");
        var from = GetRequired("EmailSettings:FromEmail");

        if (!int.TryParse(GetRequired("EmailSettings:Port"), out var port) ||
            port <= 0 || port > 65535)
        {
            throw new InvalidOperationException("EmailSettings:Port không hợp lệ.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(from, "EMUA"),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = htmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to)); // to = email khách đã đăng ký

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = true, // STARTTLS, phù hợp smtp.gmail.com:587
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20000
        };

        await smtp.SendMailAsync(message);
    }
}
