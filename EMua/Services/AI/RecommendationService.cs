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

        // 1. Chứa đại từ chỉ định hoặc từ tham chiếu trực tiếp đến sản phẩm đang thảo luận
        string[] referenceWords = new[]
        {
            "máy này", "con này", "em này", "chiếc này", "bản này", "mẫu này", "sản phẩm này", "thiết bị này",
            "máy đó", "con đó", "mẫu đó", "chiếc đó", "bản đó", "sản phẩm đó",
            "cái này", "cái đó", "thứ nhất", "thứ hai", "thứ 2", "thứ ba", "thứ 3", "đầu tiên"
        };
        if (referenceWords.Any(w => q.Contains(w)))
            return true;

        // Nếu câu hỏi về ngành nghề, trường học, hướng nghiệp, học thuật mà không có đại từ tham chiếu sản phẩm -> KHÔNG phải follow-up sản phẩm
        string[] nonProductTopics = new[]
        {
            "ngành", "học ngành", "nghề", "trường", "khoa học", "đại học", "cao đẳng",
            "tuyển sinh", "việc làm", "xin việc", "mức lương", "lương bổng", "tương lai",
            "thị trường", "xu hướng"
        };
        if (nonProductTopics.Any(t => q.Contains(t)))
            return false;

        // 2. Hỏi trực tiếp về thông số phần cứng, giá bán, tồn kho hoặc cấu hình sản phẩm
        string[] specKeywords = new[]
        {
            "ram", "cpu", "chip", "ổ cứng", "ssd", "hdd", "màn hình", "card", "vga", "gpu",
            "pin", "trọng lượng", "bao nhiêu kg", "nặng bao nhiêu", "cấu hình", "thông số",
            "giá bao nhiêu", "bao nhiêu tiền", "giá sao", "nhiêu tiền", "giá thế nào", "bao tiền",
            "còn hàng", "tồn kho", "sẵn hàng", "còn mấy", "có sẵn", "bảo hành mấy năm", "bảo hành bao lâu"
        };
        if (specKeywords.Any(w => q.Contains(w)))
            return true;

        // 3. Câu hỏi hỏi tiếp về mục đích sử dụng hoặc thuộc tính của thiết bị phần cứng cụ thể
        string[] specificDeviceUsage = new[]
        {
            "dùng để lập trình", "dùng để code", "dùng để render", "dùng để chơi game",
            "dùng cho lập trình", "dùng cho đồ họa", "dùng cho văn phòng",
            "chạy máy ảo", "chạy docker", "chạy giả lập",
            "pin dùng", "pin thế nào", "pin được bao lâu", "nặng bao nhiêu kg", "bao nhiêu kg",
            "nặng không", "nâng cấp ram", "nâng cấp ssd", "có nâng cấp được",
            "bảo hành mấy năm", "có sẵn win", "có cổng type c", "màn hình mấy hz"
        };
        if (specificDeviceUsage.Any(w => q.Contains(w)))
            return true;

        return false;
    }

    public static bool IsShoppingQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var q = query.Trim().ToLowerInvariant();

        // 0. LOẠI TRỪ câu hỏi kiến thức chung, định hướng ngành nghề, học tập lý thuyết (không nhằm mục đích mua sắm)
        // Ví dụ: "Học ngành AI ổn không?", "Trí tuệ nhân tạo là gì?", "Học CNTT ra trường làm gì?"
        string[] generalKnowledgePhrases = new[]
        {
            "học ngành", "ngành học", "ngành trí tuệ nhân tạo", "ngành ai", "ngành công nghệ thông tin",
            "ngành cntt", "ngành khoa học máy tính", "ngành phần mềm", "ngành hệ thống",
            "định hướng ngành", "định hướng nghề", "cơ hội việc làm", "mức lương",
            "là gì", "khái niệm", "lịch sử", "xu hướng phát triển", "tương lai ngành"
        };

        bool isGeneralKnowledge = generalKnowledgePhrases.Any(g => q.Contains(g));

        // Kiểm tra xem có từ khóa thiết bị cụ thể đi kèm không (ví dụ "laptop học ngành ai", "máy tính học ai")
        string[] deviceKeywords = new[]
        {
            "laptop", "máy tính", "macbook", "pc", "điện thoại", "smartphone", "iphone", "samsung",
            "tai nghe", "chuột", "bàn phím", "màn hình", "ipad", "tablet", "mua máy"
        };
        bool hasDeviceKeyword = deviceKeywords.Any(d => q.Contains(d));

        // Nếu là câu hỏi kiến thức chung mà KHÔNG nhắc đến thiết bị phần cứng cụ thể -> KHÔNG phải shopping
        if (isGeneralKnowledge && !hasDeviceKeyword)
            return false;

        // 1. Nhận diện có ngân sách
        if (ExtractBudget(q) != null)
            return true;

        // 2. Từ khóa loại thiết bị / danh mục sản phẩm công nghệ
        string[] productKeywords = new[]
        {
            "laptop", "máy tính", "macbook", "xps", "thinkpad", "vivobook", "inspiron",
            "zenbook", "ideapad", "legion", "asus", "dell", "hp", "lenovo", "acer", "msi",
            "điện thoại", "dien thoai", "smartphone", "iphone", "samsung", "galaxy", "xiaomi", "oppo", "redmi",
            "tai nghe", "chuột", "bàn phím", "màn hình", "loa", "sạc", "cáp", "phụ kiện",
            "ipad", "tablet", "máy tính bảng", "linh kiện"
        };

        if (productKeywords.Any(k => q.Contains(k)))
            return true;

        // 3. Từ khóa ý định mua sắm
        string[] shoppingKeywords = new[]
        {
            "mua", "tìm mua", "cần mua", "muốn mua", "chọn mua", "tư vấn mua", "tầm giá",
            "ngân sách", "bao nhiêu tiền", "giá bao nhiêu", "báo giá", "có bán", "còn hàng",
            "đề xuất", "gợi ý", "mẫu nào", "loại nào", "hãng nào", "nên mua"
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
        bool categoryFiltered = false;

        // 1. Lọc theo danh mục / loại thiết bị
        if (keyword.Contains("laptop") || keyword.Contains("máy tính") || keyword.Contains("macbook") ||
            keyword.Contains("máy học ai") || keyword.Contains("học ai") || keyword.Contains("trí tuệ nhân tạo") ||
            keyword.Contains("lập trình") || keyword.Contains("đồ họa") ||
            keyword.Contains("xps") || keyword.Contains("thinkpad") || keyword.Contains("ideapad") ||
            keyword.Contains("vivobook") || keyword.Contains("zenbook") || keyword.Contains("legion") ||
            keyword.Contains("tuf") || keyword.Contains("rog"))
        {
            categoryFiltered = true;
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("laptop") ||
                    p.TenSanPham.ToLower().Contains("macbook") ||
                    p.TenSanPham.ToLower().Contains("xps") ||
                    p.TenSanPham.ToLower().Contains("thinkpad") ||
                    p.TenSanPham.ToLower().Contains("inspiron") ||
                    p.TenSanPham.ToLower().Contains("vivobook") ||
                    p.TenSanPham.ToLower().Contains("ideapad") ||
                    p.TenSanPham.ToLower().Contains("legion") ||
                    p.TenSanPham.ToLower().Contains("tuf") ||
                    p.TenSanPham.ToLower().Contains("rog") ||
                    (p.MaDanhMucNavigation != null && p.MaDanhMucNavigation.TenDanhMuc != null &&
                     p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("laptop"))
                ));
        }
        else if (keyword.Contains("điện thoại") || keyword.Contains("dien thoai") || keyword.Contains("smartphone") ||
                 keyword.Contains("iphone") || keyword.Contains("galaxy") || keyword.Contains("samsung") ||
                 keyword.Contains("xiaomi") || keyword.Contains("oppo") || keyword.Contains("redmi"))
        {
            categoryFiltered = true;
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
        else if (keyword.Contains("tai nghe") || keyword.Contains("chuột") || keyword.Contains("bàn phím") ||
                 keyword.Contains("màn hình") || keyword.Contains("loa") || keyword.Contains("cáp") ||
                 keyword.Contains("sạc") || keyword.Contains("phụ kiện"))
        {
            categoryFiltered = true;
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    (keyword.Contains("tai nghe") && p.TenSanPham.ToLower().Contains("tai nghe")) ||
                    (keyword.Contains("chuột") && p.TenSanPham.ToLower().Contains("chuột")) ||
                    (keyword.Contains("bàn phím") && p.TenSanPham.ToLower().Contains("bàn phím")) ||
                    (keyword.Contains("màn hình") && p.TenSanPham.ToLower().Contains("màn hình")) ||
                    (keyword.Contains("loa") && p.TenSanPham.ToLower().Contains("loa")) ||
                    (keyword.Contains("cáp") && p.TenSanPham.ToLower().Contains("cáp")) ||
                    (keyword.Contains("sạc") && p.TenSanPham.ToLower().Contains("sạc")) ||
                    (p.MaDanhMucNavigation != null && p.MaDanhMucNavigation.TenDanhMuc != null &&
                     (p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("phụ kiện") ||
                      p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("tai nghe") ||
                      p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("chuột") ||
                      p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("bàn phím")))
                ));
        }
        else if (keyword.Contains("ipad") || keyword.Contains("tablet") || keyword.Contains("máy tính bảng"))
        {
            categoryFiltered = true;
            source = source.Where(p =>
                p.TenSanPham != null &&
                (
                    p.TenSanPham.ToLower().Contains("ipad") ||
                    p.TenSanPham.ToLower().Contains("tablet") ||
                    p.TenSanPham.ToLower().Contains("tab") ||
                    (p.MaDanhMucNavigation != null && p.MaDanhMucNavigation.TenDanhMuc != null &&
                     p.MaDanhMucNavigation.TenDanhMuc.ToLower().Contains("tablet"))
                ));
        }

        // Nếu không thuộc danh mục nhận diện được:
        if (!categoryFiltered)
        {
            var stopWords = new HashSet<string> { "mua", "cần", "muốn", "tìm", "tư", "vấn", "giá", "tầm", "khoảng", "dưới", "trên", "triệu", "hàng", "còn", "không", "sản", "phẩm", "loại", "nào", "mẫu", "cho", "em", "mình" };
            var searchTerms = rawQuery.Split(new[] { ' ', ',', '.', '?', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2 && !stopWords.Contains(w.ToLowerInvariant()))
                .Select(w => w.ToLowerInvariant())
                .ToList();

            if (searchTerms.Any())
            {
                source = source.Where(p => p.TenSanPham != null &&
                    searchTerms.Any(term => p.TenSanPham.ToLower().Contains(term)));
            }
            else
            {
                // Không nhận diện được danh mục hoặc sản phẩm -> Tuyệt đối không lấy 3 sản phẩm mặc định
                _logger.LogInformation("EMUA AI: Không nhận diện được danh mục hoặc sản phẩm cụ thể. Bỏ qua gợi ý.");
                return ("", new List<ProductRecommendationDto>(), null);
            }
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

    public async Task<(bool Handled, string Answer, ProductRecommendationDto? Product)> TryGetDirectProductAnswerAsync(
        int productId,
        string message,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var p = await _db.SanPhams
            .AsNoTracking()
            .Include(x => x.BienTheSanPhams)
            .Include(x => x.SanPhamHinhAnhs)
            .FirstOrDefaultAsync(x => x.MaSanPham == productId, cancellationToken);

        if (p == null)
            return (false, "", null);

        var inStockVariants = p.BienTheSanPhams.Where(b => b.SoLuong > 0).ToList();
        var minPrice = inStockVariants.Any()
            ? inStockVariants.Min(b => (decimal?)b.Gia)
            : p.GiaGoc;
        var inStock = inStockVariants.Any() || p.BienTheSanPhams.Any(b => b.TrangThai == "Còn hàng");
        var imageUrl = p.SanPhamHinhAnhs
            .OrderByDescending(img => img.LaAnhChinh)
            .ThenBy(img => img.ThuTu)
            .Select(img => img.DuongDanAnh)
            .FirstOrDefault();

        var productDto = new ProductRecommendationDto(
            Id: p.MaSanPham,
            Name: p.TenSanPham ?? "Sản phẩm EMUA",
            MinPrice: minPrice,
            InStock: inStock,
            ImageUrl: imageUrl,
            DetailUrl: $"/Product/Details/{p.MaSanPham}",
            Description: p.MoTa,
            Specs: p.ThongSoKyThuat
        );

        var q = (message ?? "").ToLowerInvariant().Trim();
        var specs = p.ThongSoKyThuat ?? "";
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        var name = p.TenSanPham ?? "sản phẩm";

        // Tách các dòng hoặc phân đoạn thông số từ CSDL
        var specSegments = specs.Split(new[] { '\r', '\n', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // 1. Hỏi về RAM
        if (q.Contains("ram") || q.Contains("bộ nhớ ram") || (q.Contains("bộ nhớ") && !q.Contains("lưu trữ") && !q.Contains("ssd") && !q.Contains("ổ cứng")))
        {
            string? ramValue = null;
            foreach (var s in specSegments)
            {
                if (Regex.IsMatch(s, @"\b(?:RAM|Bộ nhớ RAM)\b", RegexOptions.IgnoreCase))
                {
                    var clean = Regex.Replace(s, @"^(?:RAM|Bộ nhớ RAM)\s*[:=-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        ramValue = clean;
                        break;
                    }
                }
                else if (s.Contains("Unified Memory", StringComparison.OrdinalIgnoreCase))
                {
                    ramValue = s;
                    break;
                }
            }

            // Nếu chưa có trong ThongSoKyThuat, kiểm tra các phiên bản biến thể
            if (string.IsNullOrWhiteSpace(ramValue))
            {
                var variantRams = p.BienTheSanPhams
                    .Select(b => b.PhienBan)
                    .Where(pb => !string.IsNullOrWhiteSpace(pb))
                    .Select(pb => Regex.Match(pb!, @"\b\d+\s*GB\b", RegexOptions.IgnoreCase).Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .ToList();

                if (variantRams.Any())
                {
                    ramValue = string.Join(" hoặc ", variantRams);
                }
            }

            if (!string.IsNullOrWhiteSpace(ramValue))
            {
                var answer = $"Dạ mẫu **{name}** có thông số RAM là **{ramValue}** bạn nhé! Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem thêm thông tin chi tiết và ưu đãi tại EMUA.";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại trên hệ thống dữ liệu kỹ thuật của EMUA chưa có thông tin chi tiết về dung lượng RAM cho mẫu **{name}**. Bạn vui lòng bấm vào nút 'Xem sản phẩm' bên dưới để xem thêm catalogue chi tiết hoặc liên hệ nhân viên EMUA để kiểm tra trực tiếp máy nhé!";
                return (true, answer, productDto);
            }
        }

        // 2. Hỏi về CPU / Chip / Vi xử lý
        if (q.Contains("cpu") || q.Contains("chip") || q.Contains("vi xử lý") || q.Contains("core i") || q.Contains("ryzen") || q.Contains("bộ vi xử lý"))
        {
            string? cpuValue = null;
            foreach (var s in specSegments)
            {
                if (Regex.IsMatch(s, @"\b(?:CPU|Bộ vi xử lý|Vi xử lý|Chip)\b", RegexOptions.IgnoreCase))
                {
                    var clean = Regex.Replace(s, @"^(?:CPU|Bộ vi xử lý|Vi xử lý|Chip)\s*[:=-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        cpuValue = clean;
                        break;
                    }
                }
                else if (s.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("Core i", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("Apple M", StringComparison.OrdinalIgnoreCase))
                {
                    cpuValue = s;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(cpuValue))
            {
                var answer = $"Dạ mẫu **{name}** được trang bị vi xử lý **{cpuValue}** bạn nhé! Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết cấu hình và ưu đãi tại EMUA.";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại trên hệ thống của EMUA chưa có thông số chi tiết về CPU/Chip cho mẫu **{name}**. Bạn có thể bấm vào nút 'Xem sản phẩm' bên dưới để xem catalogue đầy đủ từ nhà sản xuất nhé!";
                return (true, answer, productDto);
            }
        }

        // 3. Hỏi về Giá bán
        if (q.Contains("giá") || q.Contains("bao nhiêu tiền") || q.Contains("nhiêu tiền") || q.Contains("bao tiền") || q.Contains("giá sao") || q.Contains("giá thế nào"))
        {
            var sbPrice = new StringBuilder();
            var minPriceStr = minPrice.HasValue && minPrice.Value > 0
                ? minPrice.Value.ToString("N0", culture) + " VNĐ"
                : "Liên hệ cửa hàng";

            sbPrice.AppendLine($"Dạ mẫu **{name}** hiện đang có giá bán tại EMUA khởi điểm từ **{minPriceStr}**.");
            if (p.BienTheSanPhams.Count > 1)
            {
                sbPrice.AppendLine("Các phiên bản chi tiết đang sẵn hàng tại cửa hàng:");
                foreach (var b in p.BienTheSanPhams.OrderBy(b => b.Gia))
                {
                    var label = string.Join(" - ", new[] { b.MauSac, b.PhienBan }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (string.IsNullOrWhiteSpace(label)) label = "Bản tiêu chuẩn";
                    var stockStr = b.SoLuong > 0 ? $"còn {b.SoLuong} máy" : "tạm hết";
                    sbPrice.AppendLine($"• {label}: **{b.Gia:N0} VNĐ** ({stockStr})");
                }
            }
            sbPrice.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem ưu đãi và đặt mua trực tiếp nhé!");
            return (true, sbPrice.ToString(), productDto);
        }

        // 4. Hỏi về Tồn kho / Còn hàng không
        if (q.Contains("còn hàng") || q.Contains("còn ko") || q.Contains("còn không") || q.Contains("tồn kho") || q.Contains("sẵn hàng") || q.Contains("còn mấy") || q.Contains("có sẵn"))
        {
            var totalStock = p.BienTheSanPhams.Where(b => b.SoLuong > 0).Sum(b => b.SoLuong);
            var minPriceStr = minPrice.HasValue && minPrice.Value > 0
                ? minPrice.Value.ToString("N0", culture) + " VNĐ"
                : "";

            if (totalStock > 0)
            {
                var answer = $"Dạ mẫu **{name}** hiện **còn hàng** tại EMUA (sẵn sàng giao ngay hoặc trải nghiệm tại showroom) với số lượng thực tế là **{totalStock} sản phẩm**, mức giá từ **{minPriceStr}** bạn nhé!";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ mẫu **{name}** hiện đang tạm hết hàng tại kho EMUA. Bạn có thể bấm 'Xem sản phẩm' bên dưới để nhận thông báo khi có hàng lại hoặc tham khảo các mẫu tương đương tại cửa hàng nhé!";
                return (true, answer, productDto);
            }
        }

        // 5. Hỏi về Ổ cứng / SSD / Lưu trữ
        if (q.Contains("ổ cứng") || q.Contains("ssd") || q.Contains("hdd") || (q.Contains("dung lượng") && !q.Contains("pin")) || q.Contains("lưu trữ"))
        {
            string? ssdValue = null;
            foreach (var s in specSegments)
            {
                if (Regex.IsMatch(s, @"\b(?:Ổ cứng|SSD|HDD|Lưu trữ|Bộ nhớ lưu trữ)\b", RegexOptions.IgnoreCase))
                {
                    var clean = Regex.Replace(s, @"^(?:Ổ cứng|SSD|HDD|Lưu trữ|Bộ nhớ lưu trữ)\s*[:=-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        ssdValue = clean;
                        break;
                    }
                }
                else if (Regex.IsMatch(s, @"\b\d+\s*(?:GB|TB)\s*(?:SSD|NVMe|PCIe|HDD)\b", RegexOptions.IgnoreCase))
                {
                    ssdValue = s;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(ssdValue))
            {
                var answer = $"Dạ mẫu **{name}** được trang bị ổ cứng **{ssdValue}** bạn nhé! Ổ cứng tốc độ cao giúp máy khởi động và mở ứng dụng cực nhanh.";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại trên hệ thống của EMUA chưa có thông số chi tiết về ổ cứng/SSD cho mẫu **{name}**. Bạn vui lòng bấm vào nút 'Xem sản phẩm' bên dưới để xem thêm nhé!";
                return (true, answer, productDto);
            }
        }

        // 6. Hỏi về Card đồ họa / GPU / VGA
        if (q.Contains("card") || q.Contains("vga") || q.Contains("gpu") || (q.Contains("đồ họa") && !q.Contains("học đồ họa") && !q.Contains("làm đồ họa")))
        {
            string? vgaValue = null;
            foreach (var s in specSegments)
            {
                if (Regex.IsMatch(s, @"\b(?:VGA|GPU|Card đồ họa|Card màn hình|Đồ họa)\b", RegexOptions.IgnoreCase))
                {
                    var clean = Regex.Replace(s, @"^(?:VGA|GPU|Card đồ họa|Card màn hình|Đồ họa)\s*[:=-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        vgaValue = clean;
                        break;
                    }
                }
                else if (s.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("Iris Xe", StringComparison.OrdinalIgnoreCase))
                {
                    vgaValue = s;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(vgaValue))
            {
                var answer = $"Dạ mẫu **{name}** được trang bị card đồ họa **{vgaValue}** bạn nhé!";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại trên hệ thống của EMUA chưa có thông số chi tiết về card đồ họa cho mẫu **{name}**. Bạn vui lòng bấm vào nút 'Xem sản phẩm' bên dưới để xem thêm nhé!";
                return (true, answer, productDto);
            }
        }

        // 7. Hỏi về Màn hình
        if (q.Contains("màn hình") || q.Contains("màn") || q.Contains("mấy inch") || q.Contains("bao nhiêu inch") || q.Contains("mấy hz") || q.Contains("tần số quét"))
        {
            string? screenValue = null;
            foreach (var s in specSegments)
            {
                if (Regex.IsMatch(s, @"\b(?:Màn hình|Display|Screen)\b", RegexOptions.IgnoreCase))
                {
                    var clean = Regex.Replace(s, @"^(?:Màn hình|Display|Screen)\s*[:=-]?\s*", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        screenValue = clean;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(screenValue))
            {
                var answer = $"Dạ mẫu **{name}** sở hữu màn hình **{screenValue}** bạn nhé!";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại trên hệ thống của EMUA chưa có thông số chi tiết về màn hình cho mẫu **{name}**. Bạn vui lòng bấm vào nút 'Xem sản phẩm' bên dưới để xem thêm nhé!";
                return (true, answer, productDto);
            }
        }

        // 8. Hỏi về Pin / Trọng lượng
        if (q.Contains("pin") || q.Contains("nặng bao nhiêu") || q.Contains("bao nhiêu kg") || q.Contains("trọng lượng") || q.Contains("nặng không"))
        {
            string? pinOrWeight = null;
            foreach (var s in specSegments)
            {
                if (q.Contains("pin") && Regex.IsMatch(s, @"\b(?:Pin|Battery)\b", RegexOptions.IgnoreCase))
                {
                    pinOrWeight = s;
                    break;
                }
                else if (!q.Contains("pin") && Regex.IsMatch(s, @"\b(?:Trọng lượng|Khối lượng|Weight|kg)\b", RegexOptions.IgnoreCase))
                {
                    pinOrWeight = s;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(pinOrWeight))
            {
                var answer = $"Dạ thông số về {(q.Contains("pin") ? "pin" : "trọng lượng")} của mẫu **{name}** là: **{pinOrWeight}** bạn nhé!";
                return (true, answer, productDto);
            }
            else
            {
                var answer = $"Dạ hiện tại thông số chi tiết về {(q.Contains("pin") ? "dung lượng pin" : "trọng lượng")} của mẫu **{name}** chưa được cập nhật đầy đủ trong hệ thống. Bạn vui lòng bấm vào thẻ sản phẩm bên dưới để xem catalogue chi tiết nhé!";
                return (true, answer, productDto);
            }
        }

        // 9. Hỏi về Bảo hành
        if (q.Contains("bảo hành"))
        {
            var bh = !string.IsNullOrWhiteSpace(p.BaoHanh) ? p.BaoHanh : "12 tháng";
            var answer = $"Dạ mẫu **{name}** được hưởng chính sách bảo hành chính hãng **{bh}** tại EMUA, hỗ trợ 1 đổi 1 trong vòng 7 ngày đầu nếu có lỗi phần cứng từ nhà sản xuất bạn nhé!";
            return (true, answer, productDto);
        }

        // 10. Hỏi cấu hình / thông số tổng quát
        if (q.Contains("cấu hình") || q.Contains("thông số") || q.Contains("chi tiết máy") || q.Contains("thông tin máy"))
        {
            if (specSegments.Any())
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Dạ thông số kỹ thuật chi tiết của mẫu **{name}** như sau:");
                foreach (var s in specSegments.Take(8))
                {
                    sb.AppendLine($"• {s}");
                }
                var minPriceStr = minPrice.HasValue && minPrice.Value > 0 ? minPrice.Value.ToString("N0", culture) + " VNĐ" : "";
                if (!string.IsNullOrWhiteSpace(minPriceStr))
                {
                    sb.AppendLine($"• Giá bán hiện tại: **{minPriceStr}** ({(inStock ? "Còn hàng" : "Tạm hết hàng")})");
                }
                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem catalogue đầy đủ và đặt hàng nhé!");
                return (true, sb.ToString(), productDto);
            }
            else
            {
                var minPriceStr = minPrice.HasValue && minPrice.Value > 0 ? minPrice.Value.ToString("N0", culture) + " VNĐ" : "";
                var answer = $"Dạ mẫu **{name}** hiện đang có giá bán từ **{minPriceStr}** ({(inStock ? "Còn hàng" : "Tạm hết hàng")}). Do hệ thống chưa có bảng thông số kỹ thuật chi tiết của mã này, bạn vui lòng bấm 'Xem sản phẩm' bên dưới để xem catalogue từ nhà sản xuất nhé!";
                return (true, answer, productDto);
            }
        }

        return (false, "", productDto);
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
