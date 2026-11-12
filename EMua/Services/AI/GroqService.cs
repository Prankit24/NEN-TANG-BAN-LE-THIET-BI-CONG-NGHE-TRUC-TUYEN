
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMua.Services.AI
{
    /// <summary>
    /// Dịch vụ gọi Groq API (OpenAI-compatible endpoint).
    /// Sử dụng làm provider dự phòng khi Gemini gặp lỗi tạm thời.
    ///
    /// Groq API endpoint: https://api.groq.com/openai/v1/chat/completions
    /// API Key đọc từ IConfiguration["Groq:ApiKey"] (User Secrets).
    /// Model đọc từ IConfiguration["Groq:Model"] (appsettings.json).
    /// </summary>
    public class GroqService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GroqService> _logger;

        private const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";

        public string ProviderName => "Groq";

        public GroqService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GroqService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> AskAsync(string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Câu hỏi không được để trống.");

            var apiKey = _configuration["Groq:ApiKey"]?.Trim();
            var model = _configuration["Groq:Model"]?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Chưa cấu hình Groq:ApiKey.");

            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("Chưa cấu hình Groq:Model.");

            // System prompt giống hệt Gemini để đảm bảo cùng quy tắc nghiệp vụ
            var systemPrompt =
                "Bạn là trợ lý tư vấn công nghệ thông minh, thân thiện của cửa hàng EMUA. " +
                "Nhiệm vụ của bạn là tư vấn mua hàng tự nhiên, đúng trọng tâm và bám sát nhu cầu, ngân sách của khách. " +
                "QUY TẮC BẮT BUỘC:\n" +
                "1. Chỉ đề xuất TỐI ĐA 3 SẢN PHẨM trong mỗi câu trả lời.\n" +
                "2. Mỗi sản phẩm đề xuất phải nêu rõ: Tên sản phẩm, Giá bán thực tế và 1-2 Lý do lựa chọn phù hợp nhất với nhu cầu khách.\n" +
                "3. TUYỆT ĐỐI chỉ sử dụng tên sản phẩm, giá bán, cấu hình và tình trạng tồn kho có trong mục [DỮ LIỆU SẢN PHẨM TỪ CỬA HÀNG].\n" +
                "4. Không tự ý bịa thêm sản phẩm, không bịa giá hoặc tồn kho.\n" +
                "5. Nếu khách chưa nói rõ ngân sách hoặc nhu cầu cụ thể, hãy gợi ý nhanh 1-2 mẫu tiêu biểu có sẵn và lịch sự hỏi thêm để tư vấn chính xác hơn.\n" +
                "6. Trả lời bằng tiếng Việt văn minh, ngắn gọn, súc tích, định dạng bullet point rõ ràng, dễ đọc.";

            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = message }
                },
                max_tokens = 800,
                temperature = 0.3
            };

            _logger.LogInformation(
                "EMUA AI: Đang gọi Groq model {Model}", model);

            // Retry tối đa 2 lần (1 attempt + 1 retry) CHỈ khi gặp lỗi tạm thời (429, 503, 504)
            const int maxRetries = 2;
            HttpResponseMessage response = null!;
            string json = "";

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                var attemptTimer = System.Diagnostics.Stopwatch.StartNew();

                using var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = JsonContent.Create(requestBody);

                response?.Dispose();
                try
                {
                    response = await _httpClient.SendAsync(request, cancellationToken);
                    json = await response.Content.ReadAsStringAsync(cancellationToken);
                    attemptTimer.Stop();

                    _logger.LogInformation(
                        "EMUA AI: Groq Attempt {Attempt}/{MaxRetries} hoàn thành sau {ElapsedMs} ms. HTTP: {StatusCode}",
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
                        "EMUA AI: Groq Attempt {Attempt}/{MaxRetries} thất bại sau {ElapsedMs} ms",
                        attempt,
                        maxRetries,
                        attemptTimer.ElapsedMilliseconds);
                    throw;
                }

                if (response.IsSuccessStatusCode)
                    break;

                var statusCode = (int)response.StatusCode;
                var isTransient = statusCode == 429 || statusCode == 503 || statusCode == 504;

                if (isTransient && attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(1500);
                    if (response.Headers.RetryAfter?.Delta is { } retryDelta)
                    {
                        delay = retryDelta;
                    }

                    // Không retry nếu delay > 3s
                    if (delay > TimeSpan.FromSeconds(3))
                    {
                        _logger.LogWarning("Groq yêu cầu Retry-After quá lâu ({Seconds}s). Hủy retry.", delay.TotalSeconds);
                        break;
                    }

                    _logger.LogWarning(
                        "Groq HTTP {StatusCode} (Attempt {Attempt}/{Max}). Chờ {Ms}ms thử lại...",
                        statusCode, attempt, maxRetries, (int)delay.TotalMilliseconds);
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
                        if (errorDoc.RootElement.TryGetProperty("error", out var error))
                        {
                            var errorMessage = error.TryGetProperty("message", out var msg)
                                ? msg.GetString()
                                : null;
                            var errorType = error.TryGetProperty("type", out var tp)
                                ? tp.GetString()
                                : null;

                            apiError = $"{errorType}: {errorMessage}";
                        }
                    }
                    catch (JsonException)
                    {
                        apiError = "Không đọc được chi tiết lỗi Groq API.";
                    }

                    _logger.LogError(
                        "Groq HTTP {StatusCode}, model {Model}: {Error}",
                        (int)response.StatusCode,
                        model,
                        apiError);

                    throw new HttpRequestException(
                        $"Groq HTTP {(int)response.StatusCode}: {apiError}",
                        null,
                        response.StatusCode);
                }
            }

            // Parse phản hồi theo chuẩn OpenAI Chat Completions
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "Groq không trả về choices. Kiểm tra log API.");
            }

            var firstChoice = choices[0];

            if (!firstChoice.TryGetProperty("message", out var choiceMessage) ||
                !choiceMessage.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "Groq không trả về nội dung văn bản.");
            }

            var result = (content.GetString() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException(
                    "Groq trả về nội dung rỗng.");

            // Log finish_reason nếu cần
            if (firstChoice.TryGetProperty("finish_reason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason == "length")
                    _logger.LogWarning("Groq đã đạt giới hạn output tokens.");
            }

            _logger.LogInformation("EMUA AI: Groq phản hồi thành công.");

            return result;
        }
    }
}
