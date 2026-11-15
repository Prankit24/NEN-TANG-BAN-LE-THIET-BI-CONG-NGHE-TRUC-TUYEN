using EMua.Models.Database;
namespace EMua.Services;
public static class InvoiceSnapshotRules
{
    public static bool IsComplete(HoaDon invoice)
    {
        if (!invoice.MaNguoiLap.HasValue || !invoice.NgayLapHoaDon.HasValue ||
            string.IsNullOrWhiteSpace(invoice.HoTenKhachHang) ||
            !invoice.TongTien.HasValue || !invoice.PhiVanChuyen.HasValue ||
            !invoice.TienGiamGia.HasValue || !invoice.TienTruocThue.HasValue ||
            !invoice.TienThueVat.HasValue || !invoice.ChiTietHoaDons.Any()) return false;
        if (invoice.ChiTietHoaDons.Any(x => x.SoLuong <= 0 || x.ThanhTien < 0 || x.TienThueVat < 0 || x.TienThueVat > x.ThanhTien)) return false;
        var gross = invoice.ChiTietHoaDons.Sum(x => x.ThanhTien);
        var vat = invoice.ChiTietHoaDons.Sum(x => x.TienThueVat);
        return invoice.PhiVanChuyen >= 0 && invoice.TienGiamGia >= 0 && invoice.TienGiamGia <= gross &&
            invoice.TienThueVat == vat && invoice.TienTruocThue == gross - vat &&
            invoice.TongTien == gross + invoice.PhiVanChuyen - invoice.TienGiamGia;
    }
}
