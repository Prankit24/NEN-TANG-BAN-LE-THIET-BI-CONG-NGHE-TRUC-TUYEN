
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMua.Services.AI
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiService> _logger;

        public GeminiService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> AskAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException(
                    "Câu hỏi không được để trống.");

            var apiKey = _configuration["Gemini:ApiKey"]?.Trim();
            var model = _configuration["Gemini:Model"]?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "Chưa cấu hình Gemini:ApiKey.");

            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(
                    "Chưa cấu hình Gemini:Model.");

            if (model.StartsWith(
                "models/", StringComparison.OrdinalIgnoreCase))
            {
                model = model.Substring("models/".Length);
            }

            var url =
                "https://generativelanguage.googleapis.com/" +
                "v1beta/models/" +
                Uri.EscapeDataString(model) +
                ":generateContent";

            var generationConfig =
                new System.Collections.Generic.Dictionary<string, object>
                {
                    ["maxOutputTokens"] = 2048,
                    ["temperature"] = 0.5
                };

            // Gemini 2.5 sử dụng thinkingBudget.
            // Không gửi thinkingLevel cho model 2.5.
            if (model.StartsWith(
                "gemini-2.5-", StringComparison.OrdinalIgnoreCase))
            {
                generationConfig["thinkingConfig"] =
                    new { thinkingBudget = 512 };
            }
            else if (model.StartsWith(
                "gemini-3", StringComparison.OrdinalIgnoreCase))
            {
                generationConfig["thinkingConfig"] =
                    new { thinkingLevel = "low" };
            }

            var body = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new
                        {
                            text =
                                "Bạn là trợ lý tư vấn công nghệ của EMUA. " +
                                "Trả lời bằng tiếng Việt, rõ ràng, ngắn gọn. " +
                                "Chỉ hỗ trợ công nghệ và thiết bị. " +
                                "Chỉ sử dụng giá, tồn kho và thông số " +
                                "được cung cấp trong ngữ cảnh sản phẩm. " +
                                "Không tự bịa sản phẩm, giá hoặc đơn hàng. " +
                                "Nếu thiếu dữ liệu, hãy hỏi thêm nhu cầu."
                        }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = message }
                        }
                    }
                },
                generationConfig
            };

            using var request =
                new HttpRequestMessage(HttpMethod.Post, url);

            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(body);

            _logger.LogInformation(
                "EMUA AI: Đang gọi Gemini model {Model}", model);

            HttpResponseMessage response = null!;
            string json = "";

            // Retry tối đa 3 lần với Exponential Backoff khi gặp 503 ServiceUnavailable hoặc 429 TooManyRequests (do spike tải).
            // Nếu hết quota thực sự (ResourceExhausted / quota exceeded) hoặc có Retry-After vượt mức cho phép, dừng ngay.
            const int maxRetries = 3;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                using var attemptRequest = new HttpRequestMessage(HttpMethod.Post, url);
                attemptRequest.Headers.Add("x-goog-api-key", apiKey);
                attemptRequest.Content = JsonContent.Create(body);

                response?.Dispose();
                response = await _httpClient.SendAsync(attemptRequest);
                json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                var statusCode = (int)response.StatusCode;
                var isTransient = statusCode == 503 || statusCode == 429;

                // Kiểm tra xem 429 có phải do hết hẳn Quota không
                var isExhaustedQuota = false;
                if (statusCode == 429 && !string.IsNullOrWhiteSpace(json))
                {
                    if (json.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                        json.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase))
                    {
                        isExhaustedQuota = true;
                    }
                }

                if (isTransient && !isExhaustedQuota && attempt < maxRetries)
                {
                    // Ưu tiên đọc Retry-After header từ response
                    TimeSpan delay = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt - 1)); // 1s, 2s, 4s
                    if (response.Headers.RetryAfter != null)
                    {
                        if (response.Headers.RetryAfter.Delta.HasValue)
                        {
                            delay = response.Headers.RetryAfter.Delta.Value;
                        }
                        else if (response.Headers.RetryAfter.Date.HasValue)
                        {
                            var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                            if (diff > TimeSpan.Zero) delay = diff;
                        }
                    }

                    // Nếu delay quá dài (> 10s), không retry để tránh treo request của khách
                    if (delay > TimeSpan.FromSeconds(10))
                    {
                        _logger.LogWarning("Gemini yêu cầu Retry-After quá lâu ({Seconds}s). Hủy retry.", delay.TotalSeconds);
                        break;
                    }

                    _logger.LogWarning("Gemini HTTP {StatusCode} (Attempt {Attempt}/{Max}). Chờ {Ms}ms thử lại...", statusCode, attempt, maxRetries, (int)delay.TotalMilliseconds);
                    await Task.Delay(delay);
                    continue;
                }

                break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string apiError = "";

                    try
                    {
                        using var errorDoc = JsonDocument.Parse(json);

                        if (errorDoc.RootElement.TryGetProperty(
                                "error", out var error))
                        {
                            var code = error.TryGetProperty(
                                "status", out var status)
                                ? status.GetString()
                                : null;

                            var detail = error.TryGetProperty(
                                "message", out var detailElement)
                                ? detailElement.GetString()
                                : null;

                            apiError = $"{code}: {detail}";
                        }
                    }
                    catch (JsonException)
                    {
                        apiError = "Không đọc được chi tiết lỗi API.";
                    }

                    _logger.LogError(
                        "Gemini HTTP {StatusCode}, model {Model}: {Error}",
                        (int)response.StatusCode,
                        model,
                        apiError);

                    throw new HttpRequestException(
                        $"Gemini HTTP {(int)response.StatusCode}: {apiError}",
                        null,
                        response.StatusCode);
                }
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "Gemini không trả về candidates. " +
                    "Kiểm tra promptFeedback và log API.");
            }

            var candidate = candidates[0];

            if (candidate.TryGetProperty(
                "finishReason", out var finishReason))
            {
                var reason = finishReason.GetString();

                if (reason == "SAFETY")
                    throw new InvalidOperationException(
                        "Gemini từ chối câu hỏi vì giới hạn an toàn.");

                if (reason == "MAX_TOKENS")
                    _logger.LogWarning(
                        "Gemini đã đạt giới hạn output tokens.");
            }

            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Gemini không trả về nội dung văn bản.");
            }

            var answer = new StringBuilder();

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) &&
                    thought.ValueKind == JsonValueKind.True)
                    continue;

                if (part.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    answer.Append(text.GetString());
                }
            }

            var result = answer.ToString().Trim();

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException(
                    "Gemini trả về nội dung rỗng.");

            _logger.LogInformation(
                "EMUA AI: Gemini phản hồi thành công.");

            return result;
        }
    }
}
