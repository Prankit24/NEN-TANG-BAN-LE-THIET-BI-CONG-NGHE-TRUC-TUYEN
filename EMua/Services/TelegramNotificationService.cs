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

        private static bool IsValidPublicUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var host = uri.Host;
            return !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
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

                var baseUrl = (_configuration["App:BaseUrl"] ?? "https://emua.id.vn").Trim().TrimEnd('/');
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

                bool canAttachButton = IsValidPublicUrl(detailUrl);
                object? replyMarkup = canAttachButton
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

                if (hasValidImage && !string.IsNullOrWhiteSpace(fullPhotoUrl) && IsValidPublicUrl(fullPhotoUrl))
                {
                    var caption = $"🆕 <b>SẢN PHẨM MỚI ĐƯỢC THÊM</b>\n\n" +
                                  $"📦 <b>Tên sản phẩm:</b> {safeName}\n" +
                                  $"🏷️ <b>Danh mục / Hãng:</b> {safeCategory} · {safeBrand}\n" +
                                  $"💰 <b>Giá bán:</b> {priceStr}\n" +
                                  $"👤 <b>Người tạo:</b> {safeCreator}\n" +
                                  $"🕒 <b>Thời gian:</b> {timeStr}";

                    var endpoint = $"https://api.telegram.org/bot{botToken}/sendPhoto";
                    bool success = false;

                    if (replyMarkup != null)
                    {
                        var payload = new { chat_id = chatId, photo = fullPhotoUrl, caption, parse_mode = "HTML", reply_markup = replyMarkup };
                        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(endpoint, content);
                        if (response.IsSuccessStatusCode)
                        {
                            success = true;
                        }
                        else
                        {
                            var errBody = await response.Content.ReadAsStringAsync();
                            _logger.LogWarning("Telegram sendPhoto with reply_markup failed ({StatusCode}): {Body}. Đang thử gửi lại không kèm nút bấm.", response.StatusCode, errBody);
                        }
                    }

                    if (!success)
                    {
                        var fallbackPayload = new { chat_id = chatId, photo = fullPhotoUrl, caption, parse_mode = "HTML" };
                        var fallbackContent = new StringContent(JsonSerializer.Serialize(fallbackPayload), Encoding.UTF8, "application/json");
                        var fallbackResponse = await client.PostAsync(endpoint, fallbackContent);
                        if (fallbackResponse.IsSuccessStatusCode)
                        {
                            success = true;
                        }
                        else
                        {
                            var errBody = await fallbackResponse.Content.ReadAsStringAsync();
                            _logger.LogWarning("Telegram sendPhoto without reply_markup failed ({StatusCode}): {Body}. Đang chuyển sang gửi tin nhắn văn bản.", fallbackResponse.StatusCode, errBody);
                        }
                    }

                    if (success) return;
                }

                // Fallback gửi tin nhắn text nếu không có ảnh hoặc gửi ảnh không thành công
                var text = $"🆕 <b>SẢN PHẨM MỚI ĐƯỢC THÊM</b>\n\n" +
                           $"📦 <b>Tên sản phẩm:</b> {safeName}\n" +
                           $"🏷️ <b>Danh mục / Hãng:</b> {safeCategory} · {safeBrand}\n" +
                           $"💰 <b>Giá bán:</b> {priceStr}\n" +
                           $"👤 <b>Người tạo:</b> {safeCreator}\n" +
                           $"🕒 <b>Thời gian:</b> {timeStr}\n\n" +
                           $"⚠️ <i>[Lưu ý: Sản phẩm này chưa có hình ảnh]</i>";

                var sendMsgEndpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
                bool msgSent = false;

                if (replyMarkup != null)
                {
                    var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = replyMarkup };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(sendMsgEndpoint, content);
                    if (response.IsSuccessStatusCode)
                    {
                        msgSent = true;
                    }
                    else
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram sendMessage with reply_markup failed ({StatusCode}): {Body}. Đang gửi lại văn bản thuần.", response.StatusCode, errBody);
                    }
                }

                if (!msgSent)
                {
                    var plainPayload = new { chat_id = chatId, text, parse_mode = "HTML" };
                    var plainContent = new StringContent(JsonSerializer.Serialize(plainPayload), Encoding.UTF8, "application/json");
                    var plainResponse = await client.PostAsync(sendMsgEndpoint, plainContent);
                    if (!plainResponse.IsSuccessStatusCode)
                    {
                        var errBody = await plainResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram sendMessage fallback failed ({StatusCode}): {Body}", plainResponse.StatusCode, errBody);
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

                var baseUrl = (_configuration["App:BaseUrl"] ?? "https://emua.id.vn").Trim().TrimEnd('/');
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

                var orderDetailUrl = $"{baseUrl}/Staff/Order/Details/{orderId}";
                bool canAttachButton = IsValidPublicUrl(orderDetailUrl);

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";

                bool success = false;

                if (canAttachButton)
                {
                    var payloadWithButton = new
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
                                        url = orderDetailUrl
                                    }
                                }
                            }
                        }
                    };

                    var content = new StringContent(JsonSerializer.Serialize(payloadWithButton), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(endpoint, content);

                    if (response.IsSuccessStatusCode)
                    {
                        success = true;
                    }
                    else
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram NotifyNewOrderAsync with reply_markup failed ({StatusCode}): {Body}. Đang gửi lại văn bản thuần dự phòng.", response.StatusCode, errBody);
                    }
                }

                if (!success)
                {
                    var plainPayload = new
                    {
                        chat_id = chatId,
                        text,
                        parse_mode = "HTML"
                    };

                    var plainContent = new StringContent(JsonSerializer.Serialize(plainPayload), Encoding.UTF8, "application/json");
                    var plainResponse = await client.PostAsync(endpoint, plainContent);

                    if (!plainResponse.IsSuccessStatusCode)
                    {
                        var errBody = await plainResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning("Telegram NotifyNewOrderAsync fallback failed ({StatusCode}): {Body}", plainResponse.StatusCode, errBody);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi gửi thông báo đơn hàng mới qua Telegram.");
            }
        }
    }
}
