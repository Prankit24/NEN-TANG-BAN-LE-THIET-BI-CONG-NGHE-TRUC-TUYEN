namespace EMua.Services;

public static class OrderRules
{
    public const string Pending = "Chờ xử lý", AwaitingPayment = "Chờ thanh toán", Processing = "Đang xử lý",
        Shipping = "Đang giao hàng", DeliveryFailed = "Giao thất bại",
        Completed = "Đã hoàn thành", Cancelled = "Đã hủy", ReturnRequested = "Yêu cầu đổi trả",
        Returning = "Đang đổi trả", ReturnReceived = "Đã nhận đổi trả", Refunded = "Đã hoàn tiền",
        ReturnRejected = "Từ chối đổi trả";
    public const string Paid = "Đã thanh toán", PaymentReported = "Chờ xác minh", RefundPending = "Chờ hoàn tiền",
        CancelledReconcile = "Cần đối soát khi hủy";
    public const decimal StandardFee = 30000m, ExpressFee = 25000m, FreeShippingMinimum = 500000m;
    public const decimal MaximumOrderTotal = 9999999999.99m; // Cột TongTien hiện là numeric(12,2).
    public static readonly string[] PendingAliases = { Pending, "Chờ xác nhận", AwaitingPayment };
    // Đang xử lý = nhân viên đã xác nhận, đang chuẩn bị/đóng gói. Đang giao hàng = đã bàn giao cho đơn vị vận chuyển.
    public static readonly string[] ProcessingAliases = { Processing };
    public static readonly string[] ShippingAliases = { Shipping, "Đang giao" };
    public static readonly string[] DeliveryFailedAliases = { DeliveryFailed, "Giao hàng thất bại" };
    public static readonly string[] CompletedAliases = { Completed, "Đã giao", "Hoàn thành" };
    public static readonly string[] CancelledAliases = { Cancelled, "Đã huỷ" };
    public static readonly string[] Statuses = { Pending, AwaitingPayment, Processing, Shipping, DeliveryFailed, Completed, Cancelled,
        ReturnRequested, Returning, ReturnReceived, Refunded, ReturnRejected };

    public static DateTime VietnamNow() => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
    public static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    public static string Normalize(string? status)
    {
        status = status?.Trim();
        if (string.IsNullOrEmpty(status) || status == "Chờ xác nhận") return Pending;
        if (ProcessingAliases.Contains(status)) return Processing;
        if (ShippingAliases.Contains(status)) return Shipping;
        if (DeliveryFailedAliases.Contains(status)) return DeliveryFailed;
        if (CompletedAliases.Contains(status)) return Completed;
        if (CancelledAliases.Contains(status)) return Cancelled;
        return status;
    }
    public static bool IsPending(string? status) => Normalize(status) is Pending or AwaitingPayment;
    public static bool IsInDelivery(string? status) => Normalize(status) is Processing or Shipping or DeliveryFailed;
    public static bool IsPaid(string? status) => status is Paid or "Thành công";
    public static bool IsCod(string? method) => method?.Contains("COD", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsWeb3(string? method) => method?.Contains("WEB3", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsPercent(string? type) => type?.Contains('%') == true || type?.Contains("trăm", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsExpress(string? method) => method == "EXPRESS" || method == "Giao hàng nhanh";
    public static bool KnownShipping(string? method) => method is "EXPRESS" or "STANDARD" or "Giao hàng nhanh" or "Giao hàng tiêu chuẩn";
    public static decimal ShippingFee(decimal subtotal, bool express) => express ? ExpressFee : subtotal >= FreeShippingMinimum ? 0m : StandardFee;

    public static OrderTotals Calculate(decimal subtotal, bool express, string? couponType, decimal couponValue, decimal minimum)
    {
        if (subtotal <= 0 || subtotal > MaximumOrderTotal || couponValue < 0 || minimum < 0)
            throw new ArgumentException("Tiền hàng hoặc điều kiện giảm giá không hợp lệ.");
        if (couponValue > 0 && subtotal < minimum)
            throw new ArgumentException("Số lượng mới làm tiền hàng thấp hơn mức tối thiểu của mã giảm giá. Vui lòng giữ đủ giá trị đơn hoặc hủy và đặt lại.");
        var discount = couponValue <= 0 ? 0m : Money(Math.Clamp(IsPercent(couponType) ? subtotal * couponValue / 100m : couponValue, 0m, subtotal));
        var fee = ShippingFee(subtotal, express);
        var total = Money(Math.Max(0m, subtotal + fee - discount));
        if (total > MaximumOrderTotal) throw new ArgumentException("Tổng giá trị đơn vượt giới hạn lưu trữ của hệ thống.");
        return new OrderTotals(Money(subtotal), fee, discount, total);
    }

    public static string[] QuickTargets(string? state, bool cod, bool paid)
    {
        var current = Normalize(state);
        if (IsPending(current)) return cod || paid ? new[] { current, Processing, Cancelled } : new[] { current, Cancelled };
        if (current == Processing) return new[] { current, Shipping };
        // Giao thành công cần ảnh/thu COD, giao thất bại cần lý do: làm ở màn hình chi tiết.
        return new[] { current };
    }

    public static string[] DetailTargets(string? state, bool cod, bool paid)
    {
        return new[] { Pending, Processing, Shipping, Completed, DeliveryFailed, Cancelled };
    }

    public static bool CanTransition(string? from, string? to, bool cod, bool paid, bool proof, bool codCollected, bool returnedReceived)
    {
        var current = Normalize(from); var target = Normalize(to);
        if (!Statuses.Contains(target)) return false;
        if (current == target) return true;
        if (IsPending(current) && target == Cancelled) return true;
        if (IsPending(current) && target == Processing) return cod || paid;
        if (current == Processing && target == Shipping) return true;
        if (current == Shipping && target == Completed) return proof && (cod ? paid || codCollected : paid);
        if ((IsPending(current) || current == Processing) && target is Shipping or Completed) return true;
        if (current == Shipping && target == DeliveryFailed) return true;
        if (current == DeliveryFailed && target == Shipping) return true;
        if (current == DeliveryFailed && target == Cancelled) return true;
        if (current == Returning && target == ReturnReceived) return returnedReceived;
        if (current == ReturnReceived && target == Refunded) return true;
        if (current == ReturnReceived && target == Completed) return true;
        return false;
    }
}

public sealed record OrderTotals(decimal Subtotal, decimal Shipping, decimal Discount, decimal Total);