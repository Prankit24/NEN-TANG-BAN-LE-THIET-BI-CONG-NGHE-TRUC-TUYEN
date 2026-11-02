
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EMua.Services;

public sealed class SmtpEmailSender : IAppEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IConfiguration configuration,
        ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody)
    {
        string GetRequired(string key)
        {
            var value = _configuration[key];

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Thiếu cấu hình {key}.");
            }

            return value;
        }

        // 1. Đọc cấu hình SMTP
        var host = GetRequired("EmailSettings:Host");

        var username = GetRequired("EmailSettings:UserName");

        var from = GetRequired("EmailSettings:FromEmail");

        var password = GetRequired("EmailSettings:Password")
            .Replace(" ", "")
            .Trim();

        var port = _configuration.GetValue<int>(
            "EmailSettings:Port", 587);

        // 2. Kiểm tra cấu hình, không in mật khẩu
        _logger.LogInformation(
            "SMTP CONFIG: UserName={UserName}, PasswordLength={Length}, Environment={Environment}",
            username,
            password.Length,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        );

        if (password.Length != 16)
        {
            _logger.LogWarning(
                "Mật khẩu SMTP có {Length} ký tự. Google App Password thông thường có 16 ký tự.",
                password.Length
            );
        }

        // 3. Tạo nội dung email
        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress("EMUA", from));

        message.To.Add(
            MailboxAddress.Parse(to));

        message.Subject = subject;

        message.Body = new BodyBuilder
        {
            HtmlBody = htmlBody
        }.ToMessageBody();

        // 4. Khởi tạo MailKit SMTP
        using var client = new MailKit.Net.Smtp.SmtpClient();

        client.Timeout = 30000;

        try
        {
            // 5. Kết nối Gmail SMTP
            _logger.LogInformation(
                "Đang kết nối SMTP {Host}:{Port}",
                host,
                port
            );

            await client.ConnectAsync(
                host,
                port,
                SecureSocketOptions.StartTls
            );

            _logger.LogInformation(
                "Kết nối SMTP và STARTTLS thành công."
            );

            // 6. Xác thực Gmail
            _logger.LogInformation(
                "Đang xác thực SMTP với tài khoản {UserName}",
                username
            );

            await client.AuthenticateAsync(
                username,
                password
            );

            _logger.LogInformation(
                "Xác thực SMTP thành công."
            );

            // 7. Gửi email
            await client.SendAsync(message);

            _logger.LogInformation(
                "SMTP đã chấp nhận email gửi đến {Recipient}",
                to
            );

            // 8. Ngắt kết nối
            await client.DisconnectAsync(true);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            _logger.LogError(
                ex,
                "Gmail từ chối xác thực SMTP. Kiểm tra UserName và Google App Password."
            );

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Lỗi gửi email SMTP đến {Recipient}",
                to
            );

            throw;
        }
    }
}
