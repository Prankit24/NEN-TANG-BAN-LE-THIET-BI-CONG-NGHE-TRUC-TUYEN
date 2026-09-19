using System.ComponentModel.DataAnnotations;

namespace EMua.ViewModels;

public class ProductListViewModel
{
    public string? Keyword { get; set; }
    public string? Category { get; set; }
    public string? Sort { get; set; }
    public bool FavoritesOnly { get; set; }
    public List<ProductCardViewModel> Products { get; set; } = [];
}

public class ProductCardViewModel
{
    public int ProductId { get; set; }
    public int DefaultVariantId { get; set; }
    public bool IsFavorite { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
}

public class ProductDetailsViewModel : ProductCardViewModel
{
    public string? Description { get; set; }
    public string? Specifications { get; set; }
    public string? Warranty { get; set; }
    public List<ProductVariantViewModel> Variants { get; set; } = [];
    public List<ProductReviewViewModel> Reviews { get; set; } = [];
    public decimal AverageRating { get; set; }
}

public class ProductVariantViewModel
{
    public int VariantId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
}

public class CartViewModel
{
    public List<CartItemViewModel> Items { get; set; } = [];
    public int TotalQuantity => Items.Sum(x => x.Quantity);
    public decimal Total => Items.Sum(x => x.LineTotal);
}

public class CartItemViewModel
{
    public int CartItemId { get; set; }
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int Stock { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class CheckoutViewModel
{
    public List<CartItemViewModel> Items { get; set; } = [];
    public List<CouponOptionViewModel> AvailableCoupons { get; set; } = [];
    public decimal Subtotal => Items.Sum(x => x.LineTotal);
    public decimal Discount { get; set; }
    public string ShippingMethod { get; set; } = "STANDARD";
    public decimal ShippingFee => ShippingMethod == "EXPRESS" ? 25000 : 0;
    public decimal Total => Math.Max(0, Subtotal - Discount + ShippingFee);
    public string? CouponCode { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ tên người nhận.")]
    [StringLength(100)]
    public string RecipientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [StringLength(15)]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ giao hàng.")]
    [StringLength(255)]
    public string ShippingAddress { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn phương thức thanh toán.")]
    public string PaymentMethod { get; set; } = "COD";

    public string? PaymentProvider { get; set; }
}

public class CouponOptionViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DiscountText { get; set; } = string.Empty;
    public decimal? MinimumOrder { get; set; }
}

public class OrderListItemViewModel
{
    public int OrderId { get; set; }
    public DateTime? OrderedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public class OrderDetailsViewModel
{
    public int OrderId { get; set; }
    public DateTime? OrderedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public List<CartItemViewModel> Items { get; set; } = [];
    public List<OrderStatusViewModel> StatusHistory { get; set; } = [];
}

public class OrderStatusViewModel
{
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Time { get; set; }
}

public class ProductReviewViewModel
{
    public string CustomerName { get; set; } = "Khách hàng";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class ReviewCreateViewModel
{
    public int ProductId { get; set; }
    public int OrderId { get; set; }

    [Range(1, 5, ErrorMessage = "Mức đánh giá phải từ 1 đến 5 sao.")]
    public int Rating { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nhận xét.")]
    [StringLength(255)]
    public string Comment { get; set; } = string.Empty;
}

public class CheckoutSuccessViewModel
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public bool IsPaid { get; set; }
}