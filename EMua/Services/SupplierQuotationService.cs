using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services;

public class QuotationComparisonItemDto
{
    public int MaBaoGia { get; set; }
    public int MaNhaCungCap { get; set; }
    public string TenNhaCungCap { get; set; } = string.Empty;
    public int MaBienThe { get; set; }
    public string TenSanPham { get; set; } = string.Empty;
    public string? MauSac { get; set; }
    public string? PhienBan { get; set; }
    public int SoLuongToiThieu { get; set; }

    // Giá nhập & Thuế đầu vào
    public decimal DonGiaChuaThue { get; set; }
    public decimal ThueSuatVat { get; set; }
    public decimal TienThueVat { get; set; }
    public decimal DonGiaSauThue { get; set; }
    public decimal ChietKhau { get; set; }
    public decimal ChiPhiVanChuyenUocTinh { get; set; }

    // Chi phí thực tế nhập 1 đơn vị
    public decimal GiaVonThucTeMotDonVi => Math.Max(0, DonGiaSauThue - ChietKhau + ChiPhiVanChuyenUocTinh);

    // Giá bán lẻ niêm yết hiện tại
    public decimal GiaBanHienTai { get; set; }

    // Thuế GTGT đầu ra giả định (10%)
    public decimal ThueVatDauRaUocTinh => Math.Round(GiaBanHienTai * 0.10m, 2);

    // Lợi nhuận gộp dự kiến trên 1 đơn vị
    public decimal LoiNhuanGopDuKien => Math.Max(0, GiaBanHienTai - GiaVonThucTeMotDonVi);

    // Tỷ suất lợi nhuận gộp (%)
    public decimal TySuatLoiNhuan => GiaBanHienTai > 0 ? Math.Round((LoiNhuanGopDuKien / GiaBanHienTai) * 100, 2) : 0;

    public bool IsBestPrice { get; set; }
    public string? GhiChu { get; set; }
}

public interface ISupplierQuotationService
{
    Task<List<QuotationComparisonItemDto>> GetQuotationsForVariantAsync(int variantId, int quantity = 1);
    Task<QuotationComparisonItemDto?> GetBestQuotationAsync(int variantId, int quantity = 1);
}

public class SupplierQuotationService : ISupplierQuotationService
{
    private readonly EMuaDbContext _db;

    public SupplierQuotationService(EMuaDbContext db)
    {
        _db = db;
    }

    public async Task<List<QuotationComparisonItemDto>> GetQuotationsForVariantAsync(int variantId, int quantity = 1)
    {
        var variant = await _db.BienTheSanPhams
            .Include(b => b.MaSanPhamNavigation)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.MaBienThe == variantId);

        if (variant == null) return new List<QuotationComparisonItemDto>();

        var quotations = await _db.BaoGiaNhapHangs
            .Include(bg => bg.MaNhaCungCapNavigation)
            .AsNoTracking()
            .Where(bg => bg.MaBienThe == variantId && bg.TrangThai == "Hiệu lực")
            .OrderBy(bg => bg.DonGiaSauThue - bg.ChietKhau + bg.ChiPhiVanChuyenUocTinh)
            .ToListAsync();

        var result = new List<QuotationComparisonItemDto>();

        foreach (var q in quotations)
        {
            var item = new QuotationComparisonItemDto
            {
                MaBaoGia = q.MaBaoGia,
                MaNhaCungCap = q.MaNhaCungCap,
                TenNhaCungCap = q.MaNhaCungCapNavigation?.TenNhaCungCap ?? "NCC #" + q.MaNhaCungCap,
                MaBienThe = q.MaBienThe,
                TenSanPham = variant.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm #" + variant.MaSanPham,
                MauSac = variant.MauSac,
                PhienBan = variant.PhienBan,
                SoLuongToiThieu = q.SoLuongToiThieu,
                DonGiaChuaThue = q.DonGiaChuaThue,
                ThueSuatVat = q.ThueSuatVat,
                TienThueVat = q.TienThueVat,
                DonGiaSauThue = q.DonGiaSauThue,
                ChietKhau = q.ChietKhau,
                ChiPhiVanChuyenUocTinh = q.ChiPhiVanChuyenUocTinh,
                GiaBanHienTai = variant.Gia,
                GhiChu = q.GhiChu
            };
            result.Add(item);
        }

        if (result.Any())
        {
            // Đánh dấu báo giá có chi phí thực tế thấp nhất
            var minCost = result.Min(x => x.GiaVonThucTeMotDonVi);
            var best = result.FirstOrDefault(x => x.GiaVonThucTeMotDonVi == minCost);
            if (best != null) best.IsBestPrice = true;
        }

        return result;
    }

    public async Task<QuotationComparisonItemDto?> GetBestQuotationAsync(int variantId, int quantity = 1)
    {
        var list = await GetQuotationsForVariantAsync(variantId, quantity);
        return list.FirstOrDefault(x => x.IsBestPrice) ?? list.FirstOrDefault();
    }
}
