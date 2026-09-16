using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
namespace EMua.Areas.Admin.Models.ViewModels;

public class BrandManagementViewModel
{
    public List<BrandListItemViewModel> Brands { get; set; } = [];
    public BrandEditViewModel Editor { get; set; } = new();
}

public class BrandListItemViewModel
{
    public int MaThuongHieu { get; set; }
    public string TenThuongHieu { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public int ProductCount { get; set; }
    public bool TrangThai { get; set; }
}

public class BrandEditViewModel
{
    public int MaThuongHieu { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên thương hiệu.")]
    [StringLength(100, ErrorMessage = "Tên thương hiệu không vượt quá 100 ký tự.")]
    public string TenThuongHieu { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả không vượt quá 500 ký tự.")]
    public string? MoTa { get; set; }

    public bool TrangThai { get; set; } = true;
    public string? Logo { get; set; }
    public int ProductCount { get; set; }
    public IFormFile? LogoFile { get; set; }
}
