using EMua.Models.AI;
using Microsoft.ML;

namespace EMua.Services.AI
{
    /// <summary>
    /// Phân loại câu hỏi có thuộc lĩnh vực công nghệ/cửa hàng hay không.
    /// - Tạo PredictionEngine MỘT LẦN (trước đây tạo mới ở mỗi request: chậm và tốn RAM).
    /// - PredictionEngine không thread-safe nên được bảo vệ bằng lock (DomainClassifier là Singleton).
    /// - Thiếu/hỏng file model KHÔNG làm sập chatbot: chuyển sang phân loại theo từ khóa.
    /// - Model chỉ học từ ít mẫu nên bổ sung từ khóa công nghệ để tránh từ chối nhầm câu hỏi thật.
    /// </summary>
    public class DomainClassifier
    {
        private static readonly string[] TechKeywords =
        {
            "laptop", "máy tính", "pc", "macbook", "ipad", "tablet", "điện thoại", "smartphone", "iphone", "samsung",
            "galaxy", "xiaomi", "oppo", "tai nghe", "airpods", "chuột", "bàn phím", "màn hình", "loa", "sạc", "cáp",
            "phụ kiện", "ram", "ssd", "hdd", "cpu", "gpu", "vga", "rtx", "gtx", "card đồ họa", "pin", "wifi",
            "bluetooth", "windows", "android", "ios", "cấu hình", "thông số", "bảo hành", "linh kiện", "công nghệ"
        };

        private readonly ILogger<DomainClassifier> _logger;
        private readonly PredictionEngine<DomainTrainingData, DomainPrediction>? _engine;
        private readonly object _engineLock = new();

        public DomainClassifier(IWebHostEnvironment environment, ILogger<DomainClassifier> logger)
        {
            _logger = logger;
            var modelPath = Path.Combine(environment.ContentRootPath, "MLModels", "domain-model.zip");

            try
            {
                if (!File.Exists(modelPath))
                {
                    _logger.LogWarning("EMUA AI: Không thấy {ModelPath}. Chatbot dùng phân loại theo từ khóa.", modelPath);
                    return;
                }

                var mlContext = new MLContext(seed: 1);
                var model = mlContext.Model.Load(modelPath, out _);
                _engine = mlContext.Model.CreatePredictionEngine<DomainTrainingData, DomainPrediction>(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EMUA AI: Không tải được domain-model.zip. Chatbot dùng phân loại theo từ khóa.");
                _engine = null;
            }
        }

        public DomainPrediction Predict(string text)
        {
            if (_engine != null)
            {
                try
                {
                    DomainPrediction prediction;
                    lock (_engineLock)
                    {
                        prediction = _engine.Predict(new DomainTrainingData { Text = text });
                    }

                    // Model ít dữ liệu hay từ chối nhầm câu có từ khóa công nghệ rõ ràng.
                    if (!prediction.IsTechnology && LooksTechnical(text))
                    {
                        prediction.IsTechnology = true;
                    }

                    return prediction;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EMUA AI: Lỗi dự đoán bằng model. Dùng phân loại theo từ khóa.");
                }
            }

            return new DomainPrediction { IsTechnology = LooksTechnical(text) || RecommendationService.IsShoppingQuery(text) };
        }

        private static bool LooksTechnical(string? text) => TextMatch.HasAnyWord(text, TechKeywords);
    }
}
