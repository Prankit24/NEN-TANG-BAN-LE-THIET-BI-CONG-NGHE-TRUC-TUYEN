using System.Text;
using System.Text.RegularExpressions;
using EMua.Data;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services.AI
{
    public sealed record OrderTrackingAnswer(string Message, int? OrderId);

    /// <summary>
    /// Trả lời câu hỏi "đơn hàng của tôi đến đâu rồi" bằng DỮ LIỆU THẬT trong database,
    /// chỉ với đơn của chính khách đang đăng nhập. Không giao cho Gemini/Groq vì AI không có dữ liệu đơn
    /// và sẽ đoán sai tình trạng giao hàng.
    /// </summary>
    public sealed class OrderTrackingAssistant
    {
        private readonly EMuaDbContext _db;
        public OrderTrackingAssistant(EMuaDbContext db) => _db = db;

        private static readonly string[] Phrases =
        {
            "đơn hàng của tôi", "đơn hàng của mình", "đơn của tôi", "đơn của mình", "đơn hàng vừa đặt", "vừa đặt hàng",
            "theo dõi đơn", "tra cứu đơn", "tra cứu vận đơn", "kiểm tra đơn", "trạng thái đơn", "tình trạng đơn",
            "đơn hàng đến đâu", "đơn hàng tới đâu", "đơn đến đâu", "đơn tới đâu", "giao đến đâu", "giao tới đâu",
            "bao giờ giao", "bao giờ nhận được", "khi nào giao", "khi nào nhận được hàng", "đã giao chưa",
            "chưa nhận được hàng", "chưa thấy giao", "mã vận đơn", "shipper"
        };

        // #DH-00012, DH12, "đơn 12", "đơn hàng số 12" (loại trừ "đơn 2 triệu", "đơn 500k")
        private static readonly Regex OrderCode = new(
            @"(?:#?\s*(?<![\p{L}\p{N}])DH\s*-?\s*0*(?<id>\d{1,9})(?!\d))" +
            @"|(?:(?<![\p{L}\p{N}])đơn(?:\s+hàng)?\s*(?:số|mã)?\s*#?\s*0*(?<id>\d{1,9})(?![\p{L}\p{N}])(?!\s*(?:triệu|tr\b|k\b|nghìn|ngàn|đ\b|vnđ|%)))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsOrderTrackingQuery(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (OrderCode.IsMatch(message)) return true;
            var q = message.Trim().ToLowerInvariant();
            return Phrases.Any(phrase => q.Contains(phrase));
        }

        public static int? ExtractOrderId(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            var m = OrderCode.Match(message);
            return m.Success && int.TryParse(m.Groups["id"].Value, out var id) && id > 0 ? id : null;
        }

        public async Task<OrderTrackingAnswer> AnswerAsync(int userId, string message, CancellationToken ct = default)
        {
            if (userId <= 0)
            {
                return new("Để tra cứu tình trạng đơn hàng, bạn vui lòng đăng nhập tài khoản EMUA rồi hỏi lại giúp mình nhé. " +
                           "Sau khi đăng nhập, bạn chỉ cần gõ \"đơn hàng của tôi đến đâu rồi\" hoặc kèm mã đơn, ví dụ #DH-00012.", null);
            }

            var requestedId = ExtractOrderId(message);
            var query = _db.DonHangs.AsNoTracking().Where(x => x.MaNguoiDung == userId);
            var order = requestedId.HasValue
                ? await query.Include(x => x.ThanhToans).AsSplitQuery().FirstOrDefaultAsync(x => x.MaDonHang == requestedId.Value, ct)
                : await query.Include(x => x.ThanhToans).AsSplitQuery()
                    .OrderByDescending(x => x.NgayDat).ThenByDescending(x => x.MaDonHang).FirstOrDefaultAsync(ct);

            if (order == null)
            {
                return requestedId.HasValue
                    ? new($"Mình không tìm thấy đơn #DH-{requestedId.Value:D5} trong tài khoản của bạn. Bạn kiểm tra lại mã đơn trong mục Đơn hàng của tôi giúp mình nhé.", null)
                    : new("Tài khoản của bạn hiện chưa có đơn hàng nào. Bạn có thể chọn sản phẩm và đặt hàng, sau đó hỏi mình để theo dõi đơn nhé!", null);
            }

            var status = OrderRules.Normalize(order.TrangThaiDonHang);
            var payment = order.ThanhToans.OrderByDescending(p => p.NgayTao).ThenByDescending(p => p.MaThanhToan).FirstOrDefault();
            var isCod = OrderRules.IsCod(payment?.PhuongThuc);
            var isPaid = OrderRules.IsPaid(payment?.TrangThai);
            var total = order.TongTien ?? 0m;
            var code = $"#DH-{order.MaDonHang:D5}";
            var eta = order.NgayDuKienGiao?.AddHours(7); // DB lưu UTC, hiển thị giờ Việt Nam

            var sb = new StringBuilder();
            sb.AppendLine(requestedId.HasValue
                ? $"Đơn {code} (đặt {order.NgayDat:dd/MM/yyyy HH:mm}, tổng {total:N0}đ):"
                : $"Đơn gần nhất của bạn là {code} (đặt {order.NgayDat:dd/MM/yyyy HH:mm}, tổng {total:N0}đ):");

            switch (status)
            {
                case OrderRules.Pending:
                case OrderRules.AwaitingPayment:
                    if (!isCod && !isPaid)
                    {
                        sb.Append(payment?.TrangThai == OrderRules.PaymentReported
                            ? "• Bạn đã báo chuyển tiền, nhân viên đang đối soát khoản thanh toán. Sau khi xác nhận, đơn sẽ được chuẩn bị."
                            : "• Đơn đang chờ thanh toán. Sau khi bạn thanh toán và nhân viên xác nhận, đơn sẽ được chuẩn bị giao.");
                    }
                    else
                    {
                        sb.Append("• Đơn đã được ghi nhận và đang chờ nhân viên xác nhận.");
                    }
                    break;

                case OrderRules.Processing:
                    sb.Append("• Đơn đã được xác nhận, EMUA đang chuẩn bị hàng và sẽ bàn giao cho đơn vị vận chuyển trong thời gian sớm nhất.");
                    break;

                case OrderRules.Shipping:
                    sb.AppendLine("• Đơn đang trên đường giao đến bạn.");
                    if (eta.HasValue) sb.AppendLine($"• Dự kiến giao: {eta.Value:HH:mm, dd/MM/yyyy}.");
                    if (isCod && !isPaid) sb.Append($"• Đơn thanh toán khi nhận hàng, bạn vui lòng chuẩn bị {total:N0}đ.");
                    break;

                case OrderRules.DeliveryFailed:
                    sb.Append("• Lần giao gần nhất chưa thành công. Nhân viên EMUA sẽ liên hệ qua số điện thoại nhận hàng để sắp xếp giao lại hoặc hỗ trợ hủy đơn. Bạn cũng có thể chủ động liên hệ hotline để được hỗ trợ nhanh hơn.");
                    break;

                case OrderRules.Completed:
                    sb.Append("• Đơn đã giao thành công. Nếu sản phẩm có vấn đề, bạn có thể gửi yêu cầu đổi trả trong 7 ngày kể từ lúc hoàn thành ngay tại trang chi tiết đơn.");
                    break;

                case OrderRules.Cancelled:
                    sb.Append(payment?.TrangThai == OrderRules.RefundPending
                        ? "• Đơn đã bị hủy. Khoản tiền bạn đã thanh toán đang chờ nhân viên hoàn lại."
                        : "• Đơn đã bị hủy.");
                    break;

                case OrderRules.ReturnRequested:
                    sb.Append("• Yêu cầu đổi trả của bạn đã được gửi và đang chờ nhân viên xem xét.");
                    break;

                case OrderRules.Returning:
                    sb.Append("• Yêu cầu đổi trả đã được chấp nhận, đang chờ thu hồi và kiểm tra hàng.");
                    break;

                case OrderRules.ReturnReceived:
                    sb.Append("• EMUA đã nhận lại hàng đổi trả và đang hoàn tất xử lý.");
                    break;

                case OrderRules.Refunded:
                    sb.Append("• Đơn đã được hoàn tiền.");
                    break;

                default:
                    sb.Append($"• Trạng thái hiện tại: {status}.");
                    break;
            }

            sb.AppendLine().Append("Bạn có thể xem chi tiết tiến trình trong mục Đơn hàng của tôi nhé!");
            return new(sb.ToString(), order.MaDonHang);
        }
    }
}
