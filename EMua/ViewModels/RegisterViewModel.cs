
using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class RegisterViewModel
{
    // =========================
    // HỌ VÀ TÊN
    // =========================

    [Required(
        ErrorMessage = "Vui lòng nhập họ và tên."
    )]
    [StringLength(
        100,
        MinimumLength = 2,
        ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự."
    )]
    [RegularExpression(
        @"^[a-zA-ZÀ-ỹĐđ\s'.-]+$",
        ErrorMessage = "Họ tên chỉ được chứa chữ cái và khoảng trắng."
    )]
    public string FullName { get; set; } = string.Empty;


    // =========================
    // SỐ ĐIỆN THOẠI
    // =========================

    [Required(
        ErrorMessage = "Vui lòng nhập số điện thoại."
    )]
    [RegularExpression(
        @"^(?:\+84|0)(?:3|5|7|8|9)\d{8}$",
        ErrorMessage = "Số điện thoại Việt Nam không hợp lệ."
    )]
    public string PhoneNumber { get; set; } = string.Empty;


    // =========================
    // EMAIL
    // =========================

    [Required(
        ErrorMessage = "Vui lòng nhập email."
    )]
    [StringLength(
        100,
        ErrorMessage = "Email không được vượt quá 100 ký tự."
    )]
    [EmailAddress(
        ErrorMessage = "Email không đúng định dạng."
    )]
    [RegularExpression(
        @"^[A-Za-z0-9](?:[A-Za-z0-9._%+\-]*[A-Za-z0-9])?@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.(?:com|net|org|edu|gov|info|io|vn|co)(?:\.vn)?$",
        ErrorMessage = "Email không hợp lệ hoặc có đuôi tên miền không được hỗ trợ."
    )]
    public string Email { get; set; } = string.Empty;


    // =========================
    // MẬT KHẨU
    // =========================

    [Required(
        ErrorMessage = "Vui lòng nhập mật khẩu."
    )]
    [StringLength(
        64,
        MinimumLength = 8,
        ErrorMessage = "Mật khẩu phải từ 8 đến 64 ký tự."
    )]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,64}$",
        ErrorMessage = "Mật khẩu cần chữ hoa, chữ thường, số và ký tự đặc biệt."
    )]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;


    // =========================
    // XÁC NHẬN MẬT KHẨU
    // =========================

    [Required(
        ErrorMessage = "Vui lòng xác nhận mật khẩu."
    )]
    [Compare(
        nameof(Password),
        ErrorMessage = "Xác nhận mật khẩu không trùng khớp."
    )]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;


    // =========================
    // ĐIỀU KHOẢN SỬ DỤNG
    // =========================

    [Range(
        typeof(bool),
        "true",
        "true",
        ErrorMessage = "Bạn cần đồng ý với điều khoản sử dụng."
    )]
    public bool AcceptTerms { get; set; }
}
