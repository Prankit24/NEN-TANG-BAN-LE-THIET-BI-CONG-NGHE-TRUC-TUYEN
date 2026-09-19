using EMua.Services.AI;
using Microsoft.AspNetCore.Mvc;

namespace EMua.Controllers
{
    public class AIController : Controller
    {
        private readonly DomainClassifier _domainClassifier;
        private readonly GeminiService _geminiService;

        public AIController(
            DomainClassifier domainClassifier,
            GeminiService geminiService)
        {
            _domainClassifier = domainClassifier;
            _geminiService = geminiService;
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
                var answer =
                    await _geminiService.AskAsync(request.Message);

                return Json(new
                {
                    domain = "Technology",
                    message = answer,
                    isLimitReached = false
                });
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode ==
                    System.Net.HttpStatusCode.TooManyRequests)
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
            catch (HttpRequestException ex)
     when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
           ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
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