using EMua.Areas.Staff.Models;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services;

// Dùng chung cho Admin và Staff. Không cộng kho ở bước phê duyệt.
public sealed class ImportOrderReceivingService
{
    private readonly EMuaDbContext _db;
    private readonly ILogger<ImportOrderReceivingService> _logger;

    public ImportOrderReceivingService(EMuaDbContext db, ILogger<ImportOrderReceivingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public static DateTime VietnamNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);

    public async Task<ImportOrderReceiveViewModel?> LoadAsync(int id)
    {
        var receipt = await _db.PhieuNhapHangs.AsNoTracking()
            .Include(x => x.MaNguoiTaoNavigation)
            .Include(x => x.MaNguoiDuyetNavigation)
            .Include(x => x.ChiTietPhieuNhapHangs)
                .ThenInclude(x => x.MaBienTheNavigation)
                    .ThenInclude(x => x.MaSanPhamNavigation)
            .SingleOrDefaultAsync(x => x.MaPhieuNhap == id);
        if (receipt == null) return null;
        return new ImportOrderReceiveViewModel
        {
            MaPhieuNhap = receipt.MaPhieuNhap,
            MaChungTu = receipt.MaChungTu,
            NhaCungCap = receipt.NhaCungCap,
            LyDoNhap = receipt.LyDoNhap,
            DienGiai = receipt.DienGiai,
            TrangThai = receipt.TrangThai,
            NgayTao = receipt.NgayTao,
            NguoiTao = receipt.MaNguoiTaoNavigation?.TenNguoiDung ?? "Nhân viên",
            NguoiDuyet = receipt.MaNguoiDuyetNavigation?.TenNguoiDung,
            NgayDuyet = receipt.NgayDuyet,
            TongTienDuKien = receipt.TongTienDuKien,
            Items = receipt.ChiTietPhieuNhapHangs.OrderBy(x => x.MaChiTietPhieuNhap)
                .Select(x => new ImportOrderReceiveItemViewModel
                {
                    MaChiTietPhieuNhap = x.MaChiTietPhieuNhap,
                    MaBienThe = x.MaBienThe,
                    TenSanPham = x.MaBienTheNavigation?.MaSanPhamNavigation?.TenSanPham ?? "Sản phẩm",
                    MauSac = x.MaBienTheNavigation?.MauSac,
                    PhienBan = x.MaBienTheNavigation?.PhienBan,
                    TonKhoHienTai = x.MaBienTheNavigation?.SoLuong ?? 0,
                    SoLuongDeNghi = x.SoLuongNhap,
                    SoLuongThucNhan = x.SoLuongNhap
                }).ToList()
        };
    }

    public async Task<(bool Success, string Message)> ConfirmAsync(ImportOrderReceiveCommand command, int userId)
    {
        if (userId <= 0 || command.MaPhieuNhap <= 0)
            return (false, "Phiên đăng nhập hoặc mã phiếu không hợp lệ.");
        var items = command.Items;
        if (items == null || items.Count == 0 ||
            items.Any(x => x.MaChiTietPhieuNhap <= 0 || x.MaBienThe <= 0 ||
                           x.SoLuongThucNhan < 0 || x.SoLuongThucNhan > 100000) ||
            items.Select(x => x.MaChiTietPhieuNhap).Distinct().Count() != items.Count ||
            !items.Any(x => x.SoLuongThucNhan > 0))
            return (false, "Danh sách kiểm nhận không hợp lệ. Nhập đủ từng dòng, số lượng từ 0 đến 100.000 và tổng phải lớn hơn 0.");
        if (command.GhiChuKiemNhan?.Length > 500)
            return (false, "Ghi chú kiểm nhận tối đa 500 ký tự.");

        if (!await _db.NguoiDungs.AsNoTracking().AnyAsync(x => x.MaNguoiDung == userId))
            return (false, "Tài khoản thực hiện không còn tồn tại. Vui lòng đăng nhập lại.");

        try
        {
            // Cần ExecutionStrategy vì kết nối PostgreSQL đã bật EnableRetryOnFailure.
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync();
                var now = VietnamNow();
                // UPDATE có điều kiện lấy quyền xử lý phiếu. Giao dịch thứ hai phải đợi
                // và sẽ không cập nhật được phiếu đã nhập, kể cả khi Admin/Staff cùng bấm.
                var claimed = await _db.PhieuNhapHangs
                    .Where(x => x.MaPhieuNhap == command.MaPhieuNhap && x.TrangThai == "Đã duyệt")
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.TrangThai, "Đã nhập kho")
                        .SetProperty(x => x.NgayNhapKho, (DateTime?)now)
                        .SetProperty(x => x.MaNguoiNhapKho, (int?)userId));
                if (claimed != 1)
                {
                    await tx.RollbackAsync();
                    return (false, "Phiếu không tồn tại, chưa được duyệt hoặc đã nhập kho. Không cộng tồn lần nữa.");
                }

