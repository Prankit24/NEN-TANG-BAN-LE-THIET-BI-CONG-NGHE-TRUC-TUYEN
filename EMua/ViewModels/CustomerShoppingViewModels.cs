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
    private const decimal StandardShippingFee = 30000m;
    private const decimal FreeShippingThreshold = 500000m;

    public List<CartItemViewModel> Items { get; set; } = [];
    public int TotalQuantity => Items.Sum(x => x.Quantity);
    public int ItemCount => Items.Count;

    // Tạm tính (chưa trừ khuyến mãi)
    public decimal Subtotal => Items.Sum(x => x.LineTotal);

    // Phí vận chuyển: miễn phí từ 500.000đ trở lên (đồng bộ với trang Checkout)
    public decimal ShippingFee => Subtotal <= 0 ? 0 : (Subtotal >= FreeShippingThreshold ? 0 : StandardShippingFee);

    // Mã khuyến mãi đang được áp dụng cho giỏ hàng (lưu qua Session)
    public string? CouponCode { get; set; }
    public decimal Discount { get; set; }

    // Tổng cộng = Tạm tính + Phí vận chuyển - Giảm giá
    public decimal FinalTotal => Math.Max(0m, Subtotal + ShippingFee - Discount);

    // Danh sách mã khuyến mãi đang khả dụng để gợi ý cho khách
    public List<CouponOptionViewModel> AvailableCoupons { get; set; } = [];
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
public class OrderDetailItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; } // Thêm thuộc tính này
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
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
    public string Status { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemSummaryViewModel> Items { get; set; } = new(); // Danh sách sản phẩm
    public int ItemCount { get; internal set; }
}

public class OrderItemSummaryViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string ProductImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
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

// --- CẬP NHẬT TRANG THÀNH CÔNG / TIẾP TỤC THANH TOÁN ---
public class CheckoutSuccessViewModel
{
    public int OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Total { get => TotalAmount; set => TotalAmount = value; }
    public bool IsPaid => PaymentStatus == "Đã thanh toán";
    public string PaymentMethod { get; set; } = "COD";
    public string PaymentStatus { get; set; } = "Chưa thanh toán";

    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public string? BankQrUrl { get; set; }
    public string? MomoQrUrl { get; set; }
    public string TransferContent { get; set; } = string.Empty;

    public List<CheckoutItemViewModel> Items { get; set; } = [];
}