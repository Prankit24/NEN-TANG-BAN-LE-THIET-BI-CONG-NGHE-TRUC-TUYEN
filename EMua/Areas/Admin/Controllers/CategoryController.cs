using System.Security.Claims;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CategoryController : Controller
{
    private const int AdminUserId = 6;
    private readonly EMuaDbContext _db;

    public CategoryController(EMuaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!IsFixedAdmin())
            return RedirectToAction("Index", "Home", new { area = "" });

        var categories = await GetCategoriesAsync();

        var editor = categories.Count == 0
            ? new CategoryEditViewModel { IsActive = true }
            : await GetEditorAsync(categories[0].MaDanhMuc)
              ?? new CategoryEditViewModel { IsActive = true };

        return View(new CategoryManagementViewModel
        {
            Categories = categories,
            Editor = editor,
            SelectedCategoryId = editor.MaDanhMuc,
            IsCreateMode = editor.MaDanhMuc == 0
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetCategory(int id)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        var category = await GetEditorAsync(id);

        if (category == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy danh mục."
            });
        }

        return Json(new
        {
            success = true,
            category
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAjax(CategoryEditViewModel model)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        model.TenDanhMuc = model.TenDanhMuc?.Trim() ?? string.Empty;
        model.MoTa = string.IsNullOrWhiteSpace(model.MoTa)
            ? null
            : model.MoTa.Trim();

        var isDuplicate = await _db.DanhMucSanPhams.AnyAsync(x =>
            x.MaDanhMuc != model.MaDanhMuc &&
            x.TenDanhMuc == model.TenDanhMuc);

        if (isDuplicate)
        {
            ModelState.AddModelError(
                nameof(model.TenDanhMuc),
                "Tên danh mục này đã tồn tại."
            );
        }

        if (!ModelState.IsValid)
        {
            var message = ModelState.Values
                .SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage)
                .FirstOrDefault();

            return BadRequest(new
            {
                success = false,
                message = string.IsNullOrWhiteSpace(message)
                    ? "Dữ liệu danh mục chưa hợp lệ."
                    : message
            });
        }

        int categoryId;
        string successMessage;

        if (model.MaDanhMuc == 0)
        {
            var newCategory = new DanhMucSanPham
            {
                TenDanhMuc = model.TenDanhMuc,
                MoTa = model.MoTa,
                TrangThaiDanhMuc = model.IsActive
                    ? "Đang hoạt động"
                    : "Ngừng hiển thị"
            };

            _db.DanhMucSanPhams.Add(newCategory);
            await _db.SaveChangesAsync();

            categoryId = newCategory.MaDanhMuc;
            successMessage = $"Đã thêm danh mục “{newCategory.TenDanhMuc}”.";
        }
        else
        {
            var category = await _db.DanhMucSanPhams.FindAsync(
                model.MaDanhMuc
            );

            if (category == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Danh mục không còn tồn tại."
                });
            }

            category.TenDanhMuc = model.TenDanhMuc;
            category.MoTa = model.MoTa;
            category.TrangThaiDanhMuc = model.IsActive
                ? "Đang hoạt động"
                : "Ngừng hiển thị";

            await _db.SaveChangesAsync();

            categoryId = category.MaDanhMuc;
            successMessage = $"Đã lưu thay đổi cho “{category.TenDanhMuc}”.";
        }

        var updatedCategory = await GetEditorAsync(categoryId);
        var categories = await GetCategoriesAsync();

        return Json(new
        {
            success = true,
            message = successMessage,
            category = updatedCategory,
            categories
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAjax(int id)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        var category = await _db.DanhMucSanPhams.FindAsync(id);

        if (category == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy danh mục cần xóa."
            });
        }

        var productCount = await _db.SanPhams.CountAsync(x =>
            x.MaDanhMuc == id);

        if (productCount > 0)
        {
            return BadRequest(new
            {
                success = false,
                message = $"Không thể xóa vì danh mục “{category.TenDanhMuc}” vẫn còn {productCount} sản phẩm."
            });
        }

        var categoryName = category.TenDanhMuc;

        _db.DanhMucSanPhams.Remove(category);
        await _db.SaveChangesAsync();

        var categories = await GetCategoriesAsync();

        return Json(new
        {
            success = true,
            message = $"Đã xóa danh mục “{categoryName}”.",
            categories
        });
    }

    private async Task<List<CategoryListItemViewModel>> GetCategoriesAsync()
    {
        return await _db.DanhMucSanPhams
            .AsNoTracking()
            .OrderBy(x => x.TenDanhMuc)
            .Select(x => new CategoryListItemViewModel
            {
                MaDanhMuc = x.MaDanhMuc,
                TenDanhMuc = x.TenDanhMuc ?? "Chưa đặt tên",
                ProductCount = x.SanPhams.Count,
                IsActive = x.TrangThaiDanhMuc != "Ngừng hiển thị" &&
                           x.TrangThaiDanhMuc != "Ngừng hoạt động"
            })
            .ToListAsync();
    }

    private async Task<CategoryEditViewModel?> GetEditorAsync(int id)
    {
        return await _db.DanhMucSanPhams
            .AsNoTracking()
            .Where(x => x.MaDanhMuc == id)
            .Select(x => new CategoryEditViewModel
            {
                MaDanhMuc = x.MaDanhMuc,
                TenDanhMuc = x.TenDanhMuc ?? string.Empty,
                MoTa = x.MoTa,
                ProductCount = x.SanPhams.Count,
                IsActive = x.TrangThaiDanhMuc != "Ngừng hiển thị" &&
                           x.TrangThaiDanhMuc != "Ngừng hoạt động"
            })
            .FirstOrDefaultAsync();
    }

    private bool IsFixedAdmin()
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier
        );

        return int.TryParse(userIdText, out var userId) &&
               userId == AdminUserId;
    }
}