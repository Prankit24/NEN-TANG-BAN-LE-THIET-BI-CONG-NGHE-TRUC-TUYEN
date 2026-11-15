namespace EMua.Services;

// Quy ước thống nhất: chiết khấu từng dòng tính trên một đơn vị sau VAT.
// Đây là phép tính dự kiến cho phiếu đề nghị, không thay thế hóa đơn thuế của NCC.
public static class ImportOrderAmounts
{
    public const decimal MaximumTotal = 1_000_000_000_000m;
    public static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static ImportLineAmounts CalculateLine(int quantity, decimal netUnit,
        decimal vatRate, decimal discountUnit)
    {
        if (quantity < 1 || quantity > 100000)
            throw new ArgumentException("Số lượng nhập phải từ 1 đến 100.000.");
        if (netUnit < 0 || netUnit > 1_000_000_000m)
            throw new ArgumentException("Giá trước VAT phải từ 0 đến 1 tỷ đồng.");
        if (vatRate != 0m && vatRate != 5m && vatRate != 8m && vatRate != 10m)
            throw new ArgumentException("Thuế suất phải là 0%, 5%, 8% hoặc 10%.");
        var net = Money(netUnit);
        var vat = Money(net * vatRate / 100m);
        var gross = net + vat;
        if (discountUnit < 0 || discountUnit > gross)
            throw new ArgumentException("Chiết khấu mỗi sản phẩm phải từ 0 đến giá sau VAT.");
        var discount = Money(discountUnit);
        return new ImportLineAmounts(net, vat, gross, discount,
            net * quantity, vat * quantity, discount * quantity,
            (gross - discount) * quantity);
    }

    public static decimal CalculateTotal(IEnumerable<ImportLineAmounts> lines,
        decimal shipping, decimal other, decimal totalDiscount)
    {
        if (shipping < 0 || other < 0 || totalDiscount < 0 ||
            shipping > 1_000_000_000m || other > 1_000_000_000m || totalDiscount > MaximumTotal)
            throw new ArgumentException("Phí và chiết khấu không hợp lệ hoặc vượt giới hạn.");
        var beforeDiscount = lines.Sum(x => x.Total) + Money(shipping) + Money(other);
        if (Money(totalDiscount) > beforeDiscount)
            throw new ArgumentException("Chiết khấu toàn phiếu không được vượt tổng tiền hàng và phụ phí.");
        var total = beforeDiscount - Money(totalDiscount);
        if (total > MaximumTotal)
            throw new ArgumentException("Tổng giá trị một phiếu tối đa 1.000 tỷ đồng.");
        return total;
    }
}

public sealed record ImportLineAmounts(decimal NetUnit, decimal VatUnit,
    decimal GrossUnit, decimal DiscountUnit, decimal Net, decimal Vat,
    decimal Discount, decimal Total);
