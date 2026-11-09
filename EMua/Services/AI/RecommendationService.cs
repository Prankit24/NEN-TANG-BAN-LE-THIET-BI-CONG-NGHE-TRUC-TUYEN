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
    string DetailUrl,
    string? Description = null,
    string? Specs = null
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

        // 5. Cụm từ chứa giá trực tiếp: "laptop 20 triệu", "20tr", "giá 20 triệu", "tầm 20 củ"
        var directPattern = @"(?:giá|tầm|cỡ|khoảng)?\s*(\d+(?:\.\d+)?)\s*(?:tr|triệu|củ)\b";
        var mDirect = Regex.Match(q, directPattern);
        if (mDirect.Success)
        {
            if (decimal.TryParse(mDirect.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var target = v < 1000m ? v * 1_000_000m : v;
                var min = target * 0.80m;
                var max = target * 1.15m;
                return new BudgetInfo(min, max, $"tầm {target:N0}đ (khoảng {min:N0}đ - {max:N0}đ)");
            }
        }

        return null;
    }

    public static bool IsFollowUpQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var q = query.Trim().ToLowerInvariant();

        // 1. Chứa đại từ chỉ định hoặc từ tham chiếu sản phẩm
        string[] referenceWords = new[]
        {
            "con này", "máy này", "em này", "chiếc này", "bản này", "mẫu này", "sản phẩm này",
            "máy đó", "con đó", "mẫu đó", "cái này", "cái đó", "thứ nhất", "thứ hai", "thứ 2",
            "thứ ba", "thứ 3", "đầu tiên"
        };
        if (referenceWords.Any(w => q.Contains(w)))
            return true;

        // 2. Câu hỏi hỏi tiếp về mục đích sử dụng hoặc thuộc tính của sản phẩm
        string[] usagePhrases = new[]
        {
            "dùng để", "dùng cho", "phù hợp", "ổn không", "tốt không", "mượt không",
            "lập trình", "học lập trình", "chơi game", "làm đồ họa", "thiết kế", "văn phòng",
            "học tập", "chạy máy ảo", "render", "pin thế nào", "pin dùng", "bao nhiêu kg",
            "nặng không", "nâng cấp", "màu gì", "bảo hành mấy năm", "có nên mua"
        };
        if (usagePhrases.Any(w => q.Contains(w)))
            return true;

        return false;
    }

    public static bool IsShoppingQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var q = query.Trim().ToLowerInvariant();

        // 0. Nhận diện câu hỏi tiếp nối về sản phẩm đã tư vấn
        if (IsFollowUpQuery(q))
            return true;

        // 1. Nhận diện có ngân sách
        if (ExtractBudget(q) != null)
            return true;

        // 2. Từ khóa loại thiết bị / danh mục sản phẩm công nghệ
        string[] productKeywords = new[]
        {
            "laptop", "máy tính", "macbook", "xps", "thinkpad", "vivobook", "inspiron",
            "zenbook", "asus", "dell", "hp", "lenovo", "acer", "msi",
            "điện thoại", "dien thoai", "smartphone", "iphone", "samsung", "galaxy", "xiaomi", "oppo", "redmi",
            "tai nghe", "chuột", "bàn phím", "màn hình", "loa", "sạc", "cáp", "phụ kiện",
            "ipad", "tablet", "máy tính bảng", "linh kiện"
        };

        if (productKeywords.Any(k => q.Contains(k)))
            return true;

        // 3. Từ khóa ý định mua sắm
        string[] shoppingKeywords = new[]
        {
            "mua", "tìm", "tư vấn", "cần mua", "muốn mua", "chọn mua", "tầm giá",
            "ngân sách", "bao nhiêu tiền", "giá bao nhiêu", "có bán", "còn hàng",
            "đề xuất", "gợi ý", "mẫu nào", "loại nào", "hãng nào", "nên mua", "sản phẩm"
        };

        return shoppingKeywords.Any(k => q.Contains(k));
    }

    public async Task<(string ContextText, List<ProductRecommendationDto> Products, decimal? LowestExistingPrice)> GetProductRecommendationsAsync(string query, System.Threading.CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var rawQuery = (query ?? "").Trim();
        var keyword = rawQuery.ToLowerInvariant();

        // Với câu hỏi không liên quan đến mua sắm, không tự đề xuất sản phẩm
        if (!IsShoppingQuery(rawQuery))
        {
            _logger.LogInformation("EMUA AI: Câu hỏi không thuộc ý định mua sắm. Bỏ qua truy vấn sản phẩm.");
            return ("", new List<ProductRecommendationDto>(), null);
        }

        var budget = ExtractBudget(rawQuery);
        var source = _db.SanPhams.AsNoTracking();

        // 1. Lọc theo danh mục / loại thiết bị
        if (keyword.Contains("laptop") || keyword.Contains("máy tính xách tay") || keyword.Contains("macbook"))
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
        else if (keyword.Contains("điện thoại") || keyword.Contains("dien thoai") || keyword.Contains("smartphone") || keyword.Contains("iphone") || keyword.Contains("galaxy"))
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
        else if (keyword.Contains("tai nghe") || keyword.Contains("chuột") || keyword.Contains("bàn phím") || keyword.Contains("màn hình"))
        {
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    (keyword.Contains("tai nghe") && p.TenSanPham.ToLower().Contains("tai nghe")) ||
                    (keyword.Contains("chuột") && p.TenSanPham.ToLower().Contains("chuột")) ||
                    (keyword.Contains("bàn phím") && p.TenSanPham.ToLower().Contains("bàn phím")) ||
                    (keyword.Contains("màn hình") && p.TenSanPham.ToLower().Contains("màn hình"))
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
            Specs = p.ThongSoKyThuat == null
                ? ""
                : p.ThongSoKyThuat.Length > 800
                    ? p.ThongSoKyThuat.Substring(0, 800)
                    : p.ThongSoKyThuat,
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
            .ToListAsync(cancellationToken);

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
            DetailUrl: $"/Product/Details/{p.MaSanPham}",
            Description: p.Description,
            Specs: p.Specs
        )).ToList();

        // 5. Trường hợp KHÔNG có sản phẩm nào thỏa mãn ngân sách
        if (rawProducts.Count == 0)
        {
            // Kiểm tra xem trong danh mục có sản phẩm nào không (để biết mức giá thấp nhất hiện có)
            var lowestExisting = await source
                .Select(p => p.BienTheSanPhams.Where(b => b.SoLuong > 0).Select(b => (decimal?)b.Gia).Min())
                .Where(g => g.HasValue)
                .OrderBy(g => g)
                .FirstOrDefaultAsync(cancellationToken);

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

            return (emptyContextBuilder.ToString(), new List<ProductRecommendationDto>(), lowestExisting);
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

        return (context.ToString(), productDtos, rawProducts.FirstOrDefault()?.MinPrice);
    }

    public async Task<string> GetProductContextAsync(string query, System.Threading.CancellationToken cancellationToken = default)
    {
        var result = await GetProductRecommendationsAsync(query, cancellationToken);
        return result.ContextText;
    }

    public async Task<ProductRecommendationDto?> GetProductByIdAsync(int id, System.Threading.CancellationToken cancellationToken = default)
    {
        var p = await _db.SanPhams.AsNoTracking()
            .Where(x => x.MaSanPham == id)
            .Select(x => new
            {
                x.MaSanPham,
                x.TenSanPham,
                Description = x.MoTa == null ? "" : x.MoTa.Length > 600 ? x.MoTa.Substring(0, 600) : x.MoTa,
                Specs = x.ThongSoKyThuat == null ? "" : x.ThongSoKyThuat.Length > 800 ? x.ThongSoKyThuat.Substring(0, 800) : x.ThongSoKyThuat,
                InStock = x.BienTheSanPhams.Any(b => b.SoLuong > 0),
                MinPrice = x.BienTheSanPhams.Where(b => b.SoLuong > 0).Select(b => (decimal?)b.Gia).Min(),
                ImageUrl = x.SanPhamHinhAnhs.OrderByDescending(img => img.LaAnhChinh).ThenBy(img => img.ThuTu).Select(img => img.DuongDanAnh).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (p == null) return null;

        return new ProductRecommendationDto(
            Id: p.MaSanPham,
            Name: p.TenSanPham ?? "Sản phẩm EMUA",
            MinPrice: p.MinPrice,
            InStock: p.InStock,
            ImageUrl: p.ImageUrl,
            DetailUrl: $"/Product/Details/{p.MaSanPham}",
            Description: p.Description,
            Specs: p.Specs
        );
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
