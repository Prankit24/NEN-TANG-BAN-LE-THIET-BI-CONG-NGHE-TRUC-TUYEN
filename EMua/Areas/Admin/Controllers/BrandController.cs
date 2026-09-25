using EMua.Areas.Admin.Models.ViewModels;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class BrandController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public BrandController(
        EMuaDbContext db,
        IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!IsFixedAdmin())
            return RedirectToAction("Index", "Home", new { area = "" });

        var brands = await GetBrandsAsync();

        var editor = brands.Count == 0
            ? new BrandEditViewModel { TrangThai = true }
            : await GetEditorAsync(brands[0].MaThuongHieu)
              ?? new BrandEditViewModel { TrangThai = true };

        return View(new BrandManagementViewModel
        {
            Brands = brands,
            Editor = editor
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetBrand(int id)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        var brand = await GetEditorAsync(id);

        return brand is null
            ? NotFound(new
            {
                success = false,
                message = "Không tìm thấy thương hiệu."
            })
            : Json(new
            {
                success = true,
                brand
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAjax(BrandEditViewModel model)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        model.TenThuongHieu = model.TenThuongHieu?.Trim() ?? string.Empty;
        model.MoTa = string.IsNullOrWhiteSpace(model.MoTa)
            ? null
            : model.MoTa.Trim();

        if (await _db.ThuongHieus.AnyAsync(x =>
            x.MaThuongHieu != model.MaThuongHieu &&
            x.TenThuongHieu == model.TenThuongHieu))
        {
            ModelState.AddModelError(
                nameof(model.TenThuongHieu),
                "Tên thương hiệu này đã tồn tại.");
        }

        if (model.LogoFile is { Length: > 0 })
        {
            var extension = Path.GetExtension(
                model.LogoFile.FileName).ToLowerInvariant();

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowed.Contains(extension))
            {
                ModelState.AddModelError(
                    nameof(model.LogoFile),
                    "Logo chỉ nhận JPG, JPEG, PNG hoặc WEBP.");
            }

            if (model.LogoFile.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError(
                    nameof(model.LogoFile),
                    "Logo không được vượt quá 2 MB.");
            }
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
                    ? "Dữ liệu chưa hợp lệ."
                    : message
            });
        }

        ThuongHieu brand;
        var isNew = model.MaThuongHieu == 0;

        if (isNew)
        {
            brand = new ThuongHieu();
            _db.ThuongHieus.Add(brand);
        }
        else
        {
            var existingBrand = await _db.ThuongHieus.FindAsync(
                model.MaThuongHieu);

            if (existingBrand is null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Thương hiệu không còn tồn tại."
                });
            }

            brand = existingBrand;
        }

        brand.TenThuongHieu = model.TenThuongHieu;
        brand.MoTa = model.MoTa;
        brand.TrangThai = model.TrangThai;

        if (model.LogoFile is { Length: > 0 })
            brand.Logo = await SaveLogoAsync(model.LogoFile);

        await _db.SaveChangesAsync();

        var brands = await GetBrandsAsync();
        var editor = await GetEditorAsync(brand.MaThuongHieu);

        return Json(new
        {
            success = true,
            message = isNew
                ? $"Đã thêm thương hiệu “{brand.TenThuongHieu}”."
                : $"Đã lưu thay đổi cho “{brand.TenThuongHieu}”.",
            brand = editor,
            brands
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAjax(int id)
    {
        if (!IsFixedAdmin())
            return Unauthorized();

        var brand = await _db.ThuongHieus.FindAsync(id);

        if (brand is null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy thương hiệu cần xóa."
            });
        }

        var productCount = await _db.SanPhams.CountAsync(x =>
            x.MaThuongHieu == id);

        if (productCount > 0)
        {
            return BadRequest(new
            {
                success = false,
                message = $"Không thể xóa “{brand.TenThuongHieu}” vì vẫn còn {productCount} sản phẩm."
            });
        }

        var name = brand.TenThuongHieu;

        _db.ThuongHieus.Remove(brand);
        await _db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = $"Đã xóa thương hiệu “{name}”.",
            brands = await GetBrandsAsync()
        });
    }

    private Task<List<BrandListItemViewModel>> GetBrandsAsync()
    {
        return _db.ThuongHieus
            .AsNoTracking()
            .OrderBy(x => x.TenThuongHieu)
            .Select(x => new BrandListItemViewModel
            {
                MaThuongHieu = x.MaThuongHieu,
                TenThuongHieu = x.TenThuongHieu,
                Logo = x.Logo,
                ProductCount = x.SanPhams.Count,
                TrangThai = x.TrangThai
            })
            .ToListAsync();
    }

    private Task<BrandEditViewModel?> GetEditorAsync(int id)
    {
        return _db.ThuongHieus
            .AsNoTracking()
            .Where(x => x.MaThuongHieu == id)
            .Select(x => new BrandEditViewModel
            {
                MaThuongHieu = x.MaThuongHieu,
                TenThuongHieu = x.TenThuongHieu,
                MoTa = x.MoTa,
                Logo = x.Logo,
                ProductCount = x.SanPhams.Count,
                TrangThai = x.TrangThai
            })
            .FirstOrDefaultAsync();
    }

    private async Task<string> SaveLogoAsync(IFormFile file)
    {
        var extension = Path.GetExtension(
            file.FileName).ToLowerInvariant();

        var folder = Path.Combine(
            _environment.WebRootPath,
            "uploads",
            "brands");

        Directory.CreateDirectory(folder);

        var name = $"{Guid.NewGuid():N}{extension}";

        await using var stream = new FileStream(
            Path.Combine(folder, name),
            FileMode.Create);

        await file.CopyToAsync(stream);

        return $"/uploads/brands/{name}";
    }

    // ĐÃ SỬA HÀM NÀY ĐỂ TRÁNH BỊ BỎ SÓT ROLE
    private bool IsFixedAdmin()
    {
        return User.IsInRole("Quản trị viên") || User.IsInRole("Admin") || User.IsInRole("QuanTriVien");
    }
}