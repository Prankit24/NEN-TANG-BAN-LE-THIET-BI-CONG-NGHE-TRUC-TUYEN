
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

        public async Task<string> AskAsync(string message, System.Threading.CancellationToken cancellationToken = default)
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
                    ["maxOutputTokens"] = 800,
                    ["temperature"] = 0.3
                };

            // Tắt suy nghĩ kéo dài (thinking) không cần thiết để phản hồi tức thì
            if (model.StartsWith(
                "gemini-2.5-", StringComparison.OrdinalIgnoreCase))
            {
                generationConfig["thinkingConfig"] =
                    new { thinkingBudget = 0 };
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
                                "Bạn là trợ lý tư vấn công nghệ thông minh, thân thiện của cửa hàng EMUA. " +
                                "Nhiệm vụ của bạn là tư vấn mua hàng tự nhiên, đúng trọng tâm và bám sát nhu cầu, ngân sách của khách. " +
                                "QUY TẮC BẮT BUỘC:\n" +
                                "1. Chỉ đề xuất TỐI ĐA 3 SẢN PHẨM trong mỗi câu trả lời.\n" +
                                "2. Mỗi sản phẩm đề xuất phải nêu rõ: Tên sản phẩm, Giá bán thực tế và 1-2 Lý do lựa chọn phù hợp nhất với nhu cầu khách.\n" +
                                "3. TUYỆT ĐỐI chỉ sử dụng tên sản phẩm, giá bán, cấu hình và tình trạng tồn kho có trong mục [DỮ LIỆU SẢN PHẨM TỪ CỬA HÀNG].\n" +
                                "4. Không tự ý bịa thêm sản phẩm, không bịa giá hoặc tồn kho.\n" +
                                "5. Nếu khách chưa nói rõ ngân sách hoặc nhu cầu cụ thể, hãy gợi ý nhanh 1-2 mẫu tiêu biểu có sẵn và lịch sự hỏi thêm để tư vấn chính xác hơn.\n" +
                                "6. Trả lời bằng tiếng Việt văn minh, ngắn gọn, súc tích, định dạng bullet point rõ ràng, dễ đọc."
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

            _logger.LogInformation(
                "EMUA AI: Đang gọi Gemini model {Model}", model);

            HttpResponseMessage response = null!;
            string json = "";

            // Retry tối đa 2 lần (1 attempt + 1 retry) CHỈ khi gặp lỗi tạm thời (429, 503, 504)
            // Không retry lỗi 400, 401, 403, 404
            const int maxRetries = 2;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                var attemptTimer = System.Diagnostics.Stopwatch.StartNew();
                using var attemptRequest = new HttpRequestMessage(HttpMethod.Post, url);
                attemptRequest.Headers.Add("x-goog-api-key", apiKey);
                attemptRequest.Content = JsonContent.Create(body);

                response?.Dispose();
                try
                {
                    response = await _httpClient.SendAsync(attemptRequest, cancellationToken);
                    json = await response.Content.ReadAsStringAsync(cancellationToken);
                    attemptTimer.Stop();

                    _logger.LogInformation(
                        "EMUA AI: Gemini Attempt {Attempt}/{MaxRetries} hoàn thành sau {ElapsedMs} ms. HTTP: {StatusCode}",
                        attempt,
                        maxRetries,
                        attemptTimer.ElapsedMilliseconds,
                        (int)response.StatusCode);
                }
                catch (Exception ex)
                {
                    attemptTimer.Stop();
                    _logger.LogWarning(
                        ex,
                        "EMUA AI: Gemini Attempt {Attempt}/{MaxRetries} thất bại sau {ElapsedMs} ms",
                        attempt,
                        maxRetries,
                        attemptTimer.ElapsedMilliseconds);
                    throw;
                }

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                var statusCode = (int)response.StatusCode;
                var isTransient = statusCode == 503 || statusCode == 429 || statusCode == 504;

                // Kiểm tra xem 429 có phải do cạn hẳn Quota không
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
                    // Đọc Retry-After header nếu có, mặc định 1.5s
                    var delay = TimeSpan.FromMilliseconds(1500);
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

                    // Không retry nếu delay > 3s để tránh kéo dài request của khách
                    if (delay > TimeSpan.FromSeconds(3))
                    {
                        _logger.LogWarning("Gemini yêu cầu Retry-After quá lâu ({Seconds}s). Hủy retry.", delay.TotalSeconds);
                        break;
                    }

                    _logger.LogWarning("Gemini HTTP {StatusCode} (Attempt {Attempt}/{Max}). Chờ {Ms}ms thử lại...", statusCode, attempt, maxRetries, (int)delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
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
