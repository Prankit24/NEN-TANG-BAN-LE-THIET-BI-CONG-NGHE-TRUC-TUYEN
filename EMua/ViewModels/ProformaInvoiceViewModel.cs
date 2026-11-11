using System;
using System.Collections.Generic;

namespace EMua.ViewModels;

public class ProformaInvoiceItemViewModel
{
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal GrossUnitPrice { get; set; }
    public decimal NetUnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class ProformaInvoiceViewModel
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
    public string OrderStatus { get; set; } = string.Empty;

    // Khách hàng / Người nhận
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;

    // Thanh toán & Vận chuyển
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ShippingMethod { get; set; } = string.Empty;
    public string? Note { get; set; }

    // Tài chính
    public decimal ItemsGrossTotal { get; set; }
    public decimal ItemsNetTotal { get; set; }
    public decimal EstimatedVatTotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal VoucherDiscount { get; set; }
    public string? VoucherCode { get; set; }
    public decimal FinalTotal { get; set; }

    // Hóa đơn chính thức (nếu đã được Staff lập)
    public int? OfficialInvoiceId { get; set; }

    public List<ProformaInvoiceItemViewModel> Items { get; set; } = new();
}
