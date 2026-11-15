using System.Security.Claims;
using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EMua.Areas.Staff.Controllers;

public class ImportOrderController : StaffControllerBase
{
    private readonly EMuaDbContext _db;
    private readonly ImportOrderReceivingService _receiving;
    private readonly ISupplierQuotationService _quotationService;
    private readonly ILogger<ImportOrderController> _logger;
    private static readonly string[] ImportRoles = { "Admin", "Quản trị viên", "QuanTriVien", "NhanVien", "Nhân viên" };

    public ImportOrderController(EMuaDbContext db, ISupplierQuotationService quotationService,
        ImportOrderReceivingService receiving, ILogger<ImportOrderController> logger)
    {
        _db = db; _quotationService = quotationService; _receiving = receiving; _logger = logger;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
        { context.Result = Challenge(); return; }
        var allowed = await _db.NguoiDungs.AsNoTracking().AnyAsync(u => u.MaNguoiDung == id && u.TrangThai &&
            (u.MaQuyen == 1 || u.MaQuyen == 2 || ImportRoles.Contains(u.MaQuyenNavigation.TenQuyen)));
        if (!allowed) { context.Result = Forbid(); return; }
        await next();
    }

    public async Task<IActionResult> Index(string? status, string? search, int page = 1, int pageSize = 12)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.PhieuNhapHangs.AsNoTracking()
            .Include(x => x.MaNguoiTaoNavigation).Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all") query = query.Where(x => x.TrangThai == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.MaChungTu, pattern) || EF.Functions.ILike(x.NhaCungCap, pattern) ||
                EF.Functions.ILike(x.LyDoNhap, pattern) || (x.DienGiai != null && EF.Functions.ILike(x.DienGiai, pattern)));
        }
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        ViewBag.Status = status ?? "all"; ViewBag.Search = search;
        ViewBag.CurrentPage = page; ViewBag.TotalPages = pages; ViewBag.TotalItems = total; ViewBag.PageSize = pageSize;
        return View(await query.OrderByDescending(x => x.NgayTao).ThenByDescending(x => x.MaPhieuNhap)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var receipt = await _db.PhieuNhapHangs.AsNoTracking()
            .Include(x => x.MaNguoiTaoNavigation).Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.MaNguoiNhapKhoNavigation).Include(x => x.MaNhaCungCapNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs).ThenInclude(x => x.MaBienTheNavigation).ThenInclude(x => x.MaSanPhamNavigation)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.MaPhieuNhap == id);
        return receipt == null ? NotFound() : View(receipt);
    }

    [HttpGet]
    public async Task<IActionResult> GetSuppliers() => Json(await _db.NhaCungCaps.AsNoTracking()
        .Where(x => x.TrangThai == "Đang hợp tác").OrderBy(x => x.TenNhaCungCap)
        .Select(x => new { id = x.MaNhaCungCap, name = x.TenNhaCungCap, tax = x.MaSoThue, phone = x.SoDienThoai }).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> CompareQuotations(int variantId, int qty = 1)
    {
        if (variantId <= 0 || qty < 1 || qty > 100000)
            return BadRequest(new { success = false, message = "Chọn biến thể và số lượng từ 1 đến 100.000." });
        return Json(new { success = true, data = await _quotationService.GetQuotationsForVariantAsync(variantId, qty) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSupplierAjax([FromBody] SupplierQuickCreateModel? model)
    {
        if (model == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(model.TenNhaCungCap))
            return BadRequest(new { success = false, message = ModelState.Values.SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage).FirstOrDefault() ?? "Thông tin nhà cung cấp không hợp lệ." });
        var name = model.TenNhaCungCap.Trim();
        try
        {
            var result = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                // Chung với Create: tránh hai yêu cầu cùng tự tạo một NCC hoặc một phiếu.
                await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(681126, 1)");
                var key = name.ToLowerInvariant();
                var supplier = await _db.NhaCungCaps.FirstOrDefaultAsync(x => x.TenNhaCungCap.Trim().ToLower() == key);
                if (supplier != null && supplier.TrangThai != "Đang hợp tác")
                    return (Success: false, Id: 0, Name: name, Message: "Nhà cung cấp này đã ngừng hợp tác. Vui lòng chọn NCC khác.");
                if (supplier == null)
                {
                    supplier = new NhaCungCap { TenNhaCungCap = name, SoDienThoai = model.SoDienThoai?.Trim(),
                        MaSoThue = model.MaSoThue?.Trim(), Email = model.Email?.Trim(), DiaChi = model.DiaChi?.Trim(),
                        GhiChu = model.GhiChu?.Trim(), TrangThai = "Đang hợp tác", NgayTao = ImportOrderReceivingService.VietnamNow() };
                    _db.NhaCungCaps.Add(supplier);
                    await _db.SaveChangesAsync();
                }
                await tx.CommitAsync();
                return (Success: true, Id: supplier.MaNhaCungCap, Name: supplier.TenNhaCungCap, Message: "Đã chọn nhà cung cấp.");
            });
            return Json(new { success = result.Success, id = result.Id, name = result.Name, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể thêm nhà cung cấp nhập hàng");
            return StatusCode(500, new { success = false, message = "Không lưu được nhà cung cấp. Vui lòng thử lại." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? productId, int? variantId)
    {
        var vm = new ImportOrderCreateViewModel
        { MaYeuCau = $"PNN-{ImportOrderReceivingService.VietnamNow():yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant() };
        var query = _db.BienTheSanPhams.AsNoTracking().Include(x => x.MaSanPhamNavigation).AsQueryable();
        if (variantId is > 0) query = query.Where(x => x.MaBienThe == variantId);
        else if (productId is > 0) query = query.Where(x => x.MaSanPham == productId);
        else query = query.Where(x => false);
        foreach (var v in await query.OrderBy(x => x.MaBienThe).Take(100).ToListAsync())
        {
            // 70% là giá dự kiến khi chưa có báo giá; không tự coi đó là giá NCC.
            vm.Items.Add(new ImportOrderItemViewModel { MaBienThe = v.MaBienThe, SoLuongNhap = 10,
                DonGiaChuaThue = ImportOrderAmounts.Money(v.Gia * 0.7m / 1.1m), ThueSuatVat = 10m });
        }
        await PopulateAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ImportOrderCreateViewModel model)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        model.Items ??= new();
        if (model.Items.Count < 1 || model.Items.Count > 100)
            ModelState.AddModelError("", "Một phiếu cần từ 1 đến 100 dòng sản phẩm.");
        if (model.Items.Select(x => x.MaBienThe).Distinct().Count() != model.Items.Count)
            ModelState.AddModelError("", "Không chọn trùng biến thể trong cùng một phiếu. Hãy gộp số lượng vào một dòng.");
        if (!ModelState.IsValid) return await InvalidCreateAsync(model);

        // Mã yêu cầu giữ nguyên khi người dùng gửi lại; unique index bảo vệ ở database.
        var documentCode = model.MaYeuCau.Trim().ToUpperInvariant();
        try
        {
            var result = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(681126, 1)");
                var existing = await _db.PhieuNhapHangs.AsNoTracking().FirstOrDefaultAsync(x => x.MaChungTu == documentCode);
                if (existing != null)
                {
                    if (existing.MaNguoiTao != userId) throw new ArgumentException("Mã yêu cầu đã được sử dụng. Vui lòng mở lại trang lập phiếu.");
                    return existing.MaPhieuNhap;
                }
                var supplier = model.MaNhaCungCap is > 0
                    ? await _db.NhaCungCaps.FirstOrDefaultAsync(x => x.MaNhaCungCap == model.MaNhaCungCap)
                    : await _db.NhaCungCaps.FirstOrDefaultAsync(x => x.TenNhaCungCap.Trim().ToLower() == model.NhaCungCap.Trim().ToLower());
                if (model.MaNhaCungCap is > 0 && supplier == null)
                    throw new ArgumentException("Nhà cung cấp không còn tồn tại. Vui lòng chọn lại.");
                if (supplier != null && supplier.TrangThai != "Đang hợp tác")
                    throw new ArgumentException("Nhà cung cấp đã ngừng hợp tác. Vui lòng chọn lại.");
                if (supplier == null)
                {
                    supplier = new NhaCungCap { TenNhaCungCap = model.NhaCungCap.Trim(), TrangThai = "Đang hợp tác",
                        NgayTao = ImportOrderReceivingService.VietnamNow() };
                    _db.NhaCungCaps.Add(supplier);
                    await _db.SaveChangesAsync();
                }
                var ids = model.Items.Select(x => x.MaBienThe).ToList();
                if (await _db.BienTheSanPhams.CountAsync(x => ids.Contains(x.MaBienThe)) != ids.Count)
                    throw new ArgumentException("Có biến thể không tồn tại. Vui lòng tải lại danh sách sản phẩm.");
                var amounts = new List<ImportLineAmounts>();
                var receipt = new PhieuNhapHang { MaChungTu = documentCode, MaNhaCungCap = supplier.MaNhaCungCap,
                    NhaCungCap = supplier.TenNhaCungCap, LyDoNhap = model.LyDoNhap.Trim(), DienGiai = model.DienGiai?.Trim(),
                    GhiChu = model.GhiChu?.Trim(), TrangThai = "Chờ duyệt", MaNguoiTao = userId,
                    NgayTao = ImportOrderReceivingService.VietnamNow(), ChiPhiVanChuyen = ImportOrderAmounts.Money(model.ChiPhiVanChuyen),
                    ChiPhiKhac = ImportOrderAmounts.Money(model.ChiPhiKhac), ChietKhau = ImportOrderAmounts.Money(model.ChietKhauTong) };
                var quoteNow = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                foreach (var item in model.Items)
                {
                    var net = item.DonGiaChuaThue; var vat = item.ThueSuatVat; var discount = item.ChietKhau;
                    if (item.MaBaoGia is > 0)
                    {
                        var quote = await _db.BaoGiaNhapHangs.AsNoTracking().FirstOrDefaultAsync(q => q.MaBaoGia == item.MaBaoGia &&
                            q.MaBienThe == item.MaBienThe && q.MaNhaCungCap == supplier.MaNhaCungCap && q.TrangThai == "Hiệu lực" &&
                            q.SoLuongToiThieu >= 1 && q.SoLuongToiThieu <= item.SoLuongNhap && q.NgayHieuLuc <= quoteNow && (q.NgayHetHan == null || q.NgayHetHan >= quoteNow));
                        if (quote == null) throw new ArgumentException("Báo giá không còn phù hợp nhà cung cấp, thời hạn hoặc số lượng. Vui lòng chọn lại báo giá.");
                        if (ImportOrderAmounts.Money(item.DonGiaChuaThue) != ImportOrderAmounts.Money(quote.DonGiaChuaThue) ||
                            item.ThueSuatVat != quote.ThueSuatVat || ImportOrderAmounts.Money(item.ChietKhau) != ImportOrderAmounts.Money(quote.ChietKhau))
                            throw new ArgumentException("Báo giá vừa thay đổi. Vui lòng mở lại bảng báo giá và xác nhận giá mới.");
                        net = quote.DonGiaChuaThue; vat = quote.ThueSuatVat; discount = quote.ChietKhau;
                    }
                    var a = ImportOrderAmounts.CalculateLine(item.SoLuongNhap, net, vat, discount);
                    amounts.Add(a);
                    receipt.ChiTietPhieuNhapHangs.Add(new ChiTietPhieuNhapHang { MaBienThe = item.MaBienThe, SoLuongNhap = item.SoLuongNhap,
                        DonGiaChuaThue = a.NetUnit, ThueSuatVat = vat, DonGiaNhapDuKien = a.GrossUnit, DonGiaSauThue = a.GrossUnit,
                        TienThueVat = a.Vat, ChietKhau = a.DiscountUnit, ThanhTien = a.Total, GhiChu = item.GhiChu?.Trim() });
                }
                receipt.TienHangChuaThue = amounts.Sum(x => x.Net); receipt.TienThueVat = amounts.Sum(x => x.Vat);
                receipt.TongTienDuKien = ImportOrderAmounts.CalculateTotal(amounts, model.ChiPhiVanChuyen, model.ChiPhiKhac, model.ChietKhauTong);
                _db.PhieuNhapHangs.Add(receipt);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return receipt.MaPhieuNhap;
            });
            TempData["Success"] = "Đã lưu phiếu đề nghị nhập hàng.";
            return RedirectToAction(nameof(Details), new { id = result });
        }
        catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể tạo phiếu nhập {DocumentCode}", documentCode);
            ModelState.AddModelError("", "Chưa xác nhận được kết quả lưu phiếu. Vui lòng kiểm tra danh sách trước khi thử lại; giữ nguyên mã yêu cầu để tránh tạo trùng.");
        }
        _db.ChangeTracker.Clear();
        return await InvalidCreateAsync(model);
    }

    private async Task<IActionResult> InvalidCreateAsync(ImportOrderCreateViewModel model)
    { await PopulateAsync(model); return View("Create", model); }

    private async Task PopulateAsync(ImportOrderCreateViewModel model)
    {
        ViewBag.Suppliers = await _db.NhaCungCaps.AsNoTracking().Where(x => x.TrangThai == "Đang hợp tác")
            .OrderBy(x => x.TenNhaCungCap).ToListAsync();
        var variants = await _db.BienTheSanPhams.AsNoTracking().Include(x => x.MaSanPhamNavigation)
            .OrderBy(x => x.MaSanPhamNavigation.TenSanPham).ToListAsync();
        var byId = variants.ToDictionary(x => x.MaBienThe);
        foreach (var item in model.Items ?? new())
            if (byId.TryGetValue(item.MaBienThe, out var v))
            { item.TenSanPham = v.MaSanPhamNavigation?.TenSanPham; item.MauSac = v.MauSac; item.PhienBan = v.PhienBan;
              item.TonKhoHienTai = v.SoLuong; item.GiaBanHienTai = v.Gia; }
        ViewBag.AllVariants = variants.Select(v => new { id = v.MaBienThe,
            name = $"{v.MaSanPhamNavigation?.TenSanPham} ({v.MauSac} - {v.PhienBan})", price = v.Gia, stock = v.SoLuong }).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Receive(int id)
    {
        var vm = await _receiving.LoadAsync(id);
        if (vm == null) return NotFound();
        if (vm.TrangThai != "Đã duyệt")
        { TempData["Error"] = "Chỉ kiểm nhận phiếu đã duyệt và chưa nhập kho."; return RedirectToAction(nameof(Details), new { id }); }
        ViewData["ReceiveArea"] = "Staff"; ViewData["ReceiveAction"] = "ConfirmReceive";
        return View("~/Areas/Staff/Views/ImportOrder/Receive.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceive([FromForm] ImportOrderReceiveCommand model)
    {
        var result = ModelState.IsValid
            ? await _receiving.ConfirmAsync(model, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!))
            : (Success: false, Message: "Số lượng hoặc ghi chú kiểm nhận không hợp lệ.");
        if (result.Success)
        { TempData["Success"] = result.Message; return RedirectToAction(nameof(Details), new { id = model.MaPhieuNhap }); }
        var vm = await _receiving.LoadAsync(model.MaPhieuNhap);
        if (vm == null) return NotFound();
        if (vm.TrangThai != "Đã duyệt")
        { TempData["Error"] = result.Message; return RedirectToAction(nameof(Details), new { id = model.MaPhieuNhap }); }
        foreach (var line in vm.Items)
        {
            var posted = model.Items?.FirstOrDefault(x => x.MaChiTietPhieuNhap == line.MaChiTietPhieuNhap);
            if (posted != null) line.SoLuongThucNhan = posted.SoLuongThucNhan;
        }
        vm.GhiChuKiemNhan = model.GhiChuKiemNhan;
        ModelState.AddModelError("", result.Message);
        ViewData["ReceiveArea"] = "Staff"; ViewData["ReceiveAction"] = "ConfirmReceive";
        return View("~/Areas/Staff/Views/ImportOrder/Receive.cshtml", vm);
    }
}
