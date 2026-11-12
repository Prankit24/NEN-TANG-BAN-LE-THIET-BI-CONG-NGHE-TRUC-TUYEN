namespace EMua.Services.AI
{
    /// <summary>
    /// Interface chung cho các dịch vụ AI (Gemini, Groq, v.v.).
    /// Mọi provider AI đều phải implement interface này
    /// để đảm bảo cùng contract và có thể hoán đổi khi cần.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Tên provider hiển thị trong log (ví dụ: "Gemini", "Groq").
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Gửi câu hỏi tới AI và nhận phản hồi văn bản.
        /// </summary>
        /// <param name="message">Nội dung câu hỏi (đã bao gồm ngữ cảnh sản phẩm nếu có).</param>
        /// <param name="cancellationToken">Token hủy request.</param>
        /// <returns>Phản hồi văn bản từ AI.</returns>
        Task<string> AskAsync(string message, System.Threading.CancellationToken cancellationToken = default);
    }
}
