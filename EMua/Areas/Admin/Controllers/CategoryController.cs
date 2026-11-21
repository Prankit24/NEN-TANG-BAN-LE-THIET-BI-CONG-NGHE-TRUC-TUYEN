using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class CategoryController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IUndoDeletionManager _undoManager;

    public CategoryController(
        EMuaDbContext db, 
        IMemoryCache cache,
        IUndoDeletionManager undoManager)
    {
        _db = db;
        _cache = cache;
        _undoManager = undoManager;
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
                "Tên danh mục này đã tồn tại.");
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
                model.MaDanhMuc);

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

        // Xóa cache danh mục và trang chủ để cập nhật dữ liệu mới
        _cache.Remove(AppCacheKeys.Categories);
        _cache.Remove(AppCacheKeys.Home);

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
                message = $"Không thể xóa vì danh mục “{category.TenDanhMuc}” vẫn còn {productCount} sản phẩm trực thuộc."
            });
        }

        var categoryName = category.TenDanhMuc;

        // Lưu bản ghi vào bộ nhớ chờ hoàn tác và chuyển trạng thái "Ngừng hoạt động"
        category.TrangThaiDanhMuc = "Ngừng hoạt động";
        await _db.SaveChangesAsync();

        var undoItem = _undoManager.RegisterPendingDeletion(
            DeletionTargetType.Category,
            category.MaDanhMuc,
            categoryName,
            category.MoTa,
            User);

        // Xóa cache danh mục và trang chủ để cập nhật dữ liệu mới
        _cache.Remove(AppCacheKeys.Categories);
        _cache.Remove(AppCacheKeys.Home);

        return Json(new
        {
            success = true,
            canUndo = true,
            undoToken = undoItem.Token,
            undoDurationSeconds = 60,
            message = $"Đã xóa danh mục “{categoryName}”. Bạn có 60 giây để hoàn tác.",
            categories = await GetCategoriesAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoDeleteAjax([FromForm] string token)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        if (!_undoManager.TryClaimUndo(token, User, out var item, out var errorMessage))
        {
            return BadRequest(new { success = false, message = errorMessage ?? "Không thể hoàn tác." });
        }

        var category = await _db.DanhMucSanPhams.FindAsync(item!.EntityId);
        if (category == null)
        {
            return NotFound(new { success = false, message = "Danh mục không còn tồn tại trong hệ thống." });
        }

        category.TrangThaiDanhMuc = "Đang hoạt động";
        await _db.SaveChangesAsync();

        _cache.Remove(AppCacheKeys.Categories);
        _cache.Remove(AppCacheKeys.Home);

        return Json(new
        {
            success = true,
            message = $"Đã hoàn tác và khôi phục danh mục “{category.TenDanhMuc}” thành công.",
            categories = await GetCategoriesAsync()
        });
    }

    private async Task<List<CategoryListItemViewModel>> GetCategoriesAsync()
    {
        var list = await _db.DanhMucSanPhams
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

        return list.Where(c => !_undoManager.IsEntityPendingDeletion(DeletionTargetType.Category, c.MaDanhMuc)).ToList();
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

    // ĐÃ SỬA HÀM NÀY ĐỂ TRÁNH BỊ BỎ SÓT ROLE
    private bool IsFixedAdmin()
    {
        return User.IsInRole("Quản trị viên") || User.IsInRole("Admin") || User.IsInRole("QuanTriVien");
    }
}