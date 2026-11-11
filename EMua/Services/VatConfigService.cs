using System;

namespace EMua.Services;

public class VatRateRule
{
    public decimal RatePercent { get; set; }
    public string Description { get; set; } = string.Empty;
    public string LegalBasis { get; set; } = string.Empty;
}

public static class VatConfigService
{
    // Căn cứ pháp lý:
    // - Luật Thuế GTGT số 48/2024/QH15.
    // - Nghị quyết 204/2025/QH15 ngày 17/06/2025.
    // - Nghị định 174/2025/NĐ-CP ngày 30/06/2025.
    // Thời hạn giảm thuế GTGT (10% xuống 8%): 01/07/2025 đến hết 31/12/2026.
    public static readonly DateTime VatReductionStartDate = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime VatReductionEndDate = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    /// <summary>
    /// Kiểm tra xem thời điểm hóa đơn có nằm trong kỳ giảm thuế GTGT theo NQ 204/2025/QH15 hay không.
    /// </summary>
    public static bool IsInVatReductionPeriod(DateTime invoiceDate)
    {
        var d = invoiceDate.Kind == DateTimeKind.Utc ? invoiceDate : invoiceDate.ToUniversalTime();
        return d >= VatReductionStartDate && d <= VatReductionEndDate;
    }

    /// <summary>
    /// Xác định thuế suất VAT áp dụng cho sản phẩm dựa vào phân loại ngành hàng, tên sản phẩm và ngày lập hóa đơn.
    /// Theo Nghị định 174/2025/NĐ-CP & NQ 204/2025/QH15:
    /// - Thiết bị viễn thông, CNTT chủ lực (điện thoại thông minh, laptop, máy tính để bàn, máy tính bảng, máy chơi game) 
    ///   nằm trong nhóm sản phẩm công nghệ thông tin theo Luật CNTT, giữ nguyên mức thuế suất 10% (không thuộc diện giảm xuống 8%).
    /// - Nhóm phụ kiện tiêu dùng phổ thông không phải thiết bị viễn thông/CNTT cốt lõi (bao da, ốp lưng, giá đỡ, dây đeo, túi chống sốc)
    ///   thuộc diện được áp dụng thuế suất ưu đãi 8% trong giai đoạn 01/07/2025 - 31/12/2026.
    /// </summary>
    public static VatRateRule DetermineVatRate(string? categoryName, string? productName, DateTime invoiceDate)
    {
        var cat = (categoryName ?? "").Trim().ToLowerInvariant();
        var prod = (productName ?? "").Trim().ToLowerInvariant();

        // 1. Phụ kiện bảo vệ, thời trang (ốp lưng, bao da, túi, giá đỡ, dây đeo...) -> Giảm còn 8% trong kỳ hiệu lực
        bool isGeneralAccessory = cat.Contains("phụ kiện") || cat.Contains("ốp") || cat.Contains("bao da") || cat.Contains("dây đeo")
            || prod.Contains("ốp lưng") || prod.Contains("bao da") || prod.Contains("giá đỡ") || prod.Contains("túi chống sốc") || prod.Contains("dây đeo");

        // Tuy nhiên các phụ kiện điện tử/viễn thông thông minh (tai nghe bluetooth, sạc nhanh, củ sạc, cáp sạc) vẫn theo thuế suất thiết bị số 10%
        bool isElectronicAccessory = prod.Contains("tai nghe") || prod.Contains("củ sạc") || prod.Contains("cáp sạc") || prod.Contains("sạc dự phòng")
            || prod.Contains("chuột") || prod.Contains("bàn phím") || prod.Contains("usb") || prod.Contains("thẻ nhớ") || prod.Contains("ổ cứng");

        if (isGeneralAccessory && !isElectronicAccessory)
        {
            if (IsInVatReductionPeriod(invoiceDate))
            {
                return new VatRateRule
                {
                    RatePercent = 8.0m,
                    Description = "Thuế GTGT 8% (Áp dụng theo NQ 204/2025/QH15 & NĐ 174/2025/NĐ-CP)",
                    LegalBasis = "NQ 204/2025/QH15 & NĐ 174/2025/NĐ-CP"
                };
            }
        }

        // 2. Điện thoại, Laptop, Máy tính, Máy tính bảng, Thiết bị viễn thông & Phụ kiện công nghệ: 10%
        return new VatRateRule
        {
            RatePercent = 10.0m,
            Description = "Thuế GTGT 10% (Luật Thuế GTGT 48/2024/QH15)",
            LegalBasis = "Luật Thuế GTGT 48/2024/QH15"
        };
    }

    /// <summary>
    /// Tách giá chưa thuế từ giá đã bao gồm VAT (Quy chế TMĐT bán lẻ: Giá niêm yết đã bao gồm VAT).
    /// DonGiaChuaThue = DonGiaGoc / (1 + ThueSuat/100)
    /// Làm tròn 2 chữ số thập phân cho đơn giá chưa thuế.
    /// </summary>
    public static decimal CalculateNetPrice(decimal grossPrice, decimal vatRatePercent)
    {
        if (vatRatePercent <= 0) return grossPrice;
        return Math.Round(grossPrice / (1.0m + (vatRatePercent / 100.0m)), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Tính tiền thuế VAT cho một dòng mặt hàng:
    /// TienThueVat = ThanhTien - (ThanhTien / (1 + ThueSuat/100))
    /// Đảm bảo tính nhất quán không bao giờ cộng thuế 2 lần.
    /// </summary>
    public static decimal CalculateVatAmount(decimal grossTotal, decimal vatRatePercent)
    {
        if (vatRatePercent <= 0) return 0m;
        var netTotal = Math.Round(grossTotal / (1.0m + (vatRatePercent / 100.0m)), 0, MidpointRounding.AwayFromZero);
        return grossTotal - netTotal;
    }
}
