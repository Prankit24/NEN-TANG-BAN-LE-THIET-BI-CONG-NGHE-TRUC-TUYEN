using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels
{
    public class ResetPasswordViewModel
    {
        [Required(ErrorMessage = "Vui lòng cung cấp email.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Mã xác thực không hợp lệ.")]
        public string Token { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
        [StringLength(100, MinimumLength = 6,
            ErrorMessage = "Mật khẩu mới phải có từ 6 đến 100 ký tự.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới.")]
        [Compare("NewPassword",
            ErrorMessage = "Mật khẩu xác nhận không khớp.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }
}