using EMua.Models.Database;

namespace EMua.Models.ViewModels
{
    public class HomeViewModel
    {
        public List<SanPham> FlashSale { get; set; } = new();

        public List<SanPham> SanPhamNoiBat { get; set; } = new();

        public List<SanPham> SanPhamMoi { get; set; } = new();

        public List<SanPham> BestChoice { get; set; } = new();

        public List<DanhMucSanPham> DanhMucs { get; set; } = new();

        public List<ThuongHieu> ThuongHieus { get; set; } = new();
    }
}