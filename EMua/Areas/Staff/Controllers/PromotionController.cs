using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class PromotionController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    public PromotionController(EMuaDbContext db) => _db = db;
    public async Task<IActionResult> Index(string? q, string? status, int page = 1, int pageSize = 12)
    {
        var promotions = _db.KhuyenMais.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var pattern = $"%{search}%";
            promotions = promotions.Where(x =>
                EF.Functions.ILike(x.MaCode, pattern) ||
                (x.TenKhuyenMai != null && EF.Functions.ILike(x.TenKhuyenMai, pattern)));
        }
        if (status == "active") promotions = promotions.Where(x => x.TrangThai);
        if (status == "inactive") promotions = promotions.Where(x => !x.TrangThai);

        int totalItems = await promotions.CountAsync();
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

        var items = await promotions
            .OrderByDescending(x => x.MaKhuyenMai)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        ViewBag.PageSize = pageSize;

        return View(items);
    }

    [HttpGet]
    public IActionResult Create() => View(new PromotionCreateViewModel { NgayBatDau = DateTime.Today });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PromotionCreateViewModel model)
    {
        model.MaCode = model.MaCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (model.NgayKetThuc.HasValue && model.NgayBatDau.HasValue && model.NgayKetThuc < model.NgayBatDau)
            ModelState.AddModelError(nameof(model.NgayKetThuc), "Ngày kết thúc phải sau ngày bắt đầu.");
        if (await _db.KhuyenMais.AnyAsync(x => x.MaCode == model.MaCode)) ModelState.AddModelError(nameof(model.MaCode), "Mã khuyến mãi đã tồn tại.");
        if (!ModelState.IsValid) return View(model);
        _db.KhuyenMais.Add(ToEntity(model));
        await _db.SaveChangesAsync(); TempData["Success"] = "Đã tạo khuyến mãi."; return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var promotion = await _db.KhuyenMais.FindAsync(id); if (promotion == null) return NotFound();
        promotion.TrangThai = !promotion.TrangThai; await _db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    private static KhuyenMai ToEntity(PromotionCreateViewModel model) => new()
    {
        MaCode = model.MaCode, TenKhuyenMai = model.TenKhuyenMai.Trim(), LoaiGiamGia = model.LoaiGiamGia,
        GiaTriGiam = model.GiaTriGiam,
        GiaTriDonHangToiThieu = model.GiaTriDonHangToiThieu, SoLuong = model.SoLuong,
        NgayBatDau = model.NgayBatDau, NgayKetThuc = model.NgayKetThuc, MoTa = model.MoTa?.Trim(), TrangThai = true
    };
}
