using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EMua.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EMua.Services.AI;

public record ProductRecommendationDto(
    int Id,
    string Name,
    decimal? MinPrice,
    bool InStock,
    string? ImageUrl,
    string DetailUrl
);

public class RecommendationService
{
    private readonly EMuaDbContext _db;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        EMuaDbContext db,
        ILogger<RecommendationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(string ContextText, List<ProductRecommendationDto> Products)> GetProductRecommendationsAsync(string query)
    {
        var timer = Stopwatch.StartNew();
        var keyword = (query ?? "").Trim().ToLowerInvariant();

        var source = _db.SanPhams.AsNoTracking();

        // Lọc cơ bản theo tên sản phẩm.
        if (keyword.Contains("laptop") ||
            keyword.Contains("máy tính xách tay"))
        {
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("laptop") ||
                    p.TenSanPham.ToLower().Contains("macbook") ||
                    p.TenSanPham.ToLower().Contains("xps") ||
                    p.TenSanPham.ToLower().Contains("thinkpad") ||
                    p.TenSanPham.ToLower().Contains("inspiron") ||
                    p.TenSanPham.ToLower().Contains("vivobook")
                ));
        }
        else if (keyword.Contains("điện thoại") ||
                 keyword.Contains("dien thoai") ||
                 keyword.Contains("smartphone"))
        {
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("điện thoại") ||
                    p.TenSanPham.ToLower().Contains("iphone") ||
                    p.TenSanPham.ToLower().Contains("smartphone")
                ));
        }

        var rawProducts = await source
            .OrderBy(p => p.MaSanPham)
            .Select(p => new
            {
                p.MaSanPham,
                p.TenSanPham,

                // Giới hạn ngay phần mô tả lấy từ database.
                Description = p.MoTa == null
                    ? ""
                    : p.MoTa.Length > 600
                        ? p.MoTa.Substring(0, 600)
                        : p.MoTa,

                InStock = p.BienTheSanPhams
                    .Any(b => b.SoLuong > 0),

                // Giá thấp nhất trong các biến thể còn hàng.
                MinPrice = p.BienTheSanPhams
                    .Where(b => b.SoLuong > 0)
                    .Select(b => (decimal?)b.Gia)
                    .Min(),

                ImageUrl = p.SanPhamHinhAnhs
                    .OrderByDescending(img => img.LaAnhChinh)
                    .ThenBy(img => img.ThuTu)
                    .Select(img => img.DuongDanAnh)
                    .FirstOrDefault()
            })
            .Take(5)
            .ToListAsync();

        timer.Stop();

        _logger.LogInformation(
            "EMUA AI: Truy vấn sản phẩm mất {ElapsedMs} ms; " +
            "lấy được {Count} sản phẩm.",
            timer.ElapsedMilliseconds,
            rawProducts.Count);

        var productDtos = rawProducts.Select(p => new ProductRecommendationDto(
            Id: p.MaSanPham,
            Name: p.TenSanPham ?? "Sản phẩm EMUA",
            MinPrice: p.MinPrice,
            InStock: p.InStock,
            ImageUrl: p.ImageUrl,
            DetailUrl: $"/Product/Details/{p.MaSanPham}"
        )).ToList();

        if (rawProducts.Count == 0)
        {
            var emptyContext =
                "Không tìm thấy sản phẩm trong kết quả truy vấn này. " +
                "Không kết luận toàn bộ cửa hàng không có sản phẩm. " +
                "Hãy hỏi rõ tên hoặc loại thiết bị khách cần; " +
                "không tự bịa sản phẩm hay giá.";
            return (emptyContext, productDtos);
        }

        var context = new StringBuilder();

        context.AppendLine(
            "[DỮ LIỆU SẢN PHẨM TỪ CỬA HÀNG EMUA]:");
        context.AppendLine(
            "- Đây là danh sách sản phẩm thực tế có trong hệ thống cửa hàng.");
        context.AppendLine(
            "- Hãy ưu tiên chọn lọc tối đa 3 sản phẩm phù hợp nhất với yêu cầu khách hàng.");
        context.AppendLine();

        var culture = CultureInfo.GetCultureInfo("vi-VN");

        foreach (var p in rawProducts)
        {
            var price = p.MinPrice.HasValue && p.MinPrice.Value > 0
                ? p.MinPrice.Value.ToString("N0", culture) + " VNĐ"
                : "Liên hệ cửa hàng";

            var stock = p.InStock
                ? "Còn hàng"
                : "Hết hàng";

            var description = ShortDescription(p.Description);

            context.AppendLine(
                $"• Tên: {p.TenSanPham} | Giá từ: {price} | Tình trạng: {stock} | Đặc điểm nổi bật: {description}");
        }

        _logger.LogInformation(
            "EMUA AI: Ngữ cảnh sản phẩm dài {Length} ký tự.",
            context.Length);

        return (context.ToString(), productDtos);
    }

    public async Task<string> GetProductContextAsync(string query)
    {
        var result = await GetProductRecommendationsAsync(query);
        return result.ContextText;
    }

    private static string ShortDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Chưa có mô tả";

        // Chỉ làm sạch văn bản để gửi AI, không dùng để render HTML.
        var text = Regex.Replace(value, "<[^>]*>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length <= 200
            ? text
            : text.Substring(0, 200) + "…";
    }
}