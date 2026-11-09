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

public record BudgetInfo(
    decimal? MinPrice,
    decimal? MaxPrice,
    string RawDescription
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

    public static BudgetInfo? ExtractBudget(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var q = query.Trim().ToLowerInvariant().Replace(",", ".");

        // 1. Dải ngân sách: "từ X đến Y triệu", "X - Y triệu", "từ X - Y tr"
        var rangePattern = @"(?:từ\s+)?(\d+(?:\.\d+)?)\s*(?:tr|triệu|k)?\s*(?:đến|-|tới)\s*(\d+(?:\.\d+)?)\s*(tr|triệu|k|nghìn|ngàn)?";
        var mRange = Regex.Match(q, rangePattern);
        if (mRange.Success)
        {
            var unit = mRange.Groups[3].Value;
            var mult = unit.Contains("k") || unit.Contains("nghìn") || unit.Contains("ngàn") ? 1_000m : 1_000_000m;
            if (decimal.TryParse(mRange.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v1) &&
                decimal.TryParse(mRange.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v2))
            {
                var min = v1 < 1000m ? v1 * mult : v1;
                var max = v2 < 1000m ? v2 * mult : v2;
                if (min > max) (min, max) = (max, min);
                return new BudgetInfo(min, max, $"từ {min:N0}đ đến {max:N0}đ");
            }
        }

        // 2. Mức tối đa: "dưới X triệu", "không quá X triệu", "tối đa X tr", "<= X triệu"
        var maxPattern = @"(?:dưới|không quá|nhỏ hơn|ít hơn|tối đa|hạ|thấp hơn|<|<=)\s*(\d+(?:\.\d+)?)\s*(tr|triệu|k|nghìn|ngàn)?";
        var mMax = Regex.Match(q, maxPattern);
        if (mMax.Success)
        {
            var unit = mMax.Groups[2].Value;
            var mult = unit.Contains("k") || unit.Contains("nghìn") || unit.Contains("ngàn") ? 1_000m : 1_000_000m;
            if (decimal.TryParse(mMax.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var max = v < 1000m ? v * mult : v;
                return new BudgetInfo(null, max, $"dưới {max:N0}đ");
            }
        }

        // 3. Mức tối thiểu: "trên X triệu", "hơn X triệu", "từ X triệu trở lên", ">= X tr"
        var minPattern = @"(?:trên|hơn|lớn hơn|tối thiểu|cao hơn|>|>=)\s*(\d+(?:\.\d+)?)\s*(tr|triệu|k|nghìn|ngàn)?";
        var mMin = Regex.Match(q, minPattern);
        if (mMin.Success)
        {
            var unit = mMin.Groups[2].Value;
            var mult = unit.Contains("k") || unit.Contains("nghìn") || unit.Contains("ngàn") ? 1_000m : 1_000_000m;
            if (decimal.TryParse(mMin.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var min = v < 1000m ? v * mult : v;
                return new BudgetInfo(min, null, $"trên {min:N0}đ");
            }
        }

        // 4. Mức xấp xỉ: "tầm X triệu", "tầm 10tr", "khoảng X triệu", "cỡ X tr"
        var approxPattern = @"(?:tầm|khoảng|cỡ|quanh|mức)\s*(\d+(?:\.\d+)?)\s*(tr|triệu|k|nghìn|ngàn)?";
        var mApprox = Regex.Match(q, approxPattern);
        if (mApprox.Success)
        {
            var unit = mApprox.Groups[2].Value;
            var mult = unit.Contains("k") || unit.Contains("nghìn") || unit.Contains("ngàn") ? 1_000m : 1_000_000m;
            if (decimal.TryParse(mApprox.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var target = v < 1000m ? v * mult : v;
                var min = target * 0.85m;
                var max = target * 1.15m;
                return new BudgetInfo(min, max, $"tầm {target:N0}đ (khoảng {min:N0}đ - {max:N0}đ)");
            }
        }

        return null;
    }

    public async Task<(string ContextText, List<ProductRecommendationDto> Products)> GetProductRecommendationsAsync(string query)
    {
        var timer = Stopwatch.StartNew();
        var rawQuery = (query ?? "").Trim();
        var keyword = rawQuery.ToLowerInvariant();
        var budget = ExtractBudget(rawQuery);

        var source = _db.SanPhams.AsNoTracking();

        // 1. Lọc theo danh mục / loại thiết bị
        if (keyword.Contains("laptop") || keyword.Contains("máy tính xách tay"))
        {
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("laptop") ||
                    p.TenSanPham.ToLower().Contains("macbook") ||
                    p.TenSanPham.ToLower().Contains("xps") ||
                    p.TenSanPham.ToLower().Contains("thinkpad") ||
                    p.TenSanPham.ToLower().Contains("inspiron") ||
                    p.TenSanPham.ToLower().Contains("vivobook") ||
                    (p.MaDanhMucNavigation != null && p.MaDanhMucNavigation.TenDanhMuc != null &&
                     p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("laptop"))
                ));
        }
        else if (keyword.Contains("điện thoại") || keyword.Contains("dien thoai") || keyword.Contains("smartphone"))
        {
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("điện thoại") ||
                    p.TenSanPham.ToLower().Contains("iphone") ||
                    p.TenSanPham.ToLower().Contains("galaxy") ||
                    p.TenSanPham.ToLower().Contains("smartphone") ||
                    (p.MaDanhMucNavigation != null && p.MaDanhMucNavigation.TenDanhMuc != null &&
                     p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("điện thoại"))
                ));
        }

        // 2. Chỉ lấy các sản phẩm CÓ BIẾN THỂ CÒN HÀNG (SoLuong > 0)
        source = source.Where(p => p.BienTheSanPhams.Any(b => b.SoLuong > 0));

        // 3. Truy vấn sản phẩm và tính toán giá thấp nhất từ biến thể còn hàng
        var queryable = source.Select(p => new
        {
            p.MaSanPham,
            p.TenSanPham,
            Description = p.MoTa == null
                ? ""
                : p.MoTa.Length > 600
                    ? p.MoTa.Substring(0, 600)
                    : p.MoTa,
            InStock = p.BienTheSanPhams.Any(b => b.SoLuong > 0),
            MinPrice = p.BienTheSanPhams
                .Where(b => b.SoLuong > 0)
                .Select(b => (decimal?)b.Gia)
                .Min(),
            ImageUrl = p.SanPhamHinhAnhs
                .OrderByDescending(img => img.LaAnhChinh)
                .ThenBy(img => img.ThuTu)
                .Select(img => img.DuongDanAnh)
                .FirstOrDefault()
        });

        // 4. Lọc ngân sách nghiêm ngặt
        if (budget != null)
        {
            if (budget.MaxPrice.HasValue)
            {
                queryable = queryable.Where(p => p.MinPrice.HasValue && p.MinPrice.Value <= budget.MaxPrice.Value);
            }
            if (budget.MinPrice.HasValue)
            {
                queryable = queryable.Where(p => p.MinPrice.HasValue && p.MinPrice.Value >= budget.MinPrice.Value);
            }
        }

        // Ưu tiên sản phẩm có giá phù hợp, sắp xếp theo giá tăng dần
        var rawProducts = await queryable
            .OrderBy(p => p.MinPrice)
            .Take(3)
            .ToListAsync();

        timer.Stop();

        _logger.LogInformation(
            "EMUA AI: Truy vấn sản phẩm mất {ElapsedMs} ms; lấy được {Count} sản phẩm. Ngân sách: {Budget}",
            timer.ElapsedMilliseconds,
            rawProducts.Count,
            budget?.RawDescription ?? "Không giới hạn");

        var productDtos = rawProducts.Select(p => new ProductRecommendationDto(
            Id: p.MaSanPham,
            Name: p.TenSanPham ?? "Sản phẩm EMUA",
            MinPrice: p.MinPrice,
            InStock: p.InStock,
            ImageUrl: p.ImageUrl,
            DetailUrl: $"/Product/Details/{p.MaSanPham}"
        )).ToList();

        // 5. Trường hợp KHÔNG có sản phẩm nào thỏa mãn ngân sách
        if (rawProducts.Count == 0)
        {
            // Kiểm tra xem trong danh mục có sản phẩm nào không (để biết mức giá thấp nhất hiện có)
            var lowestExisting = await source
                .Select(p => p.BienTheSanPhams.Where(b => b.SoLuong > 0).Select(b => (decimal?)b.Gia).Min())
                .Where(g => g.HasValue)
                .OrderBy(g => g)
                .FirstOrDefaultAsync();

            var cultureVN = CultureInfo.GetCultureInfo("vi-VN");
            var minExistingStr = lowestExisting.HasValue && lowestExisting.Value > 0
                ? lowestExisting.Value.ToString("N0", cultureVN) + " VNĐ"
                : null;

            var emptyContextBuilder = new StringBuilder();
            emptyContextBuilder.AppendLine("[THÔNG BÁO VỀ TỒN KHO VÀ NGÂN SÁCH CỦA CỬA HÀNG EMUA]:");
            
            if (budget != null)
            {
                emptyContextBuilder.AppendLine($"- Khách hàng đang tìm sản phẩm với mức ngân sách: {budget.RawDescription}.");
                emptyContextBuilder.AppendLine("- Hiện tại trong cửa hàng EMUA KHÔNG CÓ sản phẩm nào còn hàng nằm trong mức giá/ngân sách này.");
                if (minExistingStr != null)
                {
                    emptyContextBuilder.AppendLine($"- Mẫu sản phẩm thuộc nhóm này có giá tốt nhất hiện tại của EMUA khởi điểm từ {minExistingStr}.");
                }
                emptyContextBuilder.AppendLine("- BẮT BUỘC: Bạn phải thông báo rõ ràng cho khách rằng cửa hàng chưa có mẫu sản phẩm nào còn hàng ở tầm giá này.");
                emptyContextBuilder.AppendLine("- TUYỆT ĐỐI KHÔNG tự ý đề xuất các sản phẩm vượt quá ngân sách khách yêu cầu.");
                emptyContextBuilder.AppendLine("- Lịch sự hỏi xem khách có muốn điều chỉnh thêm ngân sách hoặc quan tâm đến hình thức trả góp 0% / các phân khúc khác hay không.");
            }
            else
            {
                emptyContextBuilder.AppendLine("- Không tìm thấy sản phẩm còn hàng phù hợp với từ khóa này.");
                emptyContextBuilder.AppendLine("- Hãy hỏi rõ thêm tên thiết bị hoặc tầm ngân sách cụ thể để hỗ trợ khách chính xác.");
            }

            return (emptyContextBuilder.ToString(), new List<ProductRecommendationDto>());
        }

        // 6. Xây dựng ngữ cảnh sản phẩm khi tìm thấy đúng ngân sách
        var context = new StringBuilder();
        context.AppendLine("[DỮ LIỆU SẢN PHẨM TỪ CỬA HÀNG EMUA - ĐÃ XÁC THỰC THEO NGÂN SÁCH VÀ CÒN HÀNG]:");
        context.AppendLine("- Dưới đây là các sản phẩm ĐÃ ĐƯỢC HỆ THỐNG XÁC THỰC phù hợp với ngân sách và còn hàng thực tế trong kho.");
        if (budget != null)
        {
            context.AppendLine($"- Yêu cầu ngân sách của khách: {budget.RawDescription}.");
        }
        context.AppendLine("- Bạn hãy giới thiệu các sản phẩm này một cách tự nhiên, nêu bật ưu điểm và lý do phù hợp với tầm giá của khách.");
        context.AppendLine("- TUYỆT ĐỐI KHÔNG tự ý giới thiệu thêm sản phẩm nào ngoài danh sách dưới đây.\n");

        var culture = CultureInfo.GetCultureInfo("vi-VN");
        foreach (var p in rawProducts)
        {
            var price = p.MinPrice.HasValue && p.MinPrice.Value > 0
                ? p.MinPrice.Value.ToString("N0", culture) + " VNĐ"
                : "Liên hệ cửa hàng";

            var stock = p.InStock ? "Còn hàng" : "Hết hàng";
            var description = ShortDescription(p.Description);

            context.AppendLine(
                $"• Mã: {p.MaSanPham} | Tên: {p.TenSanPham} | Giá từ: {price} | Tình trạng: {stock} | Đặc điểm: {description}");
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

        var text = Regex.Replace(value, "<[^>]*>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length <= 200
            ? text
            : text.Substring(0, 200) + "…";
    }
}
