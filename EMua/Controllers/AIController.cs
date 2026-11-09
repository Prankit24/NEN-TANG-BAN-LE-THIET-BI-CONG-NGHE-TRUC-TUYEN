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
            [FromBody] ChatRequest? request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập câu hỏi."
                });
            }

            var stage = "Phân loại câu hỏi";

            try
            {
                var prediction =
                    _domainClassifier.Predict(request.Message);

                if (!prediction.IsTechnology)
                {
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
                _logger.LogInformation("EMUA AI: {Stage}", stage);

                var (productContext, recommendedProducts) =
                    await _recommendationService
                        .GetProductRecommendationsAsync(request.Message);

                var promptWithContext =
                    "NGỮ CẢNH SẢN PHẨM CỬA HÀNG:\n" +
                    productContext +
                    "\n\nCÂU HỎI KHÁCH HÀNG:\n" +
                    request.Message;

                stage = "Gọi Gemini";

                // Chỉ ghi độ dài, không ghi API key hoặc nội dung khách.
                _logger.LogInformation(
                    "EMUA AI: {Stage}. Độ dài prompt: {Length} ký tự.",
                    stage,
                    promptWithContext.Length);

                var geminiTimer = System.Diagnostics.Stopwatch.StartNew();

                var answer =
                    await _geminiService.AskAsync(promptWithContext);

                geminiTimer.Stop();

                _logger.LogInformation(
                    "EMUA AI: Gemini trả lời sau {ElapsedMs} ms.",
                    geminiTimer.ElapsedMilliseconds);

                _logger.LogInformation(
                    "EMUA AI: Đã nhận câu trả lời từ Gemini.");

                var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
                var budget = RecommendationService.ExtractBudget(request.Message);

                // Hậu kiểm tra: Chỉ chấp nhận các sản phẩm thỏa mãn điều kiện ngân sách và tồn kho
                var verifiedProducts = recommendedProducts
                    .Where(p => p.InStock && p.MinPrice.HasValue && p.MinPrice.Value > 0)
                    .Where(p => budget == null || (!budget.MaxPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value <= budget.MaxPrice.Value)))
                    .Where(p => budget == null || (!budget.MinPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value >= budget.MinPrice.Value)))
                    .Take(3)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = (p.MinPrice ?? 0m).ToString("N0", culture) + " VNĐ",
                        inStock = p.InStock,
                        image = p.ImageUrl,
                        url = p.DetailUrl
                    }).ToList();

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
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}