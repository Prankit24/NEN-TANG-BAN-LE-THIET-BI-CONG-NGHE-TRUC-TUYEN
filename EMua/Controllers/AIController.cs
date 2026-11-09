using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EMua.Services.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EMua.Controllers
{
    public class AIController : Controller
    {
        private readonly DomainClassifier _domainClassifier;
        private readonly GeminiService _geminiService;
        private readonly RecommendationService _recommendationService;
        private readonly ILogger<AIController> _logger;

        public AIController(
            DomainClassifier domainClassifier,
            GeminiService geminiService,
            RecommendationService recommendationService,
            ILogger<AIController> logger)
        {
            _domainClassifier = domainClassifier;
            _geminiService = geminiService;
            _recommendationService = recommendationService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Chat(
            [FromBody] ChatRequest? request,
            System.Threading.CancellationToken cancellationToken)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập câu hỏi."
                });
            }

            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            var stage = "Phân loại câu hỏi";

            try
            {
                var classifyTimer = System.Diagnostics.Stopwatch.StartNew();
                var prediction =
                    _domainClassifier.Predict(request.Message);
                classifyTimer.Stop();

                _logger.LogInformation(
                    "EMUA AI: [Bước 1/4] Phân loại câu hỏi mất {ElapsedMs} ms. Kết quả: {IsTechnology}",
                    classifyTimer.ElapsedMilliseconds,
                    prediction.IsTechnology);

                if (!prediction.IsTechnology)
                {
                    totalTimer.Stop();
                    return Json(new
                    {
                        domain = "NonTechnology",
                        message =
                            "Tôi là trợ lý công nghệ của EMUA. " +
                            "Tôi chỉ hỗ trợ các câu hỏi liên quan đến " +
                            "công nghệ, thiết bị và sản phẩm tại EMUA.",
                        isLimitReached = false
                    });
                }

                stage = "Lấy dữ liệu sản phẩm";
                var dbTimer = System.Diagnostics.Stopwatch.StartNew();

                var isShopping = RecommendationService.IsShoppingQuery(request.Message);

                var (productContext, recommendedProducts, lowestExistingPrice) =
                    await _recommendationService
                        .GetProductRecommendationsAsync(request.Message, cancellationToken);
                dbTimer.Stop();

                _logger.LogInformation(
                    "EMUA AI: [Bước 2/4] Ý định mua sắm: {IsShopping}. Truy vấn Supabase & lọc sản phẩm mất {ElapsedMs} ms. Tìm thấy: {Count} sản phẩm.",
                    isShopping,
                    dbTimer.ElapsedMilliseconds,
                    recommendedProducts.Count);

                var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
                var budget = RecommendationService.ExtractBudget(request.Message);

                // Hậu kiểm tra: Chỉ chấp nhận các sản phẩm thỏa mãn điều kiện ngân sách và tồn kho
                var verifiedProducts = isShopping
                    ? recommendedProducts
                        .Where(p => p.InStock && p.MinPrice.HasValue && p.MinPrice.Value > 0)
                        .Where(p => budget == null || (!budget.MaxPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value <= budget.MaxPrice.Value)))
                        .Where(p => budget == null || (!budget.MinPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value >= budget.MinPrice.Value)))
                        .Take(3)
                        .Select(p => new ChatProductItemDto(
                            p.Id,
                            p.Name,
                            (p.MinPrice ?? 0m).ToString("N0", culture) + " VNĐ",
                            p.InStock,
                            p.ImageUrl,
                            p.DetailUrl
                        )).ToList()
                    : new List<ChatProductItemDto>();

                var promptWithContext = string.IsNullOrWhiteSpace(productContext)
                    ? request.Message
                    : "NGỮ CẢNH SẢN PHẨM CỬA HÀNG:\n" +
                      productContext +
                      "\n\nCÂU HỎI KHÁCH HÀNG:\n" +
                      request.Message;

                stage = "Gọi Gemini";
                _logger.LogInformation(
                    "EMUA AI: [Bước 3/4] Gọi Gemini. Độ dài prompt: {Length} ký tự.",
                    promptWithContext.Length);

                var geminiTimer = System.Diagnostics.Stopwatch.StartNew();
                string answer;

                try
                {
                    answer = await _geminiService.AskAsync(promptWithContext, cancellationToken);
                    geminiTimer.Stop();
                    _logger.LogInformation(
                        "EMUA AI: [Bước 3/4] Gemini trả lời thành công sau {ElapsedMs} ms.",
                        geminiTimer.ElapsedMilliseconds);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    geminiTimer.Stop();
                    _logger.LogWarning(
                        ex,
                        "EMUA AI: Gọi Gemini thất bại/hết quota/quá hạn sau {ElapsedMs} ms. Kích hoạt fallback Supabase tự động.",
                        geminiTimer.ElapsedMilliseconds);

                    answer = GenerateFallbackAnswer(request.Message, isShopping, verifiedProducts, budget, lowestExistingPrice);
                }

                totalTimer.Stop();
                _logger.LogInformation(
                    "EMUA AI: [Bước 4/4] Hoàn thành toàn bộ xử lý trong {TotalMs} ms.",
                    totalTimer.ElapsedMilliseconds);

                return Json(new
                {
                    domain = "Technology",
                    message = answer,
                    products = verifiedProducts,
                    isLimitReached = false
                });
            }
            catch (OperationCanceledException)
                when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Trình duyệt đã ngắt yêu cầu.
                return new EmptyResult();
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "EMUA AI: Hết thời gian chờ tại bước {Stage}.",
                    stage);

                return StatusCode(504, new
                {
                    domain = "Technology",
                    message =
                        "Dịch vụ tư vấn phản hồi quá lâu. " +
                        "Bạn vui lòng thử lại sau.",
                    errorCode = "AI_TIMEOUT",
                    isLimitReached = false
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "EMUA AI: Lỗi HTTP tại bước {Stage}. Mã: {StatusCode}.",
                    stage,
                    ex.StatusCode);

                var isRateLimited =
                    ex.StatusCode == HttpStatusCode.TooManyRequests;

                var message = ex.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests =>
                        "Dịch vụ AI đang vượt giới hạn sử dụng. " +
                        "Bạn vui lòng thử lại sau.",

                    HttpStatusCode.ServiceUnavailable =>
                        "Dịch vụ AI tạm thời không khả dụng. " +
                        "Bạn vui lòng thử lại sau.",

                    HttpStatusCode.Unauthorized or
                    HttpStatusCode.Forbidden =>
                        "Dịch vụ AI đang gặp lỗi xác thực hoặc quyền truy cập.",

                    HttpStatusCode.NotFound =>
                        "Model AI đang cấu hình không khả dụng.",

                    HttpStatusCode.BadRequest =>
                        "Dịch vụ AI từ chối yêu cầu. " +
                        "Cần kiểm tra cấu hình và dữ liệu gửi đi.",

                    _ =>
                        "Không thể kết nối ổn định với dịch vụ AI. " +
                        "Bạn vui lòng thử lại sau."
                };

                return StatusCode(isRateLimited ? 503 : 502, new
                {
                    domain = "Technology",
                    message,
                    errorCode = "AI_UPSTREAM_ERROR",
                    isLimitReached = isRateLimited
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EMUA AI: Lỗi tại bước {Stage}.",
                    stage);

                return StatusCode(500, new
                {
                    domain = "Technology",
                    message =
                        "Hệ thống tư vấn đang gặp sự cố. " +
                        "Bạn vui lòng thử lại sau.",
                    errorCode = "AI_INTERNAL_ERROR",
                    isLimitReached = false
                });
            }
        }

        private static string GenerateFallbackAnswer(
            string message,
            bool isShopping,
            System.Collections.Generic.List<ChatProductItemDto> verifiedProducts,
            BudgetInfo? budget,
            decimal? lowestExistingPrice)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

            // 1. Nhu cầu mua sắm và tìm thấy sản phẩm phù hợp trong kho
            if (isShopping && verifiedProducts.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                if (budget != null)
                {
                    sb.AppendLine($"Dạ chào bạn! Với tầm ngân sách {budget.RawDescription}, EMUA xin gợi ý {verifiedProducts.Count} mẫu sản phẩm chính hãng, còn hàng thực tế tại cửa hàng với mức giá tốt nhất:");
                }
                else
                {
                    sb.AppendLine($"Dạ chào bạn! EMUA xin gợi ý {verifiedProducts.Count} mẫu sản phẩm nổi bật, chính hãng và đang sẵn hàng tại cửa hàng phù hợp với nhu cầu của bạn:");
                }

                foreach (var p in verifiedProducts)
                {
                    sb.AppendLine($"• {p.name} — Giá: {p.price} (Còn hàng)");
                }

                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết cấu hình và ưu đãi nhé!");
                return sb.ToString();
            }

            // 2. Nhu cầu mua sắm nhưng KHÔNG có sản phẩm nào thỏa mãn trong kho
            if (isShopping && verifiedProducts.Count == 0)
            {
                if (budget != null)
                {
                    var lowestStr = lowestExistingPrice.HasValue && lowestExistingPrice.Value > 0
                        ? $" Mẫu sản phẩm thuộc nhóm này có giá tốt nhất hiện tại của EMUA khởi điểm từ {lowestExistingPrice.Value.ToString("N0", culture)} VNĐ."
                        : "";

                    return $"Dạ hiện tại hệ thống cửa hàng EMUA chưa có mẫu sản phẩm nào còn hàng trong tầm ngân sách {budget.RawDescription}.{lowestStr} Bạn có muốn điều chỉnh thêm ngân sách hoặc tham khảo chính sách trả góp 0% tại EMUA không ạ?";
                }

                return "Dạ hiện tại EMUA chưa có mẫu sản phẩm nào còn hàng phù hợp chính xác với yêu cầu này trong kho. Bạn có thể cho EMUA biết thêm về nhu cầu sử dụng hoặc tầm ngân sách dự kiến để nhân viên hỗ trợ gợi ý dòng máy phù hợp nhất nhé!";
            }

            // 3. Câu hỏi KHÔNG liên quan đến mua sắm
            var q = (message ?? "").ToLowerInvariant();

            // Địa chỉ, showroom, giờ mở cửa, liên hệ
            if (q.Contains("địa chỉ") || q.Contains("ở đâu") || q.Contains("mở cửa") ||
                q.Contains("liên hệ") || q.Contains("hotline") || q.Contains("số điện thoại") ||
                q.Contains("sđt") || q.Contains("showroom") || q.Contains("chi nhánh"))
            {
                return "Dạ showroom EMUA mở cửa từ 8:00 đến 21:30 tất cả các ngày trong tuần. Bạn có thể ghé trực tiếp showroom để trải nghiệm sản phẩm hoặc liên hệ hotline cửa hàng qua trang Liên hệ để được nhân viên hỗ trợ chu đáo nhất nhé!";
            }

            // Bảo hành, đổi trả, chính sách
            if (q.Contains("bảo hành") || q.Contains("đổi trả") || q.Contains("chính sách") ||
                q.Contains("trả hàng") || q.Contains("hoàn tiền") || q.Contains("lỗi"))
            {
                return "Dạ chính sách tại EMUA hỗ trợ 1 đổi 1 trong vòng 7 ngày đầu nếu có lỗi phần cứng từ nhà sản xuất, bảo hành chính hãng từ 12 đến 24 tháng theo từng dòng sản phẩm. Bạn có thể xem chi tiết tại mục Chính sách bảo hành của website nhé!";
            }

            // Chào hỏi, cảm ơn
            if (q.Contains("chào") || q.Contains("hello") || q.Contains("hi ") ||
                q.Equals("hi") || q.Contains("cảm ơn") || q.Contains("thanks") || q.Contains("bạn là ai"))
            {
                return "Dạ em chào bạn! Em là trợ lý của EMUA. Em có thể hỗ trợ tư vấn các dòng laptop, điện thoại, thiết bị công nghệ chính hãng hoặc giải đáp thông tin mua hàng. Bạn đang quan tâm đến sản phẩm nào tại EMUA ạ?";
            }

            // Câu hỏi công nghệ hoặc thắc mắc khác
            return "Dạ hiện tại hệ thống AI tư vấn đang tạm thời bận hoặc vượt giới hạn xử lý. Để được giải đáp nhanh và chi tiết nhất về thắc mắc này, bạn có thể nhắn tin trực tiếp với nhân viên hỗ trợ EMUA qua trang Liên hệ nhé!";
        }
    }

    public record ChatProductItemDto(
        int id,
        string name,
        string price,
        bool inStock,
        string? image,
        string url
    );

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}