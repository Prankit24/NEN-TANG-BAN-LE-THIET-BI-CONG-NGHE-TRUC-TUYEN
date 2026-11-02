
using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
    public string Identifier { get; set; } = string.Empty;

    // Giữ tương thích với code cũ sử dụng Email
    public string? Email
    {
        get => Identifier;
        set => Identifier = value ?? string.Empty;
    }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
