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
    /// 2. Chuyển sang Groq khi Gemini thất bại vì BẤT KỲ lý do phía dịch vụ nào, để khách không bị gián đoạn:
    ///    - Lỗi tạm thời: HTTP 429/500/502/503/504, timeout, mất kết nối
    ///    - Lỗi cấu hình/xác thực Gemini (thiếu key, HTTP 400/401/403): ghi log mức Error để dev sửa,
    ///      nhưng vẫn để Groq phục vụ khách thay vì trả lỗi.
    /// 3. KHÔNG chuyển sang Groq khi:
    ///    - Lỗi dữ liệu đầu vào (ArgumentException) - Groq cũng sẽ lỗi y hệt
    ///    - Client chủ động hủy request
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
            catch (Exception ex) when (ex is not ArgumentException)
            {
                // Lỗi tạm thời → Warning; lỗi cấu hình/xác thực → Error (cần dev xử lý) - cả hai đều chuyển sang Groq
                var transient = IsTransientError(ex);
                _logger.Log(
                    transient ? LogLevel.Warning : LogLevel.Error,
                    ex,
                    "EMUA AI Orchestrator: Gemini gặp lỗi {Kind} ({ExType}: {Message}). " +
                    "Kích hoạt fallback sang Groq.",
                    transient ? "tạm thời" : "cấu hình/xác thực",
                    ex.GetType().Name,
                    SanitizeErrorMessage(ex.Message));
            }
            // ArgumentException (dữ liệu đầu vào sai) KHÔNG bị catch → throw lên cho caller xử lý

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
            if (ex is HttpRequestException httpEx)
            {
                // Không có status code = lỗi mạng/DNS/kết nối bị ngắt → coi là tạm thời
                if (!httpEx.StatusCode.HasValue) return true;

                var code = (int)httpEx.StatusCode.Value;
                return code == 429  // Too Many Requests / Quota Exhausted
                    || code == 500  // Internal Server Error
                    || code == 502  // Bad Gateway
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
