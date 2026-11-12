using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EMua.Services.AI
{
    /// <summary>
    /// Cơ chế lựa chọn AI provider thông minh:
    ///
    /// 1. Ưu tiên Gemini (provider chính).
    /// 2. Chỉ chuyển sang Groq khi Gemini gặp LỖI TẠM THỜI:
    ///    - HTTP 429 (Too Many Requests / Quota Exhausted)
    ///    - HTTP 503 (Service Unavailable)
    ///    - Timeout / OperationCanceledException (không do client cancel)
    ///    - TaskCanceledException do timeout
    /// 3. KHÔNG chuyển sang Groq khi:
    ///    - Lỗi dữ liệu đầu vào (ArgumentException)
    ///    - Lỗi cấu hình (InvalidOperationException)
    ///    - Lỗi xác thực API (HTTP 401, 403)
    ///    - Lỗi nội dung (HTTP 400)
    /// 4. Giới hạn: Chỉ thử Groq tối đa 1 lần.
    ///    Nếu Groq cũng thất bại → throw để AIController xử lý fallback Supabase.
    /// </summary>
    public class AIServiceOrchestrator
    {
        private readonly GeminiService _geminiService;
        private readonly GroqService _groqService;
        private readonly ILogger<AIServiceOrchestrator> _logger;

        /// <summary>
        /// Tên provider đã thực sự phục vụ câu trả lời gần nhất.
        /// Dùng cho log và debug.
        /// </summary>
        public string LastUsedProvider { get; private set; } = "None";

        public AIServiceOrchestrator(
            GeminiService geminiService,
            GroqService groqService,
            ILogger<AIServiceOrchestrator> logger)
        {
            _geminiService = geminiService;
            _groqService = groqService;
            _logger = logger;
        }

        /// <summary>
        /// Gửi câu hỏi tới AI với cơ chế fallback:
        /// Gemini → Groq (khi lỗi tạm thời).
        /// </summary>
        public async Task<string> AskAsync(string message, CancellationToken cancellationToken = default)
        {
            var totalTimer = Stopwatch.StartNew();

            // === Bước 1: Thử Gemini (provider chính) ===
            try
            {
                var geminiTimer = Stopwatch.StartNew();
                var result = await _geminiService.AskAsync(message, cancellationToken);
                geminiTimer.Stop();

                LastUsedProvider = _geminiService.ProviderName;

                _logger.LogInformation(
                    "EMUA AI Orchestrator: Provider={Provider} phản hồi thành công sau {ElapsedMs} ms.",
                    LastUsedProvider,
                    geminiTimer.ElapsedMilliseconds);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client thực sự hủy request → không fallback
                throw;
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                // Lỗi tạm thời → chuyển sang Groq
                _logger.LogWarning(
                    ex,
                    "EMUA AI Orchestrator: Gemini gặp lỗi tạm thời ({ExType}: {Message}). " +
                    "Kích hoạt fallback sang Groq.",
                    ex.GetType().Name,
                    SanitizeErrorMessage(ex.Message));
            }
            // Các exception khác (ArgumentException, InvalidOperationException, HTTP 400/401/403)
            // sẽ KHÔNG bị catch ở đây → throw lên cho caller xử lý

            // === Bước 2: Thử Groq (provider dự phòng) ===
            try
            {
                var groqTimer = Stopwatch.StartNew();
                var result = await _groqService.AskAsync(message, cancellationToken);
                groqTimer.Stop();

                LastUsedProvider = _groqService.ProviderName;
                totalTimer.Stop();

                _logger.LogInformation(
                    "EMUA AI Orchestrator: Fallback Provider={Provider} phản hồi thành công sau {ElapsedMs} ms. " +
                    "Tổng thời gian (bao gồm Gemini thất bại): {TotalMs} ms.",
                    LastUsedProvider,
                    groqTimer.ElapsedMilliseconds,
                    totalTimer.ElapsedMilliseconds);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception groqEx)
            {
                totalTimer.Stop();

                _logger.LogError(
                    groqEx,
                    "EMUA AI Orchestrator: Cả Gemini và Groq đều thất bại. " +
                    "Groq lỗi ({ExType}: {Message}). Tổng thời gian: {TotalMs} ms.",
                    groqEx.GetType().Name,
                    SanitizeErrorMessage(groqEx.Message),
                    totalTimer.ElapsedMilliseconds);

                // Throw HttpRequestException hoặc TimeoutException để AIController
                // nhận diện và kích hoạt fallback Supabase
                throw new HttpRequestException(
                    "Cả Gemini và Groq đều không thể phản hồi. " +
                    "Hệ thống sẽ sử dụng câu trả lời dự phòng từ cơ sở dữ liệu.",
                    groqEx,
                    HttpStatusCode.ServiceUnavailable);
            }
        }

        /// <summary>
        /// Xác định xem exception có phải lỗi tạm thời
        /// (nên fallback sang provider khác) hay không.
        /// </summary>
        private static bool IsTransientError(Exception ex)
        {
            // Timeout từ HttpClient
            if (ex is TaskCanceledException || ex is TimeoutException)
                return true;

            // OperationCanceledException do timeout (không phải client cancel)
            if (ex is OperationCanceledException)
                return true;

            // HttpRequestException với status code tạm thời
            if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                return code == 429  // Too Many Requests / Quota Exhausted
                    || code == 503  // Service Unavailable
                    || code == 504; // Gateway Timeout
            }

            return false;
        }

        /// <summary>
        /// Loại bỏ thông tin nhạy cảm (API key) khỏi thông báo lỗi trước khi log.
        /// </summary>
        private static string SanitizeErrorMessage(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return "";

            // Loại bỏ các chuỗi có thể chứa API key
            // (thường xuất hiện trong URL hoặc header dump)
            return System.Text.RegularExpressions.Regex.Replace(
                message,
                @"(key|apikey|api_key|authorization|bearer)\s*[=:]\s*\S+",
                "$1=***REDACTED***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
