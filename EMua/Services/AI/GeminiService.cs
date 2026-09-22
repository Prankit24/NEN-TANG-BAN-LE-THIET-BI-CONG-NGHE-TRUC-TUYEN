using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace EMua.Services.AI
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public GeminiService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> AskAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Câu hỏi không được để trống.");
            }

            var apiKey = _configuration["Gemini:ApiKey"];
            var model = _configuration["Gemini:Model"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình Gemini:ApiKey.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình Gemini:Model.");
            }

            var url =
                "https://generativelanguage.googleapis.com/v1beta/models/" +
                Uri.EscapeDataString(model) +
                ":generateContent";

            var body = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new
                        {
                            text =
                                "Bạn là trợ lý công nghệ của EMUA. " +
                                "Trả lời bằng tiếng Việt, rõ ràng và dễ hiểu. " +
                                "Chỉ hỗ trợ về công nghệ và thiết bị. " +
                                "Bạn chưa được kết nối dữ liệu cửa hàng, " +
                                "không tự bịa giá, tồn kho hoặc đơn hàng."
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
                }
            };

            using var httpRequest =
                new HttpRequestMessage(HttpMethod.Post, url);

            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = JsonContent.Create(body);

            using var response =
                await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var reason = (int)response.StatusCode switch
                {
                    400 => "Yêu cầu hoặc cấu hình không hợp lệ.",
                    401 or 403 => "Kiểm tra API key và quyền truy cập.",
                    404 => "Không tìm thấy model đã cấu hình.",
                    429 => "Đã chạm giới hạn lượt gọi hoặc hạn mức.",
                    _ => "Dịch vụ Gemini đang gặp lỗi."
                };

                throw new HttpRequestException(
                    $"Gemini HTTP {(int)response.StatusCode}: {reason}");
            }

            var json = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "Gemini không trả về câu trả lời.");
            }

            var candidate = candidates[0];

            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
            {
                throw new InvalidOperationException(
                    "Phản hồi Gemini không chứa nội dung văn bản.");
            }

            var answer = new StringBuilder();

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) &&
                    thought.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var text))
                {
                    answer.Append(text.GetString());
                }
            }

            if (string.IsNullOrWhiteSpace(answer.ToString()))
            {
                throw new InvalidOperationException(
                    "Gemini trả về nội dung rỗng.");
            }

            return answer.ToString().Trim();
        }
    }
}