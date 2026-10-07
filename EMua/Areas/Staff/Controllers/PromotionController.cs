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
    public async Task<IActionResult> Index(string? q, string? status)
    {
        var promotions = _db.KhuyenMais.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            promotions = promotions.Where(x => x.MaCode.Contains(search) || x.TenKhuyenMai.Contains(search));
        }
        if (status == "active") promotions = promotions.Where(x => x.TrangThai);
        if (status == "inactive") promotions = promotions.Where(x => !x.TrangThai);
        ViewBag.Search = q;
        ViewBag.Status = status ?? "all";
        return View(await promotions.OrderByDescending(x => x.MaKhuyenMai).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var promotion = await _db.KhuyenMais.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaKhuyenMai == id);
        if (promotion == null) return NotFound();

        ViewBag.PromotionId = id;
        return View(ToViewModel(promotion));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PromotionCreateViewModel model)
    {
        model.MaCode = model.MaCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (model.NgayKetThuc.HasValue && model.NgayBatDau.HasValue && model.NgayKetThuc < model.NgayBatDau)
            ModelState.AddModelError(nameof(model.NgayKetThuc), "Ngày kết thúc phải sau ngày bắt đầu.");
        if (await _db.KhuyenMais.AnyAsync(x => x.MaKhuyenMai != id && x.MaCode == model.MaCode))
            ModelState.AddModelError(nameof(model.MaCode), "Mã khuyến mãi đã tồn tại.");

        var promotion = await _db.KhuyenMais.FirstOrDefaultAsync(x => x.MaKhuyenMai == id);
        if (promotion == null) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewBag.PromotionId = id;
            ViewBag.IsEditing = true;
            return View("Details", model);
        }

        promotion.MaCode = model.MaCode;
        promotion.TenKhuyenMai = model.TenKhuyenMai.Trim();
        promotion.LoaiGiamGia = model.LoaiGiamGia;
        promotion.GiaTriGiam = model.GiaTriGiam;
        promotion.GiaTriDonHangToiThieu = model.GiaTriDonHangToiThieu;
        promotion.SoLuong = model.SoLuong;
        promotion.NgayBatDau = model.NgayBatDau;
        promotion.NgayKetThuc = model.NgayKetThuc;
        promotion.MoTa = model.MoTa?.Trim();
        promotion.TrangThai = model.TrangThai;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật khuyến mãi.";
        return RedirectToAction(nameof(Details), new { id });
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

    private static PromotionCreateViewModel ToViewModel(KhuyenMai promotion) => new()
    {
        MaCode = promotion.MaCode,
        TenKhuyenMai = promotion.TenKhuyenMai,
        LoaiGiamGia = promotion.LoaiGiamGia,
        GiaTriGiam = promotion.GiaTriGiam,
        GiaTriDonHangToiThieu = promotion.GiaTriDonHangToiThieu,
        SoLuong = promotion.SoLuong,
        NgayBatDau = promotion.NgayBatDau,
        NgayKetThuc = promotion.NgayKetThuc,
        MoTa = promotion.MoTa,
        TrangThai = promotion.TrangThai
    };
}
