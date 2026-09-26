using System.ComponentModel.DataAnnotations;

namespace EMua.Areas.Staff.Models;

public class StaffDashboardViewModel
{
    public int SelectedMonth { get; set; }
    public int SelectedYear { get; set; }
    public decimal Revenue { get; set; }
    public decimal RevenueGrowth { get; set; }
    public int OrderCount { get; set; }
    public decimal OrderGrowth { get; set; }
    public int SoldUnits { get; set; }
    public int NewCustomers { get; set; }
    public int LowStockCount { get; set; }
    public List<decimal> RevenueByMonth { get; set; } = [];
    public List<StockAlertItem> LowStockItems { get; set; } = [];
    public List<DashboardOrderItem> RecentOrders { get; set; } = [];
}

public class StockAlertItem
{
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
}

public class DashboardOrderItem
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string ProductSummary { get; set; } = string.Empty;
    public DateTime? OrderedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ReportViewModel
{
    public decimal Revenue { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int TotalOrders { get; set; }
    public int SuccessfulOrders { get; set; }
    public int ReturnedOrders { get; set; }
    public int PendingOrders { get; set; }
    public int FavoriteCount { get; set; }
    public int LowStockCount { get; set; }
    public decimal SuccessRate { get; set; }
    public List<ReportDailyRevenueItem> DailyRevenue { get; set; } = [];
    public List<ReportOrderStatusItem> OrderStatuses { get; set; } = [];
    public List<ReportProductItem> BestSellingProducts { get; set; } = [];
    public List<ReportProductItem> FavoriteProducts { get; set; } = [];
}

public class ReportDailyRevenueItem
{
    public int Day { get; set; }
    public decimal Revenue { get; set; }
}

public class ReportOrderStatusItem
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ReportProductItem
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
}

public class ProductCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(100)]
    [Display(Name = "Tên sản phẩm")]
    public string TenSanPham { get; set; } = string.Empty;

    [Display(Name = "Danh mục")]
    public int? MaDanhMuc { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn thương hiệu.")]
    [Display(Name = "Thương hiệu")]
    public int? MaThuongHieu { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Giá gốc không hợp lệ.")]
    [Display(Name = "Giá gốc")]
    public decimal? GiaGoc { get; set; }

    [StringLength(50)]
    [Display(Name = "Bảo hành")]
    public string? BaoHanh { get; set; }

    [StringLength(255)]
    [Display(Name = "Mô tả")]
    public string? MoTa { get; set; }

    [Display(Name = "Thông số kỹ thuật")]
    public string? ThongSoKyThuat { get; set; }
    [Display(Name = "Ảnh sản phẩm")]
    public IFormFile? ImageFile { get; set; }

    public string? CurrentImageUrl { get; set; }

    public List<ProductVariantQuantityViewModel> BienThe { get; set; } = [];
}

public class ProductVariantQuantityViewModel
{
    public int MaBienThe { get; set; }
    public string? MauSac { get; set; }
    public string? PhienBan { get; set; }
    [Range(0.01, double.MaxValue, ErrorMessage = "Giá biến thể phải lớn hơn 0.")]
    [Display(Name = "Giá bán")]
    public decimal Gia { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số lượng không được âm.")]
    [Display(Name = "Số lượng")]
    public int SoLuong { get; set; }
}

public class PromotionCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã khuyến mãi.")]
    [StringLength(50)]
    public string MaCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên khuyến mãi.")]
    [StringLength(150)]
    public string TenKhuyenMai { get; set; } = string.Empty;

    [Required]
    public string LoaiGiamGia { get; set; } = "Phần trăm";

    [Range(0.01, double.MaxValue, ErrorMessage = "Giá trị giảm phải lớn hơn 0.")]
    public decimal GiaTriGiam { get; set; }

    public decimal? GiaTriDonHangToiThieu { get; set; }
    public int? SoLuong { get; set; }
    public DateTime? NgayBatDau { get; set; }
    public DateTime? NgayKetThuc { get; set; }
    public string? MoTa { get; set; }
}

// ViewModel dùng cho form cập nhật trạng thái/giao nhận (POST từ trang theo dõi đơn hàng)
public class DeliveryUpdateViewModel
{
    public int OrderId { get; set; }
    public string? OrderCode { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn trạng thái mới.")]
    public string NewStatus { get; set; } = string.Empty;

    public string? ShippingProvider { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Note { get; set; }

    // Ngày dự kiến giao hàng, nhập khi đơn đang được đóng gói/vận chuyển
    [Display(Name = "Ngày dự kiến giao hàng")]
    public DateTime? EstimatedDeliveryDate { get; set; }

    // Ảnh xác nhận đã giao, bắt buộc khi NewStatus = "Đã giao hàng"
    [Display(Name = "Ảnh xác nhận đã giao")]
    public IFormFile? DeliveryProofImage { get; set; }
}

// ViewModel lịch sử vận chuyển
public class ShippingHistoryViewModel
{
    public int HistoryId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string LocationOrNote { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Ảnh đính kèm tại mốc lịch sử này (vd: ảnh xác nhận đã giao)
    public string? ImageUrl { get; set; }
}

// ViewModel theo dõi tiến trình
public class OrderTrackingViewModel
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime OrderDate { get; set; }

    // Ngày dự kiến giao hàng, hiển thị trên banner ETA khi đơn chưa hoàn tất
    public DateTime? EstimatedDeliveryDate { get; set; }

    // Ảnh xác nhận đã giao hàng (khi CurrentStatus = "Đã giao hàng")
    public string? DeliveryProofImageUrl { get; set; }

    public List<ShippingHistoryViewModel> TrackingLogs { get; set; } = new();
}