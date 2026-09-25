using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EMua.ViewModels;

public sealed class AccountProfileViewModel
{
    public int UserId { get; init; }

    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }

    public string? Address { get; init; }

    public string? AvatarUrl { get; init; }

    public string RoleName { get; init; } = "Khách hàng";

    public DateTime? MemberSince { get; init; }

    public int OrderCount { get; init; }

    public int FavoriteCount { get; init; }

    public bool IsCustomer { get; init; }

    public bool IsStaff { get; init; }

    // Tổng tiền tích lũy từ các đơn hàng hoàn thành
    public decimal TotalSpent { get; init; }

    // Logic tính phân hạng tự động
    public string MembershipTier => TotalSpent switch
    {
        >= 100_000_000m => "Thân Thiết",
        >= 55_000_000m => "Kim Cương",
        >= 25_000_000m => "Vàng",
        >= 5_000_000m => "Bạc",
        >= 1_000_000m => "Đồng",
        _ => "Khách hàng Mới"
    };

    public string Initial =>
        string.IsNullOrWhiteSpace(FullName)
            ? "U"
            : FullName.Trim()[0].ToString().ToUpperInvariant();
}

public sealed class EditAccountProfileViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(100, MinimumLength = 2,
        ErrorMessage = "Họ và tên phải từ 2 đến 100 ký tự.")]
    [RegularExpression(@"^[a-zA-ZÀ-ỹĐđ\s'.-]+$",
        ErrorMessage = "Họ tên chỉ được chứa chữ cái và khoảng trắng.")]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [RegularExpression(@"^$|^(?:\+84|0)(?:3|5|7|8|9)\d{8}$",
        ErrorMessage = "Số điện thoại Việt Nam không hợp lệ.")]
    public string? PhoneNumber { get; set; }

    [StringLength(255, ErrorMessage = "Địa chỉ không được vượt quá 255 ký tự.")]
    public string? Address { get; set; }

    public string? CurrentAvatarUrl { get; set; }

    public string? SelectedAvatar { get; set; }

    public IFormFile? AvatarFile { get; set; }

    public decimal TotalSpent { get; set; }

    public string MembershipTier => TotalSpent switch
    {
        >= 100_000_000m => "Thân Thiết",
        >= 55_000_000m => "Kim Cương",
        >= 25_000_000m => "Vàng",
        >= 5_000_000m => "Bạc",
        >= 1_000_000m => "Đồng",
        _ => "Khách hàng Mới"
    };
}