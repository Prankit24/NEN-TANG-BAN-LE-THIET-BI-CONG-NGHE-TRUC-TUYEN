namespace EMua.ViewModels;

public class CheckoutViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<CheckoutItemViewModel> Items { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal Discount { get; set; }
    public decimal Total => Math.Max(0, Subtotal + ShippingFee - Discount);
}

public class CheckoutItemViewModel
{
    public int VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class ApplyCouponRequest
{
    public string? CouponCode { get; set; }
}

public class PlaceOrderRequest
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? CouponCode { get; set; }
    public string PaymentMethod { get; set; } = "COD";
}
