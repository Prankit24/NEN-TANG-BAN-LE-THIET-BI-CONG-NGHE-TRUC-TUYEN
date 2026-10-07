using EMua.Data;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services.AI;

public class RecommendationService
{
    private readonly EMuaDbContext _db;

    public RecommendationService(EMuaDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetProductContextAsync(string query)
    {
        var products = await _db.SanPhams
            .Include(p => p.BienTheSanPhams)
            .AsNoTracking()
            .Take(6)
            .ToListAsync();

        var context = new System.Text.StringBuilder();
        context.AppendLine("Danh sách sản phẩm hiện có tại cửa hàng EMUA:");
        foreach (var p in products)
        {
            // ⚠️ Đã đổi b.GiaBan thành b.Gia (kiểm tra lại tên thuộc tính giá trong BienTheSanPham của bạn)
            var minPrice = p.BienTheSanPhams.Any() ? p.BienTheSanPhams.Min(b => b.Gia) : 0;
            context.AppendLine($"- Tên: {p.TenSanPham} | Giá từ: {minPrice:N0}đ | Mô tả: {p.MoTa}");
        }

        return context.ToString();
    }
}