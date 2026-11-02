using EMua.Models.Database;

namespace EMua.Services;

public static class ShoppingRules
{
    public static bool IsPercent(string? type) =>
        type?.Contains("%") == true ||
        type?.Contains("trăm", StringComparison.OrdinalIgnoreCase) == true;

    public static decimal Discount(KhuyenMai coupon, decimal subtotal) =>
        Math.Clamp(IsPercent(coupon.LoaiGiamGia)
            ? subtotal * coupon.GiaTriGiam / 100m : coupon.GiaTriGiam,
            0m, Math.Max(0m, subtotal));
}
