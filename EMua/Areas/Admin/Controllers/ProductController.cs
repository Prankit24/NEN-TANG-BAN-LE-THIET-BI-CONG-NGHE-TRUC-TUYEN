using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using EMua.Services;

namespace EMua.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Quản trị viên,QuanTriVien")]
public class ProductController : Controller
{
    private readonly IProductImageStorageService _storageService;

    public ProductController(IProductImageStorageService storageService)
    {
        _storageService = storageService;
    }

    [HttpGet]
    public IActionResult Index(string? q, string? status, int? brandId, int? categoryId, string? stockFilter, string? sort = "newest", int page = 1)
    {
        return RedirectToAction("Index", "Product", new { area = "Staff", q, status, brandId, categoryId, stockFilter, sort, page });
    }

    [HttpGet]
    public IActionResult Create()
    {
        return RedirectToAction("Create", "Product", new { area = "Staff" });
    }

    [HttpGet]
    public IActionResult Edit(int id)
    {
        return RedirectToAction("Edit", "Product", new { area = "Staff", id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Không có file ảnh nào được chọn." });
        }

        var url = await _storageService.SaveProductImageAsync(file);
        return Json(new { success = true, url });
    }
}
