namespace EMua.Services;

/// <summary>
/// Gửi email HTML của hệ thống đến địa chỉ người nhận tùy ý.
/// </summary>
public interface IAppEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}