                var receipt = await _db.PhieuNhapHangs
                    .Include(x => x.ChiTietPhieuNhapHangs)
                    .SingleAsync(x => x.MaPhieuNhap == command.MaPhieuNhap);
                var lines = receipt.ChiTietPhieuNhapHangs;
                var byLine = items.ToDictionary(x => x.MaChiTietPhieuNhap);
                if (lines.Count == 0 || lines.Count != items.Count ||
                    lines.Any(x => !byLine.TryGetValue(x.MaChiTietPhieuNhap, out var posted) || posted.MaBienThe != x.MaBienThe))
                {
                    await tx.RollbackAsync();
                    return (false, "Danh sách mặt hàng không khớp phiếu. Vui lòng tải lại trang kiểm nhận.");
                }
                if (lines.Any(x => byLine[x.MaChiTietPhieuNhap].SoLuongThucNhan != x.SoLuongNhap) &&
                    string.IsNullOrWhiteSpace(command.GhiChuKiemNhan))
                {
                    await tx.RollbackAsync();
                    return (false, "Vui lòng ghi lý do khi thực nhận thiếu hoặc thừa so với phiếu được duyệt.");
                }

                // Gộp theo biến thể để hỗ trợ phiếu cũ có nhiều dòng cùng biến thể.
                // Thứ tự khóa ổn định và UPDATE số lượng trực tiếp tránh ghi đè tồn kho.
                foreach (var group in lines.GroupBy(x => x.MaBienThe).OrderBy(x => x.Key))
                {
                    long received = group.Sum(x => (long)byLine[x.MaChiTietPhieuNhap].SoLuongThucNhan);
                    if (received > int.MaxValue) throw new InvalidOperationException("Số lượng nhập quá lớn.");
                    var qty = (int)received;
                    var changed = await _db.BienTheSanPhams
                        .Where(x => x.MaBienThe == group.Key && x.SoLuong <= int.MaxValue - qty)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(x => x.SoLuong, x => x.SoLuong + qty)
                            .SetProperty(x => x.TrangThai, x =>
                                x.TrangThai == "Hết hàng" && x.SoLuong + qty > 0 ? "Còn hàng" : x.TrangThai));
                    if (changed != 1) throw new InvalidOperationException("Biến thể không tồn tại hoặc số lượng tồn vượt giới hạn.");
                }

                foreach (var line in lines)
                {
                    var qty = byLine[line.MaChiTietPhieuNhap].SoLuongThucNhan;
                    // Schema hiện tại chưa có cột thực nhận: ghi nhận từng dòng trong GhiChu.
                    // Giới hạn theo cột nvarchar/varchar(255), giữ nội dung kiểm nhận ở đầu.
                    line.GhiChu = Limit($"[Thực nhận: {qty}/{line.SoLuongNhap}; lệch {qty - line.SoLuongNhap:+0;-0;0}] {line.GhiChu}", 255);
                }
                var note = command.GhiChuKiemNhan?.Trim();
                receipt.GhiChu = Limit($"[Nhập kho {now:dd/MM/yyyy HH:mm}; người nhận #{userId}] {note} | {receipt.GhiChu}", 500);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, $"Đã nhập kho phiếu [{receipt.MaChungTu}], cộng {items.Sum(x => (long)x.SoLuongThucNhan):N0} sản phẩm theo số lượng thực nhận.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể kiểm nhận phiếu nhập {ReceiptId}", command.MaPhieuNhap);
            _db.ChangeTracker.Clear();
            return (false, "Chưa hoàn tất xác nhận nhập kho. Vui lòng tải lại chi tiết phiếu để kiểm tra trạng thái trước khi thử lại.");
        }
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
