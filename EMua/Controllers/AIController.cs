using EMua.Services.AI;
using Microsoft.AspNetCore.Mvc;

namespace EMua.Controllers
{
    public class AIController : Controller
    {
        private readonly DomainClassifier _domainClassifier;
        private readonly GeminiService _geminiService;
        private readonly RecommendationService _recommendationService;

        public AIController(
            DomainClassifier domainClassifier,
            GeminiService geminiService,
            RecommendationService recommendationService)
        {
            _domainClassifier = domainClassifier;
            _geminiService = geminiService;
            _recommendationService = recommendationService;
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

            // 1. Kiểm tra domain xem có phải câu hỏi công nghệ hay không
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
                        "công nghệ, thiết bị và sản phẩm tại EMUA."
                });
            }

            try
            {
                // 2. Lấy dữ liệu sản phẩm thực tế từ Database để làm ngữ cảnh tư vấn
                var productContext = await _recommendationService.GetProductContextAsync(request.Message);

                // 3. Ghép ngữ cảnh cửa hàng vào câu hỏi của khách để Gemini tổng hợp câu trả lời chính xác
                var promptWithContext = $"{productContext}\n\nYêu cầu của khách hàng: {request.Message}\nHãy tư vấn sản phẩm phù hợp dựa vào danh sách sản phẩm thực tế của cửa hàng ở trên.";

                var answer =
                    await _geminiService.AskAsync(promptWithContext);

                return Json(new
                {
                    domain = "Technology",
                    message = answer,
                    isLimitReached = false
                });
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode ==
                    System.Net.HttpStatusCode.TooManyRequests ||
                      ex.StatusCode ==
                    System.Net.HttpStatusCode.ServiceUnavailable)
            {
                return Json(new
                {
                    domain = "Technology",
                    message =
                        "EMUA AI đang bận. Bạn vui lòng chờ ít phút, " +
                        "chuyên viên tư vấn sẽ hỗ trợ bạn sớm.",
                    isLimitReached = true
                });
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}