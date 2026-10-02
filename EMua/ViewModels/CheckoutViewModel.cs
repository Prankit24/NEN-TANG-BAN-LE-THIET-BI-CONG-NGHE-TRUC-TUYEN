using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class CheckoutViewModel
{
    // =========================================================
    // THÔNG TIN NHẬN HÀNG
    // =========================================================

    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    public string RecipientName
    {
        get => FullName;
        set => FullName = value;
    }

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^(0[3|5|7|8|9])+([0-9]{8})$", ErrorMessage = "Số điện thoại không hợp lệ (Phải đúng định dạng SĐT Việt Nam 10 số).")]
    public string PhoneNumber { get; set; } = string.Empty;

    public string RecipientPhone
    {
        get => PhoneNumber;
        set => PhoneNumber = value;
    }

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ nhận hàng.")]
    [StringLength(255, ErrorMessage = "Địa chỉ nhận hàng quá dài.")]
    public string Address { get; set; } = string.Empty;

    public string ShippingAddress
    {
        get => Address;
        set => Address = value;
    }

    [StringLength(500, ErrorMessage = "Ghi chú không được quá 500 ký tự.")]
    public string? Note { get; set; }


    // =========================================================
    // PHƯƠNG THỨC GIAO HÀNG
    // =========================================================

    public string ShippingMethod { get; set; } = "STANDARD";


    // =========================================================
    // PHƯƠNG THỨC THANH TOÁN
    // COD / BANK_TRANSFER / WEB3
    // =========================================================

    public string PaymentMethod { get; set; } = "COD";

    public string? PaymentProvider { get; set; }


    // =========================================================
    // SẢN PHẨM & COUPON
    // =========================================================

    public List<CheckoutItemViewModel> Items { get; set; } = new();

    public string? CouponCode { get; set; }


    // =========================================================
    // TỔNG TIỀN
    // =========================================================

    public decimal Subtotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal Discount { get; set; }

    public decimal Total =>
        Math.Max(0m, Subtotal + ShippingFee - Discount);

    public int TotalQuantity =>
        Items?.Sum(x => x.Quantity) ?? 0;

    public bool HasItems =>
        Items != null && Items.Count > 0;
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
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^(0[3|5|7|8|9])+([0-9]{8})$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ nhận hàng.")]
    [StringLength(255)]
    public string Address { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string? CouponCode { get; set; }

    public string PaymentMethod { get; set; } = "COD";
}