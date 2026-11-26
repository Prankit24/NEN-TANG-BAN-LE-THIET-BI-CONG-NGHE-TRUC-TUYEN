using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMua.Services
{
    public class TelegramNotificationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramNotificationService> _logger;

        public TelegramNotificationService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<TelegramNotificationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task NotifyNewProductAsync(
            int productId,
            string productName,
            string categoryName,
            string brandName,
            decimal price,
            string? imageUrl,
            string createdBy)
        {
            try
            {
                var enabled = _configuration.GetValue<bool>("Telegram:Enabled");
                var botToken = _configuration["Telegram:BotToken"]?.Trim();
                var chatId = _configuration["Telegram:ChatId"]?.Trim();

                if (!enabled || string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
                {
                    return;
                }

                var baseUrl = (_configuration["App:BaseUrl"] ?? "https://emua-shop.somee.com").TrimEnd('/');
                var detailUrl = $"{baseUrl}/Product/Details/{productId}";
                var timeStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                var priceStr = price > 0 ? $"{price:N0} đ" : "Liên hệ";

                var safeName = WebUtility.HtmlEncode(productName);
                var safeCategory = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(categoryName) ? "Chưa phân loại" : categoryName);
                var safeBrand = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(brandName) ? "EMUA" : brandName);
                var safeCreator = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(createdBy) ? "Nhân viên" : createdBy);

                bool hasValidImage = !string.IsNullOrWhiteSpace(imageUrl)
                    && !imageUrl.Contains("no-image", StringComparison.OrdinalIgnoreCase);

                string? fullPhotoUrl = null;
                if (hasValidImage)
                {
                    var trimmedImg = imageUrl!.Trim();
                    if (trimmedImg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        trimmedImg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        fullPhotoUrl = trimmedImg;
                    }
                    else
                    {
                        fullPhotoUrl = $"{baseUrl}/{(trimmedImg.TrimStart('/'))}";
                    }
                }

                bool isPublicDomain = !baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    && !baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                object? replyMarkup = isPublicDomain
                    ? new
                    {
                        inline_keyboard = new[]
                        {
                            new[]
                            {
                                new
                                {
                                    text = "🔍 Xem sản phẩm trên Web",
                                    url = detailUrl
                                }
                            }
                        }
                    }
                    : null;

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);

                if (hasValidImage && !string.IsNullOrWhiteSpace(fullPhotoUrl))
                {
                    var caption = $"🆕 <b>SẢN PHẨM MỚI ĐƯỢC THÊM</b>\n\n" +
                                  $"📦 <b>Tên sản phẩm:</b> {safeName}\n" +
                                  $"🏷️ <b>Danh mục / Hãng:</b> {safeCategory} · {safeBrand}\n" +
                                  $"💰 <b>Giá bán:</b> {priceStr}\n" +
                                  $"👤 <b>Người tạo:</b> {safeCreator}\n" +
                                  $"🕒 <b>Thời gian:</b> {timeStr}";

                    object payload = replyMarkup != null
                        ? new { chat_id = chatId, photo = fullPhotoUrl, caption, parse_mode = "HTML", reply_markup = replyMarkup }
                        : new { chat_id = chatId, photo = fullPhotoUrl, caption, parse_mode = "HTML" };

                    var endpoint = $"https://api.telegram.org/bot{botToken}/sendPhoto";
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(endpoint, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram sendPhoto failed ({StatusCode}): {Body}", response.StatusCode, errBody);
                    }
                }
                else
                {
                    var text = $"🆕 <b>SẢN PHẨM MỚI ĐƯỢC THÊM</b>\n\n" +
                               $"📦 <b>Tên sản phẩm:</b> {safeName}\n" +
                               $"🏷️ <b>Danh mục / Hãng:</b> {safeCategory} · {safeBrand}\n" +
                               $"💰 <b>Giá bán:</b> {priceStr}\n" +
                               $"👤 <b>Người tạo:</b> {safeCreator}\n" +
                               $"🕒 <b>Thời gian:</b> {timeStr}\n\n" +
                               $"⚠️ <i>[Lưu ý: Sản phẩm này chưa có hình ảnh]</i>";

                    object payload = replyMarkup != null
                        ? new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = replyMarkup }
                        : new { chat_id = chatId, text, parse_mode = "HTML" };

                    var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(endpoint, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram sendMessage failed ({StatusCode}): {Body}", response.StatusCode, errBody);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi gửi thông báo sản phẩm mới qua Telegram.");
            }
        }

        public async Task NotifyNewOrderAsync(
            int orderId,
            string customerName,
            string phone,
            string address,
            string paymentMethod,
            string itemsSummary,
            decimal totalAmount)
        {
            try
            {
                var enabled = _configuration.GetValue<bool>("Telegram:Enabled");
                var botToken = _configuration["Telegram:BotToken"]?.Trim();
                var chatId = _configuration["Telegram:ChatId"]?.Trim();

                if (!enabled || string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
                {
                    return;
                }

                var baseUrl = (_configuration["App:BaseUrl"] ?? "https://emua-shop.somee.com").TrimEnd('/');
                var orderCode = $"#DH-{orderId:D5}";
                var timeStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                var totalStr = $"{totalAmount:N0} đ";

                var safeCustomer = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(customerName) ? "Khách hàng" : customerName);
                var safePhone = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(phone) ? "—" : phone);
                var safeAddress = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(address) ? "—" : address);
                var safePayment = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(paymentMethod) ? "COD" : paymentMethod);
                var safeItems = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(itemsSummary) ? "—" : itemsSummary);

                var text = $"🛒 <b>ĐƠN HÀNG MỚI {orderCode}</b>\n\n" +
                           $"👤 <b>Khách hàng:</b> {safeCustomer} ({safePhone})\n" +
                           $"📍 <b>Địa chỉ:</b> {safeAddress}\n" +
                           $"💳 <b>Thanh toán:</b> {safePayment}\n" +
                           $"📦 <b>Sản phẩm:</b>\n{safeItems}\n\n" +
                           $"💰 <b>Tổng tiền:</b> <b>{totalStr}</b>\n" +
                           $"🕒 <b>Thời gian đặt:</b> {timeStr}";

                bool isPublicDomain = !baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                    && !baseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                object payload = isPublicDomain
                    ? new
                    {
                        chat_id = chatId,
                        text,
                        parse_mode = "HTML",
                        reply_markup = new
                        {
                            inline_keyboard = new[]
                            {
                                new[]
                                {
                                    new
                                    {
                                        text = "📋 Xem đơn hàng trên Web",
                                        url = $"{baseUrl}/Staff/Order/Details/{orderId}"
                                    }
                                }
                            }
                        }
                    }
                    : new
                    {
                        chat_id = chatId,
                        text,
                        parse_mode = "HTML"
                    };

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);

                var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Telegram NotifyNewOrderAsync failed ({StatusCode}): {Body}", response.StatusCode, errBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi gửi thông báo đơn hàng mới qua Telegram.");
            }
        }
    }
}
