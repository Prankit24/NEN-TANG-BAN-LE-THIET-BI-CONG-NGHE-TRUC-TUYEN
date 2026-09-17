using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class LoginViewModel
{
    [Required(
        ErrorMessage = "Vui lòng nhập email hoặc số điện thoại."
    )]
    [StringLength(
        100,
        ErrorMessage = "Email hoặc số điện thoại không hợp lệ."
    )]
    public string Identifier { get; set; } = string.Empty;


    [Required(
        ErrorMessage = "Vui lòng nhập mật khẩu."
    )]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;


    public bool RememberMe { get; set; }
}