using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class CategoryManagementViewModel
{
    public List<CategoryListItemViewModel> Categories { get; set; } = [];

    public CategoryEditViewModel Editor { get; set; } = new();

    public bool IsCreateMode { get; set; }

    public int SelectedCategoryId { get; set; }
}

public class CategoryListItemViewModel
{
    public int MaDanhMuc { get; set; }

    public string TenDanhMuc { get; set; } = string.Empty;

    public int ProductCount { get; set; }

    public bool IsActive { get; set; }
}

public class CategoryEditViewModel
{
    public int MaDanhMuc { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(100, ErrorMessage = "Tên danh mục không vượt quá 100 ký tự.")]
    public string TenDanhMuc { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "Mô tả không vượt quá 255 ký tự.")]
    public string? MoTa { get; set; }

    public bool IsActive { get; set; } = true;

    public int ProductCount { get; set; }
}