using System;
using System.Collections.Generic;
using EMua.Models.Database;

namespace EMua.Areas.Staff.Models;

public class DailyReportOrderPendingInvoiceItem
{
    public int MaDonHang { get; set; }
    public DateTime? NgayDat { get; set; }
    public DateTime? NgayBanGiao { get; set; }
    public string KhachHang { get; set; } = string.Empty;
    public string SoDienThoai { get; set; } = string.Empty;
    public decimal TongTien { get; set; }
    public string TrangThaiDonHang { get; set; } = string.Empty;
    public string PhuongThucThanhToan { get; set; } = string.Empty;
    public string TrangThaiThanhToan { get; set; } = string.Empty;
}

public class DailyInvoiceReportViewModel
{
    public DateTime SelectedDate { get; set; } = DateTime.Today;

    // 1. Chỉ tiêu đơn hàng phát sinh trong ngày
    public int TotalOrdersCreatedToday { get; set; }

    // 2. Chỉ tiêu đơn hàng đã bàn giao (giao thành công) trong ngày
    public int TotalOrdersDeliveredToday { get; set; }

    // 3. Chỉ tiêu số hóa đơn đã lập trong ngày
    public int TotalInvoicesIssuedToday { get; set; }

    // 4. Số đơn đã bàn giao nhưng chưa lập hóa đơn (Cần lập HĐ theo luật)
    public int TotalDeliveredPendingInvoicesCount { get; set; }
    public List<DailyReportOrderPendingInvoiceItem> DeliveredPendingInvoiceOrders { get; set; } = new();

    // 5. Tổng hợp tài chính từ các hóa đơn phát sinh trong ngày
    public decimal TotalNetRevenue { get; set; }
    public decimal TotalVatAmount { get; set; }
    public decimal TotalShippingFee { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalGrossRevenue { get; set; }

    // Phân rã VAT theo từng mức thuế suất
    public Dictionary<decimal, decimal> VatAmountByRate { get; set; } = new();

    // 6. Tình trạng tiền thanh toán của các hóa đơn trong ngày
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalUnpaidAmount { get; set; }

    // 7. Danh sách hóa đơn phát sinh trong ngày
    public List<HoaDon> InvoicesToday { get; set; } = new();
}
