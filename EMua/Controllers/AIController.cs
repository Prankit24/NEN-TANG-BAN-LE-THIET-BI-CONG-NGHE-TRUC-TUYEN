using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EMua.Models.AI;
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
            [FromBody] ChatRequest? request,
            System.Threading.CancellationToken cancellationToken)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập câu hỏi."
                });
            }

            var totalTimer = System.Diagnostics.Stopwatch.StartNew();

            // 0. Đọc ngữ cảnh phiên chat hiện tại từ Session
            const string sessionKey = "EMUA_CHAT_SESSION_CONTEXT";
            ChatConversationState? chatState = null;
            try
            {
                var sessionData = HttpContext.Session.GetString(sessionKey);
                if (!string.IsNullOrWhiteSpace(sessionData))
                {
                    chatState = System.Text.Json.JsonSerializer.Deserialize<ChatConversationState>(sessionData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EMUA AI: Không thể đọc ngữ cảnh phiên chat từ Session.");
            }
            chatState ??= new ChatConversationState();

            // Kiểm tra câu hỏi tiếp nối (Ví dụ: "dùng để lập trình liệu có ổn không", "con này pin thế nào")
            var isFollowUp = RecommendationService.IsFollowUpQuery(request.Message) && chatState.LastProducts.Any();

            // 1. Phân loại câu hỏi
            DomainPrediction prediction;
            try
            {
                var classifyTimer = System.Diagnostics.Stopwatch.StartNew();
                prediction = _domainClassifier.Predict(request.Message);
                classifyTimer.Stop();

                // Nếu là câu hỏi tiếp nối trong phiên chat công nghệ hoặc tương tác thông thường, bỏ qua từ chối nhầm
                var isCommonInteraction = IsCommonStoreInteraction(request.Message);
                if ((isFollowUp || isCommonInteraction) && !prediction.IsTechnology)
                {
                    prediction = new DomainPrediction { IsTechnology = true };
                }

                _logger.LogInformation(
                    "EMUA AI: [Bước 1/4] Phân loại câu hỏi mất {ElapsedMs} ms. Kết quả: {IsTechnology}. Tiếp nối: {IsFollowUp}",
                    classifyTimer.ElapsedMilliseconds,
                    prediction.IsTechnology,
                    isFollowUp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EMUA AI: Lỗi phân loại câu hỏi.");
                return StatusCode(500, new
                {
                    domain = "Technology",
                    message = "Hệ thống phân loại đang gặp sự cố. Bạn vui lòng thử lại sau.",
                    errorCode = "CLASSIFICATION_ERROR",
                    isLimitReached = false
                });
            }

            if (!prediction.IsTechnology)
            {
                totalTimer.Stop();
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

            // 2. Truy vấn Supabase
            var isShopping = isFollowUp || RecommendationService.IsShoppingQuery(request.Message);
            string productContext;
            List<ProductRecommendationDto> recommendedProducts;
            decimal? lowestExistingPrice;

            try
            {
                var dbTimer = System.Diagnostics.Stopwatch.StartNew();
                (productContext, recommendedProducts, lowestExistingPrice) =
                    await _recommendationService
                        .GetProductRecommendationsAsync(request.Message, cancellationToken);
                dbTimer.Stop();

                // Nếu là câu hỏi tiếp nối và truy vấn từ khóa mới không có sản phẩm, dùng ngữ cảnh sản phẩm từ phiên trước
                if (isFollowUp && recommendedProducts.Count == 0 && chatState.LastProducts.Any())
                {
                    recommendedProducts = chatState.LastProducts;
                    lowestExistingPrice ??= chatState.LastLowestPrice;

                    var sbContext = new System.Text.StringBuilder();
                    sbContext.AppendLine("[SẢN PHẨM KHÁCH HÀNG ĐANG QUAN TÂM TRONG PHIÊN CHAT HIỆN TẠI]:");
                    foreach (var p in chatState.LastProducts.Take(3))
                    {
                        sbContext.AppendLine($"• Tên: {p.Name} | Giá từ: {(p.MinPrice ?? 0m):N0} VNĐ | Thông số kỹ thuật: {p.Specs} | Mô tả: {p.Description}");
                    }
                    productContext = sbContext.ToString();
                }

                _logger.LogInformation(
                    "EMUA AI: [Bước 2/4] Ý định mua sắm: {IsShopping}. Tiếp nối: {IsFollowUp}. Truy vấn Supabase mất {ElapsedMs} ms. Số sản phẩm: {Count}.",
                    isShopping,
                    isFollowUp,
                    dbTimer.ElapsedMilliseconds,
                    recommendedProducts.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "EMUA AI: Lỗi truy vấn cơ sở dữ liệu Supabase.");
                return StatusCode(500, new
                {
                    domain = "Technology",
                    message = "Không thể kết nối đến cơ sở dữ liệu sản phẩm. Bạn vui lòng thử lại sau.",
                    errorCode = "DB_QUERY_ERROR",
                    isLimitReached = false
                });
            }

            var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            var budget = RecommendationService.ExtractBudget(request.Message) ?? (isFollowUp ? chatState.LastBudget : null);

            // Hậu kiểm tra: Lọc sản phẩm thỏa mãn điều kiện ngân sách và tồn kho
            var verifiedProducts = isShopping
                ? recommendedProducts
                    .Where(p => p.InStock && p.MinPrice.HasValue && p.MinPrice.Value > 0)
                    .Where(p => budget == null || (!budget.MaxPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value <= budget.MaxPrice.Value)))
                    .Where(p => budget == null || (!budget.MinPrice.HasValue || (p.MinPrice.HasValue && p.MinPrice.Value >= budget.MinPrice.Value)))
                    .Take(isFollowUp ? 1 : 3)
                    .Select(p => new ChatProductItemDto(
                        p.Id,
                        p.Name,
                        (p.MinPrice ?? 0m).ToString("N0", culture) + " VNĐ",
                        p.InStock,
                        p.ImageUrl,
                        p.DetailUrl
                    )).ToList()
                : new List<ChatProductItemDto>();

            var promptWithContext = string.IsNullOrWhiteSpace(productContext)
                ? request.Message
                : "NGỮ CẢNH SẢN PHẨM CỬA HÀNG:\n" +
                  productContext +
                  "\n\nCÂU HỎI KHÁCH HÀNG:\n" +
                  request.Message;

            // 3. Gọi Gemini với fallback thông minh hỗ trợ câu hỏi tiếp nối
            _logger.LogInformation(
                "EMUA AI: [Bước 3/4] Gọi Gemini. Độ dài prompt: {Length} ký tự.",
                promptWithContext.Length);

            var geminiTimer = System.Diagnostics.Stopwatch.StartNew();
            string answer;

            try
            {
                answer = await _geminiService.AskAsync(promptWithContext, cancellationToken);
                geminiTimer.Stop();
                _logger.LogInformation(
                    "EMUA AI: [Bước 3/4] Gemini trả lời thành công sau {ElapsedMs} ms.",
                    geminiTimer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TimeoutException || ex is OperationCanceledException)
            {
                geminiTimer.Stop();
                _logger.LogWarning(
                    ex,
                    "EMUA AI: Gọi Gemini thất bại/hết quota/timeout ({ExType}) sau {ElapsedMs} ms. Kích hoạt fallback Supabase.",
                    ex.GetType().Name,
                    geminiTimer.ElapsedMilliseconds);

                answer = GenerateFallbackAnswer(request.Message, isShopping, isFollowUp, verifiedProducts, recommendedProducts, budget, lowestExistingPrice);
            }

            // Cập nhật lại Session ngữ cảnh nếu tìm thấy sản phẩm mới
            if (recommendedProducts.Any() && !isFollowUp)
            {
                chatState.LastProducts = recommendedProducts;
                chatState.LastBudget = budget;
                chatState.LastLowestPrice = lowestExistingPrice;
                chatState.LastQuery = request.Message;
                chatState.LastUpdatedAt = DateTimeOffset.UtcNow;
                try
                {
                    HttpContext.Session.SetString(sessionKey, System.Text.Json.JsonSerializer.Serialize(chatState));
                }
                catch { }
            }

            totalTimer.Stop();
            _logger.LogInformation(
                "EMUA AI: [Bước 4/4] Hoàn thành toàn bộ xử lý trong {TotalMs} ms.",
                totalTimer.ElapsedMilliseconds);

            return Json(new
            {
                domain = "Technology",
                message = answer,
                products = verifiedProducts,
                isLimitReached = false
            });
        }

        private static string GenerateFallbackAnswer(
            string message,
            bool isShopping,
            bool isFollowUp,
            System.Collections.Generic.List<ChatProductItemDto> verifiedProducts,
            System.Collections.Generic.List<ProductRecommendationDto> recommendedProducts,
            BudgetInfo? budget,
            decimal? lowestExistingPrice)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

            // 0. Trường hợp câu hỏi tiếp nối về sản phẩm đã đề xuất trong phiên chat
            if (isFollowUp && verifiedProducts.Count > 0)
            {
                var targetProduct = verifiedProducts.First();
                var targetDto = recommendedProducts.FirstOrDefault(p => p.Id == targetProduct.id);
                return GenerateFollowUpAnswer(message, targetProduct, targetDto);
            }

            // 1. Nhu cầu mua sắm và tìm thấy sản phẩm phù hợp trong kho
            if (isShopping && verifiedProducts.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                if (budget != null)
                {
                    sb.AppendLine($"Dạ chào bạn! Với tầm ngân sách {budget.RawDescription}, EMUA xin gợi ý {verifiedProducts.Count} mẫu sản phẩm chính hãng, còn hàng thực tế tại cửa hàng với mức giá tốt nhất:");
                }
                else
                {
                    sb.AppendLine($"Dạ chào bạn! EMUA xin gợi ý {verifiedProducts.Count} mẫu sản phẩm nổi bật, chính hãng và đang sẵn hàng tại cửa hàng phù hợp với nhu cầu của bạn:");
                }

                foreach (var p in verifiedProducts)
                {
                    sb.AppendLine($"• {p.name} — Giá: {p.price} (Còn hàng)");
                }

                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết cấu hình và ưu đãi nhé!");
                return sb.ToString();
            }

            // 2. Nhu cầu mua sắm nhưng KHÔNG có sản phẩm nào thỏa mãn trong kho
            if (isShopping && verifiedProducts.Count == 0)
            {
                if (budget != null)
                {
                    var lowestStr = lowestExistingPrice.HasValue && lowestExistingPrice.Value > 0
                        ? $" Mẫu sản phẩm thuộc nhóm này có giá tốt nhất hiện tại của EMUA khởi điểm từ {lowestExistingPrice.Value.ToString("N0", culture)} VNĐ."
                        : "";

                    return $"Dạ hiện tại hệ thống cửa hàng EMUA chưa có mẫu sản phẩm nào còn hàng trong tầm ngân sách {budget.RawDescription}.{lowestStr} Bạn có muốn điều chỉnh thêm ngân sách hoặc tham khảo chính sách trả góp 0% tại EMUA không ạ?";
                }

                return "Dạ hiện tại EMUA chưa có mẫu sản phẩm nào còn hàng phù hợp chính xác với yêu cầu này trong kho. Bạn có thể cho EMUA biết thêm về nhu cầu sử dụng hoặc tầm ngân sách dự kiến để nhân viên hỗ trợ gợi ý dòng máy phù hợp nhất nhé!";
            }

            // 3. Câu hỏi KHÔNG liên quan đến mua sắm
            var q = (message ?? "").ToLowerInvariant();

            // Địa chỉ, showroom, giờ mở cửa, liên hệ
            if (q.Contains("địa chỉ") || q.Contains("ở đâu") || q.Contains("mở cửa") ||
                q.Contains("liên hệ") || q.Contains("hotline") || q.Contains("số điện thoại") ||
                q.Contains("sđt") || q.Contains("showroom") || q.Contains("chi nhánh"))
            {
                return "Dạ showroom EMUA mở cửa từ 8:00 đến 21:30 tất cả các ngày trong tuần. Bạn có thể ghé trực tiếp showroom để trải nghiệm sản phẩm hoặc liên hệ hotline cửa hàng qua trang Liên hệ để được nhân viên hỗ trợ chu đáo nhất nhé!";
            }

            // Bảo hành, đổi trả, chính sách
            if (q.Contains("bảo hành") || q.Contains("đổi trả") || q.Contains("chính sách") ||
                q.Contains("trả hàng") || q.Contains("hoàn tiền") || q.Contains("lỗi"))
            {
                return "Dạ chính sách tại EMUA hỗ trợ 1 đổi 1 trong vòng 7 ngày đầu nếu có lỗi phần cứng từ nhà sản xuất, bảo hành chính hãng từ 12 đến 24 tháng theo từng dòng sản phẩm. Bạn có thể xem chi tiết tại mục Chính sách bảo hành của website nhé!";
            }

            // Chào hỏi, cảm ơn
            if (q.Contains("chào") || q.Contains("hello") || q.Contains("hi ") ||
                q.Equals("hi") || q.Contains("cảm ơn") || q.Contains("thanks") || q.Contains("bạn là ai"))
            {
                return "Dạ em chào bạn! Em là trợ lý của EMUA. Em có thể hỗ trợ tư vấn các dòng laptop, điện thoại, thiết bị công nghệ chính hãng hoặc giải đáp thông tin mua hàng. Bạn đang quan tâm đến sản phẩm nào tại EMUA ạ?";
            }

            // Câu hỏi công nghệ hoặc thắc mắc khác
            return "Dạ hiện tại hệ thống AI tư vấn đang tạm thời bận hoặc vượt giới hạn xử lý. Để được giải đáp nhanh và chi tiết nhất về thắc mắc này, bạn có thể nhắn tin trực tiếp với nhân viên hỗ trợ EMUA qua trang Liên hệ nhé!";
        }

        private static bool IsCommonStoreInteraction(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            var q = message.Trim().ToLowerInvariant();
            return q.Contains("chào") || q.Contains("hello") || q.Contains("hi ") || q.Equals("hi") ||
                   q.Contains("cảm ơn") || q.Contains("thanks") || q.Contains("bạn là ai") ||
                   q.Contains("địa chỉ") || q.Contains("ở đâu") || q.Contains("mở cửa") ||
                   q.Contains("liên hệ") || q.Contains("hotline") || q.Contains("sđt") ||
                   q.Contains("bảo hành") || q.Contains("đổi trả") || q.Contains("chính sách");
        }

        private static string GenerateFollowUpAnswer(
            string message,
            ChatProductItemDto product,
            ProductRecommendationDto? productDto)
        {
            var q = (message ?? "").ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            var specs = productDto?.Specs ?? "";
            var desc = productDto?.Description ?? "";
            var fullInfo = $"{specs} {desc}".ToLowerInvariant();

            // 0. Nhu cầu học Trí tuệ nhân tạo (AI) / Machine Learning / Data Science
            if (q.Contains("học ai") || q.Contains("trí tuệ nhân tạo") || q.Contains("máy học") ||
                q.Contains("machine learning") || q.Contains("deep learning") || q.Contains("data science") ||
                q.Contains("khoa học dữ liệu") || (q.Contains("ai") && (q.Contains("ổn không") || q.Contains("được không") || q.Contains("phù hợp") || q.Contains("mượt không"))))
            {
                bool has16GbOrMore = fullInfo.Contains("16gb") || fullInfo.Contains("16 gb") || fullInfo.Contains("32gb") || fullInfo.Contains("32 gb");
                bool has8Gb = fullInfo.Contains("8gb") || fullInfo.Contains("8 gb");
                bool hasDedicatedGpu = fullInfo.Contains("rtx") || fullInfo.Contains("gtx") || fullInfo.Contains("geforce") || fullInfo.Contains("radeon rx");

                sb.AppendLine($"Dạ đối với nhu cầu học ngành Trí tuệ nhân tạo (AI) và Khoa học dữ liệu, mẫu **{product.name}** là một lựa chọn {(has16GbOrMore ? "**rất tốt và phù hợp**" : "**đáp ứng tốt giai đoạn đầu**")} bạn nhé!");

                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Cấu hình phần cứng từ cửa hàng: {specs}");
                }

                if (has16GbOrMore)
                {
                    sb.AppendLine("• Đánh giá RAM: Dung lượng RAM 16GB trở lên giúp bạn thoải mái load các tập dữ liệu (dataset), chạy các mô hình Machine Learning, xử lý dữ liệu với thư viện Python (Pandas, NumPy, Scikit-learn) và chạy mượt mà Jupyter Notebook/VS Code.");
                }
                else if (has8Gb)
                {
                    sb.AppendLine("• Đánh giá RAM: Dung lượng 8GB RAM đáp ứng tốt các môn học thuật toán, lập trình Python và kiến thức nền tảng. Khi học lên các môn chuyên sâu về Deep Learning với tập dữ liệu lớn, bạn có thể cân nhắc nâng cấp thêm RAM hoặc kết hợp huấn luyện trên nền tảng đám mây (Google Colab, Kaggle).");
                }

                if (hasDedicatedGpu)
                {
                    sb.AppendLine("• Card đồ họa (GPU): Máy trang bị card đồ họa rời giúp tăng tốc độ huấn luyện mô hình mạng nơ-ron (Neural Networks) trên môi trường CUDA rất hiệu quả.");
                }
                else
                {
                    sb.AppendLine("• Khả năng huấn luyện: Đối với các mô hình Deep Learning phức tạp đòi hỏi GPU hạng nặng, sinh viên thường huấn luyện trên môi trường Cloud (Google Colab, Azure), do đó CPU mạnh mẽ và RAM ổn định của máy hoàn toàn phục vụ trọn vẹn chương trình học tập.");
                }

                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem đầy đủ thông số chi tiết và ưu đãi tại EMUA nhé!");
                return sb.ToString();
            }

            // 1. Nhu cầu lập trình / code / IT / chạy máy ảo
            if (q.Contains("lập trình") || q.Contains("code") || q.Contains("coder") ||
                q.Contains("it") || q.Contains("visual studio") || q.Contains("dev") || q.Contains("máy ảo"))
            {
                bool has16Gb = fullInfo.Contains("16gb") || fullInfo.Contains("16 gb") || fullInfo.Contains("32gb");
                bool has8Gb = fullInfo.Contains("8gb") || fullInfo.Contains("8 gb");

                sb.AppendLine($"Dạ với nhu cầu học tập và làm việc lập trình, mẫu **{product.name}** dùng **hoàn toàn ổn định và phù hợp** bạn nhé!");

                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Thông số kỹ thuật từ cửa hàng: {specs}");
                }

                if (has16Gb)
                {
                    sb.AppendLine("• Đánh giá cấu hình: Dung lượng RAM 16GB cùng ổ cứng SSD tốc độ cao giúp máy đa nhiệm cực kỳ mượt mà khi bạn mở nhiều tab tra cứu tài liệu, chạy đồng thời VS Code, PyCharm, Visual Studio hoặc build dự án.");
                }
                else if (has8Gb)
                {
                    sb.AppendLine("• Đánh giá cấu hình: Máy đáp ứng tốt các tác vụ lập trình Web, Python, C++, thuật toán và phần mềm thông thường. Nếu sau này bạn có nhu cầu chạy máy ảo Docker nặng, bạn có thể cân nhắc nâng cấp thêm RAM để tối ưu nhất.");
                }
                else
                {
                    sb.AppendLine("• Đánh giá cấu hình: Cấu hình của máy đáp ứng tốt các công cụ lập trình cơ bản và học tập công nghệ thông tin.");
                }

                if (string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine("• Lưu ý: Do hệ thống chưa có bảng thông số kỹ thuật chi tiết của mã này trong kho dữ liệu, bạn vui lòng bấm 'Xem sản phẩm' bên dưới để xem catalogue đầy đủ từ nhà sản xuất nhé!");
                }

                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết hoặc đặt mua nhé!");
                return sb.ToString();
            }

            // 2. Chơi game / Gaming
            if (q.Contains("chơi game") || q.Contains("gaming") || q.Contains("game") ||
                q.Contains("fifa") || q.Contains("lol") || q.Contains("valorant") || q.Contains("genshin"))
            {
                bool hasGpu = fullInfo.Contains("rtx") || fullInfo.Contains("gtx") || fullInfo.Contains("geforce") || fullInfo.Contains("radeon rx");
                if (hasGpu)
                {
                    sb.AppendLine($"Dạ mẫu **{product.name}** trang bị card đồ họa chuyên dụng, hoàn toàn chiến mượt các tựa game eSports (LOL, FO4, Valorant) và game 3D ở mức thiết lập đồ họa thích hợp.");
                }
                else
                {
                    sb.AppendLine($"Dạ mẫu **{product.name}** là dòng máy mỏng nhẹ văn phòng/học tập với card đồ họa tích hợp. Máy có thể giải trí tốt các tựa game nhẹ (như Liên Minh Huyền Thoại, CS:GO, FO4 ở cấu hình vừa phải), nhưng không chuyên dụng để cày các tựa game 3D hạng nặng (AAA).");
                }

                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Cấu hình tham khảo: {specs}");
                }
                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết nhé!");
                return sb.ToString();
            }

            // 3. Đồ họa / Thiết kế
            if (q.Contains("đồ họa") || q.Contains("photoshop") || q.Contains("illustrator") ||
                q.Contains("premiere") || q.Contains("canva") || q.Contains("render") || q.Contains("autocad"))
            {
                sb.AppendLine($"Dạ mẫu **{product.name}** đáp ứng rất tốt các tác vụ thiết kế 2D (Photoshop, Illustrator, Canva, chỉnh sửa ảnh/video cơ bản).");
                sb.AppendLine("Với nhu cầu dựng phim 4K chuyên nghiệp hoặc render 3D nặng kéo dài, bạn nên tham khảo các dòng máy trạm hoặc laptop có trang bị card đồ họa rời nhé.");
                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Cấu hình thực tế: {specs}");
                }
                sb.Append("Bạn có thể bấm vào thẻ sản phẩm bên dưới để xem chi tiết nhé!");
                return sb.ToString();
            }

            // 4. Pin / Trọng lượng / Di chuyển
            if (q.Contains("pin") || q.Contains("nặng") || q.Contains("nhẹ") ||
                q.Contains("trọng lượng") || q.Contains("bao nhiêu kg") || q.Contains("mang đi"))
            {
                sb.AppendLine($"Dạ mẫu **{product.name}** thuộc phân khúc máy hiện đại, thiết kế mỏng nhẹ tiện lợi để bạn bỏ balo mang đi học hay đi làm hàng ngày.");
                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Thông số phần cứng: {specs}");
                }
                else
                {
                    sb.AppendLine("• Thông số chi tiết về khối lượng (kg) và dung lượng pin (Wh) được niêm yết đầy đủ tại trang chi tiết sản phẩm.");
                }
                sb.Append("Bạn có thể bấm vào nút 'Xem sản phẩm' bên dưới để xem thông số chính xác nhất nhé!");
                return sb.ToString();
            }

            // 5. Nâng cấp RAM / SSD
            if (q.Contains("nâng cấp") || q.Contains("thêm ram") || q.Contains("thêm ssd") || q.Contains("gắn thêm"))
            {
                sb.AppendLine($"Dạ khả năng nâng cấp RAM/SSD của **{product.name}** phụ thuộc vào phiên bản bo mạch cụ thể của nhà sản xuất.");
                if (!string.IsNullOrWhiteSpace(specs))
                {
                    sb.AppendLine($"• Cấu hình xuất xưởng: {specs}");
                }
                sb.Append("Để biết máy có sẵn khe cắm nâng cấp hay dùng RAM onboard, bạn vui lòng bấm 'Xem sản phẩm' hoặc liên hệ trực tiếp kỹ thuật EMUA để được hỗ trợ chính xác nhé!");
                return sb.ToString();
            }

            // 6. Câu hỏi đánh giá chung ("ổn không", "tốt không", "thế nào", "có nên mua")
            sb.AppendLine($"Dạ mẫu **{product.name}** (giá hiện tại: **{product.price}**) là sản phẩm chính hãng đang rất được ưa chuộng tại EMUA nhờ độ bền tốt, thiết kế hiện đại và chính sách bảo hành chính hãng uy tín.");
            if (!string.IsNullOrWhiteSpace(specs))
            {
                sb.AppendLine($"• Tóm tắt thông số: {specs}");
            }
            sb.Append("Bạn có thể bấm 'Xem sản phẩm' hoặc 'Mua ngay' bên dưới để nhận thêm các ưu đãi nhé!");
            return sb.ToString();
        }
    }

    public class ChatConversationState
    {
        public List<ProductRecommendationDto> LastProducts { get; set; } = new();
        public BudgetInfo? LastBudget { get; set; }
        public decimal? LastLowestPrice { get; set; }
        public string? LastQuery { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public record ChatProductItemDto(
        int id,
        string name,
        string price,
        bool inStock,
        string? image,
        string url
    );

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}