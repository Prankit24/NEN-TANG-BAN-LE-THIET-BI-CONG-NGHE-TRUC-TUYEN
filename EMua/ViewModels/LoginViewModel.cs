using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email / tên đăng nhập.")]
    [StringLength(100, ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    public string Identifier
    {
        get => Email;
        set => Email = value;
    }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}