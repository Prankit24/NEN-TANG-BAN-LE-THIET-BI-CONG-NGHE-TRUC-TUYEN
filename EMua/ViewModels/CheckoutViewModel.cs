using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class CheckoutViewModel
{
    // =========================================================
    // THÔNG TIN NHẬN HÀNG
    // =========================================================

    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ nhận hàng.")]
    public string Address { get; set; } = string.Empty;

    public string? Note { get; set; }


    // =========================================================
    // SẢN PHẨM
    // =========================================================

    public List<CheckoutItemViewModel> Items { get; set; } = [];


    // =========================================================
    // KHUYẾN MÃI
    // =========================================================

    public string? CouponCode { get; set; }


    // =========================================================
    // THANH TOÁN
    // =========================================================

    public string PaymentMethod { get; set; } = "MBBANK";


    // =========================================================
    // TỔNG TIỀN
    // =========================================================

    public decimal Subtotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal Discount { get; set; }

    public decimal Total =>
        Math.Max(
            0m,
            Subtotal + ShippingFee - Discount
        );


    // =========================================================
    // THÔNG TIN PHỤ
    // =========================================================

    public int TotalQuantity =>
        Items.Sum(x => x.Quantity);

    public bool HasItems =>
        Items.Count > 0;
}


// =============================================================
// SẢN PHẨM TRONG CHECKOUT
// =============================================================

public class CheckoutItemViewModel
{
    public int VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal =>
        UnitPrice * Quantity;
}


// =============================================================
// REQUEST ÁP DỤNG MÃ GIẢM GIÁ
// =============================================================

public class ApplyCouponRequest
{
    public string? CouponCode { get; set; }
}


// =============================================================
// REQUEST ĐẶT HÀNG
// =============================================================

public class PlaceOrderRequest
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string Address { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string? CouponCode { get; set; }

    public string PaymentMethod { get; set; } = "MBBANK";
}