using System.Security.Cryptography;
using System.Text;
using EMua.Data;
using EMua.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace EMua.Services;

public sealed record OrderChangeResult(bool Success, string Message, OrderTotals? Totals = null,
	string? Status = null, bool ProofUsed = false, bool Uncertain = false);

public sealed class OrderWorkflowService
{
	private readonly EMuaDbContext _db;
	private readonly ILogger<OrderWorkflowService> _logger;
	private static readonly string[] StaffRoles = { "Admin", "Quản trị viên", "QuanTriVien", "NhanVien", "Nhân viên" };
	private static readonly string[] UnpaidStates = { "Chưa thanh toán", "Chờ thanh toán", "Chờ xác nhận" };

	public OrderWorkflowService(EMuaDbContext db, ILogger<OrderWorkflowService> logger)
	{ _db = db; _logger = logger; }

	public static string? EditBlockedReason(DonHang order)
	{
		if (!OrderRules.IsPending(order.TrangThaiDonHang)) return "Đơn đã được xử lý, không còn cho phép sửa số lượng.";
		if (order.ThanhToans.Count != 1) return "Đơn thiếu hoặc có nhiều bản ghi thanh toán. Cần nhân viên đối soát trước khi sửa.";
		if (order.ThanhToans.Any(p => !UnpaidStates.Contains(p.TrangThai)))
			return "Đơn đã báo chuyển tiền, đã thanh toán hoặc đang đối soát nên không được tự sửa số lượng.";
		if (order.HoaDon != null && (order.HoaDon.MaNguoiLap.HasValue || order.HoaDon.ChiTietHoaDons.Any()))
			return "Đơn đã lập hóa đơn. Cần nhân viên xử lý chứng từ trước khi điều chỉnh.";
		try { ValidateExistingPricing(order); }
		catch (ArgumentException ex) { return ex.Message; }
		return null;
	}

	public static void ValidateExistingPricing(DonHang order)
	{
		if (!OrderRules.KnownShipping(order.PhuongThucVanChuyen))
			throw new ArgumentException("Đơn cũ chưa xác định được cách tính phí giao hàng. Cần đối chiếu trước khi sửa.");
		var subtotal = order.ChiTietDonHangs.Sum(x => x.DonGia * x.SoLuong);
		if (order.ChiTietDonHangs.Any(x => x.SoLuong <= 0 || x.DonGia <= 0 || OrderRules.Money(x.DonGia * x.SoLuong) != x.ThanhTien))
			throw new ArgumentException("Chi tiết tiền hàng cũ không khớp. Cần nhân viên đối chiếu trước khi sửa.");
		if (!order.GiaTriKhuyenMaiDaChot.HasValue || !order.DonToiThieuKhuyenMaiDaChot.HasValue ||
			!order.PhiVanChuyenDaChot.HasValue || !order.TienGiamGiaDaChot.HasValue)
		{
			// Chỉ phục hồi đơn không dùng ưu đãi khi phép tính cũ khớp chính xác.
			// Không lấy quy tắc khuyến mãi hiện tại để đoán điều kiện của đơn lịch sử.
			if (order.MaKhuyenMai.HasValue)
				throw new ArgumentException("Đơn cũ chưa lưu điều kiện giảm giá đã chốt. Vui lòng nhờ nhân viên đối chiếu hoặc hủy và đặt lại.");
			var old = OrderRules.Calculate(subtotal, OrderRules.IsExpress(order.PhuongThucVanChuyen), null, 0, 0);
			if (order.TongTien != old.Total) throw new ArgumentException("Tổng đơn cũ không khớp phí giao hàng. Cần đối chiếu trước khi sửa.");
			return;
		}
		var totals = OrderRules.Calculate(subtotal, OrderRules.IsExpress(order.PhuongThucVanChuyen), order.LoaiKhuyenMaiDaChot,
			order.GiaTriKhuyenMaiDaChot.Value, order.DonToiThieuKhuyenMaiDaChot.Value);
		if (order.TongTien != totals.Total || order.PhiVanChuyenDaChot != totals.Shipping || order.TienGiamGiaDaChot != totals.Discount)
			throw new ArgumentException("Số tiền đã chốt của đơn không khớp chi tiết. Cần đối soát trước khi sửa.");
	}

	private async Task<OrderChangeResult> RunAsync(int orderId, int userId, bool staff,
		Func<DonHang, Task<OrderChangeResult>> action)
	{
		if (orderId <= 0 || userId <= 0) return new(false, "Mã đơn hoặc phiên đăng nhập không hợp lệ.");
		try
		{
			return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
			{
				_db.ChangeTracker.Clear();
				await using var tx = await _db.Database.BeginTransactionAsync();
				var allowed = await _db.NguoiDungs.AsNoTracking().AnyAsync(u => u.MaNguoiDung == userId && u.TrangThai &&
					(!staff || StaffRoles.Contains(u.MaQuyenNavigation.TenQuyen)));
				if (!allowed) return new OrderChangeResult(false, "Tài khoản đã bị khóa hoặc không còn quyền thực hiện.");
				// Khóa chính bản ghi đơn tại database, dùng chung cho sửa/hủy/chuyển trạng thái/đối soát.
				if (staff)
					await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT madonhang FROM public.donhang WHERE madonhang = {orderId} FOR UPDATE");
				else
					await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT madonhang FROM public.donhang WHERE madonhang = {orderId} AND manguoidung = {userId} FOR UPDATE");
				var order = await _db.DonHangs.Include(x => x.ChiTietDonHangs).Include(x => x.ThanhToans)
					.Include(x => x.HoaDon).ThenInclude(x => x!.ChiTietHoaDons)
					.AsSplitQuery().FirstOrDefaultAsync(x => x.MaDonHang == orderId && (staff || x.MaNguoiDung == userId));
				if (order == null) return new OrderChangeResult(false, "Không tìm thấy đơn hàng hoặc bạn không có quyền thao tác.");
				var result = await action(order);
				if (!result.Success) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); return result; }
				await _db.SaveChangesAsync();
				await tx.CommitAsync();
				return result;
			});
		}
		catch (ArgumentException ex) { _db.ChangeTracker.Clear(); return new(false, ex.Message); }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Không hoàn tất thay đổi đơn {OrderId}", orderId);
			_db.ChangeTracker.Clear();
			return new(false, "Chưa xác định được kết quả cập nhật. Vui lòng tải lại đơn để kiểm tra trước khi thử lại.", Uncertain: true);
		}
	}

	private void Log(DonHang order, string status, string description, int userId, string? image = null)
	{
		_db.LichSuVanChuyens.Add(new LichSuVanChuyen
		{
			MaDonHang = order.MaDonHang,
			TrangThai = status,
			MoTa = Limit($"{description} Người thực hiện #{userId}.", 500),
			ThoiGian = OrderRules.VietnamNow(),
			HinhAnhNguoiNhan = image
		});
	}
	private static string Limit(string s, int max) => s.Length <= max ? s : s[..max];

	public Task<OrderChangeResult> EditItemAsync(int id, int userId, int variantId, int quantity) => RunAsync(id, userId, false, async order =>
	{
		if (variantId <= 0 || quantity < 0 || quantity > 100000) return new(false, "Số lượng phải là số nguyên từ 0 đến 100.000.");
		var blocked = EditBlockedReason(order); if (blocked != null) return new(false, blocked);
		var matches = order.ChiTietDonHangs.Where(x => x.MaBienThe == variantId).ToList();
		if (matches.Count != 1) return new(false, "Không xác định được một dòng sản phẩm duy nhất trong đơn. Vui lòng nhờ nhân viên kiểm tra.");
		var line = matches[0]; var oldQty = line.SoLuong;
		if (quantity == oldQty)
			return new(true, "Số lượng không thay đổi.", new(order.ChiTietDonHangs.Sum(x => x.ThanhTien), order.PhiVanChuyenDaChot ?? OrderRules.ShippingFee(order.ChiTietDonHangs.Sum(x => x.ThanhTien), OrderRules.IsExpress(order.PhuongThucVanChuyen)), order.TienGiamGiaDaChot ?? 0m, order.TongTien ?? 0m), OrderRules.Normalize(order.TrangThaiDonHang));
		if (quantity == 0 && order.ChiTietDonHangs.Count <= 1) return new(false, "Đơn phải có ít nhất một sản phẩm. Hãy dùng Hủy đơn nếu muốn hủy toàn bộ.");
		var newSubtotal = order.ChiTietDonHangs.Where(x => x != line).Sum(x => x.ThanhTien) + line.DonGia * quantity;
		var totals = OrderRules.Calculate(newSubtotal, OrderRules.IsExpress(order.PhuongThucVanChuyen), order.LoaiKhuyenMaiDaChot,
			order.GiaTriKhuyenMaiDaChot ?? 0m, order.DonToiThieuKhuyenMaiDaChot ?? 0m);
		var delta = quantity - oldQty;
		int changed;
		if (delta > 0)
			changed = await _db.BienTheSanPhams.Where(v => v.MaBienThe == variantId && v.SoLuong >= delta &&
				v.TrangThai == "Còn hàng" && v.Gia > 0 && v.MaSanPhamNavigation.TrangThaiSanPham != "Ẩn")
				.ExecuteUpdateAsync(set => set.SetProperty(v => v.SoLuong, v => v.SoLuong - delta)
					.SetProperty(v => v.TrangThai, v => v.SoLuong - delta == 0 ? "Hết hàng" : v.TrangThai));
		else
		{
			var restore = -delta;
			changed = await _db.BienTheSanPhams.Where(v => v.MaBienThe == variantId && v.SoLuong <= int.MaxValue - restore)
				.ExecuteUpdateAsync(set => set.SetProperty(v => v.SoLuong, v => v.SoLuong + restore)
					.SetProperty(v => v.TrangThai, v => v.TrangThai == "Hết hàng" && v.SoLuong + restore > 0 ? "Còn hàng" : v.TrangThai));
		}
		if (changed != 1) return new(false, "Sản phẩm không còn bán, không đủ tồn kho hoặc số lượng tồn vượt giới hạn. Chưa thay đổi đơn.");
		if (quantity == 0) { order.ChiTietDonHangs.Remove(line); _db.ChiTietDonHangs.Remove(line); }
		else { line.SoLuong = quantity; line.ThanhTien = OrderRules.Money(line.DonGia * quantity); }
		order.PhiVanChuyenDaChot = totals.Shipping; order.TienGiamGiaDaChot = totals.Discount;
		order.GiaTriKhuyenMaiDaChot ??= 0m; order.DonToiThieuKhuyenMaiDaChot ??= 0m;
		order.TongTien = totals.Total; order.TrangThaiDonHang = OrderRules.Normalize(order.TrangThaiDonHang);
		foreach (var p in order.ThanhToans) p.SoTien = totals.Total;
		if (order.HoaDon != null)
		{
			order.HoaDon.TongTien = totals.Total; order.HoaDon.PhiVanChuyen = totals.Shipping;
			order.HoaDon.TienGiamGia = totals.Discount;
		}
		Log(order, order.TrangThaiDonHang, $"Đổi biến thể #{variantId} từ {oldQty} thành {quantity}; tiền hàng {totals.Subtotal:N0}, phí giao {totals.Shipping:N0}, giảm {totals.Discount:N0}, tổng {totals.Total:N0}đ.", userId);
		return new(true, "Đã cập nhật số lượng và tính lại phí giao, giảm giá, tổng thanh toán.", totals, order.TrangThaiDonHang);
	});

	public Task<OrderChangeResult> CancelAsync(int id, int userId, bool staff = false) => RunAsync(id, userId, staff, order => CancelLockedAsync(order, userId, staff));
	private async Task<OrderChangeResult> CancelLockedAsync(DonHang order, int userId, bool staff)
	{
		if (OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.Cancelled) return new(true, "Đơn đã hủy trước đó; không hoàn tồn thêm lần nữa.");
		// Khách chỉ hủy được đơn chưa xử lý. Đơn giao thất bại (hàng đã quay về shop) chỉ nhân viên mới hủy/hoàn kho được.
		var cancellable = OrderRules.IsPending(order.TrangThaiDonHang) ||
			(staff && OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.DeliveryFailed);
		if (!cancellable) return new(false, "Chỉ hủy được đơn chưa chuyển sang xử lý/giao hàng. Đơn đang giao thất bại cần nhân viên xử lý.");
		if (!order.ChiTietDonHangs.Any() || order.ChiTietDonHangs.Any(x => x.SoLuong <= 0)) return new(false, "Chi tiết đơn không hợp lệ. Cần nhân viên đối chiếu.");
		if (order.MaKhuyenMai.HasValue)
		{
			var coupon = await _db.KhuyenMais.AsNoTracking().FirstOrDefaultAsync(x => x.MaKhuyenMai == order.MaKhuyenMai);
			if (coupon != null && coupon.SoLuong.HasValue)
			{
				var restored = await _db.KhuyenMais.Where(x => x.MaKhuyenMai == coupon.MaKhuyenMai && x.SoLuong < int.MaxValue)
					.ExecuteUpdateAsync(set => set.SetProperty(x => x.SoLuong, x => x.SoLuong + 1));
				if (restored != 1) return new(false, "Không thể hoàn lượt khuyến mãi; thao tác hủy được hoàn tác.");
			}
		}
		foreach (var g in order.ChiTietDonHangs.GroupBy(x => x.MaBienThe).OrderBy(x => x.Key))
		{
			var amount = g.Sum(x => (long)x.SoLuong);
			if (amount > int.MaxValue) return new(false, "Số lượng hoàn tồn vượt giới hạn.");
			var qty = (int)amount;
			var changed = await _db.BienTheSanPhams.Where(x => x.MaBienThe == g.Key && x.SoLuong <= int.MaxValue - qty)
				.ExecuteUpdateAsync(set => set.SetProperty(x => x.SoLuong, x => x.SoLuong + qty)
					.SetProperty(x => x.TrangThai, x => x.TrangThai == "Hết hàng" && x.SoLuong + qty > 0 ? "Còn hàng" : x.TrangThai));
			if (changed != 1) return new(false, "Không thể hoàn tồn một mặt hàng; toàn bộ thao tác hủy được hoàn tác.");
		}
		var needsRefund = order.ThanhToans.Any(x => OrderRules.IsPaid(x.TrangThai) && x.SoTien > 0);
		var needsReconcile = order.ThanhToans.Any(x => x.TrangThai == OrderRules.PaymentReported);
		foreach (var p in order.ThanhToans)
			p.TrangThai = OrderRules.IsPaid(p.TrangThai) && p.SoTien > 0 ? OrderRules.RefundPending :
				p.TrangThai == OrderRules.PaymentReported ? OrderRules.CancelledReconcile : OrderRules.Cancelled;
		order.TrangThaiDonHang = OrderRules.Cancelled;
		if (order.HoaDon != null) order.HoaDon.TrangThaiHoaDon = needsRefund ? OrderRules.RefundPending : OrderRules.Cancelled;
		var note = needsRefund ? "Tiền đã thu cần nhân viên hoàn tiền riêng; chưa xác nhận đã hoàn tiền." :
			needsReconcile ? "Khách đã báo chuyển tiền; cần nhân viên đối soát khoản nhận." : "Chưa ghi nhận đã thu tiền.";
		Log(order, OrderRules.Cancelled, $"Đã hủy đơn, hoàn tồn và lượt mã khuyến mãi. {note}", userId);
		return new(true, $"Đã hủy đơn và hoàn tồn. {note}", Status: OrderRules.Cancelled);
	}

	public Task<OrderChangeResult> ReportPaymentAsync(int id, int userId) => RunAsync(id, userId, false, order =>
	{
		if (order.ThanhToans.Count != 1) return Task.FromResult(new OrderChangeResult(false, "Cần nhân viên đối chiếu thông tin thanh toán."));
		var p = order.ThanhToans.Single();
		if (OrderRules.IsCod(p.PhuongThuc)) return Task.FromResult(new OrderChangeResult(false, "Đơn COD thanh toán khi nhận hàng, không cần báo chuyển tiền."));
		if (OrderRules.IsPaid(p.TrangThai)) return Task.FromResult(new OrderChangeResult(true, "Nhân viên đã xác nhận thanh toán của đơn này."));
		if (!OrderRules.IsPending(order.TrangThaiDonHang)) return Task.FromResult(new OrderChangeResult(false, "Đơn không còn ở trạng thái cho phép báo chuyển tiền."));
		if (p.TrangThai != OrderRules.PaymentReported && !UnpaidStates.Contains(p.TrangThai))
			return Task.FromResult(new OrderChangeResult(false, "Khoản tiền đang ở trạng thái cần đối soát riêng. Vui lòng liên hệ nhân viên."));
		if (p.TrangThai != OrderRules.PaymentReported)
		{ p.TrangThai = OrderRules.PaymentReported; Log(order, OrderRules.PaymentReported, "Khách báo đã chuyển tiền; chưa xác nhận tiền đã về tài khoản/ví.", userId); }
		return Task.FromResult(new OrderChangeResult(true, "Đã gửi thông báo. Đơn chỉ được xác nhận đã thanh toán sau khi nhân viên đối soát."));
	});

	public Task<OrderChangeResult> ConfirmPaymentAsync(int id, int userId, decimal amount, string? reference, bool verified) => RunAsync(id, userId, true, async order =>
	{
		reference = reference?.Trim().ToUpperInvariant();
		if (!verified || string.IsNullOrWhiteSpace(reference) || reference.Length < 3 || reference.Length > 100 || reference.Any(char.IsControl))
			return new(false, "Cần đối soát thực tế, nhập mã giao dịch hợp lệ và đánh dấu xác nhận đã nhận tiền.");
		if (order.ThanhToans.Count != 1) return new(false, "Đơn có thông tin thanh toán không đơn nhất. Cần đối soát riêng.");
		var p = order.ThanhToans.Single();
		if (OrderRules.IsPaid(p.TrangThai)) return new(true, "Đơn đã được xác nhận thanh toán trước đó.");
		if (p.TrangThai == OrderRules.RefundPending) return new(true, "Khoản tiền đã được ghi nhận và đang chờ hoàn; không ghi nhận thêm.");
		if (OrderRules.IsCod(p.PhuongThuc)) return new(false, "Với COD, xác nhận thu tiền tại bước hoàn tất giao hàng kèm ảnh giao nhận.");
		if (!OrderRules.IsPending(order.TrangThaiDonHang) && OrderRules.Normalize(order.TrangThaiDonHang) != OrderRules.Cancelled)
			return new(false, "Trạng thái đơn không phù hợp để xác nhận khoản thanh toán này.");
		if (amount <= 0 || amount != p.SoTien || amount != order.TongTien)
			return new(false, "Số tiền đối soát phải khớp chính xác số tiền cần thanh toán của đơn.");
		var lockKey = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("EMUA-PAYMENT:" + reference)), 0);
		await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})");
		if (await _db.ThanhToans.AsNoTracking().AnyAsync(x => x.MaDonHang != id && x.MaGiaoDich == reference &&
			(x.TrangThai == OrderRules.Paid || x.TrangThai == "Thành công" || x.TrangThai == OrderRules.RefundPending)))
			return new(false, "Mã giao dịch đã được đối soát cho đơn khác. Không dùng lại một khoản tiền cho hai đơn.");
		var cancelled = OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.Cancelled;
		p.TrangThai = cancelled ? OrderRules.RefundPending : OrderRules.Paid;
		p.MaGiaoDich = reference; p.NgayThanhToan = OrderRules.VietnamNow();
		if (!cancelled) order.TrangThaiDonHang = OrderRules.Pending;
		if (order.HoaDon != null) order.HoaDon.TrangThaiHoaDon = p.TrangThai;
		Log(order, p.TrangThai, $"Nhân viên xác nhận nhận {amount:N0}đ, giao dịch {reference}." +
			(cancelled ? " Đơn đã hủy: cần xử lý hoàn tiền, không mở lại đơn hoặc trừ tồn." : ""), userId);
		return new(true, cancelled ? "Đã ghi nhận tiền về cho đơn đã hủy. Cần xử lý hoàn tiền riêng." : "Đã đối soát thanh toán. Đơn chuyển sang chờ xử lý.");
	});

	public Task<OrderChangeResult> ChangeStateAsync(int id, int userId, string? status, string? proof = null,
		bool codCollected = false, bool returnedReceived = false, DateTime? etaUtc = null, string? note = null) => RunAsync(id, userId, true, async order =>
		{
			var current = OrderRules.Normalize(order.TrangThaiDonHang); var target = OrderRules.Normalize(status);
			var p = order.ThanhToans.OrderByDescending(x => x.NgayTao).ThenByDescending(x => x.MaThanhToan).FirstOrDefault();
			if (note?.Length > 200) return new(false, "Ghi chú giao nhận tối đa 200 ký tự.");

			var isCompletedState = current == OrderRules.Completed;

			if (current == target)
			{
				if (isCompletedState)
				{
					// Khóa chỉnh sửa ngày giao hàng khi đơn đã hoàn thành
					if (etaUtc.HasValue && order.NgayDuKienGiao.HasValue && order.NgayDuKienGiao.Value != etaUtc.Value)
						return new(false, "Không thể chỉnh sửa ngày dự kiến giao khi đơn hàng đã hoàn thành.");

					if (!string.IsNullOrWhiteSpace(note))
					{
						Log(order, current, $"Ghi chú: {note.Trim()}", userId);
						return new(true, "Đã lưu ghi chú giao nhận.", Status: current);
					}
					return new(true, "Trạng thái đơn không thay đổi.", Status: current, ProofUsed: proof != null && proof == order.AnhXacNhanGiao);
				}

				var changedEta = etaUtc.HasValue && order.NgayDuKienGiao != etaUtc.Value;
				if (changedEta)
				{
					var etaVn = etaUtc!.Value.AddHours(7);
					if (order.NgayDat.HasValue && etaVn.Date < order.NgayDat.Value.Date)
						return new(false, "Ngày dự kiến giao không hợp lệ.");
					order.NgayDuKienGiao = DateTime.SpecifyKind(etaUtc.Value, DateTimeKind.Utc);
				}
				var hasNote = !string.IsNullOrWhiteSpace(note);
				if (changedEta || hasNote)
				{
					var etaPart = changedEta ? $" Dự kiến: {order.NgayDuKienGiao?.AddHours(7):dd/MM/yyyy HH:mm}." : "";
					var notePart = hasNote ? $" Ghi chú: {note!.Trim()}" : "";
					Log(order, current, $"Cập nhật giao nhận.{etaPart}{notePart}".Trim(), userId);
					return new(true, "Đã lưu thông tin giao nhận.", Status: current);
				}
				return new(true, "Trạng thái đơn không thay đổi.", Status: current, ProofUsed: proof != null && proof == order.AnhXacNhanGiao);
			}

			// Chặn chuyển lùi trạng thái khi đơn đã hoàn thành (chỉ cho phép rẽ sang Yêu cầu đổi trả)
			if (isCompletedState && target != OrderRules.ReturnRequested)
			{
				return new(false, "Không thể chuyển lùi trạng thái khi đơn hàng đã hoàn thành.");
			}

			if (isCompletedState && etaUtc.HasValue && order.NgayDuKienGiao.HasValue && order.NgayDuKienGiao.Value != etaUtc.Value)
			{
				return new(false, "Không thể chỉnh sửa ngày dự kiến giao khi đơn hàng đã hoàn thành.");
			}

			if (!isCompletedState && etaUtc.HasValue)
			{
				var etaVn = etaUtc.Value.AddHours(7);
				if (order.NgayDat.HasValue && etaVn.Date < order.NgayDat.Value.Date)
					return new(false, "Ngày dự kiến giao không hợp lệ.");
				order.NgayDuKienGiao = DateTime.SpecifyKind(etaUtc.Value, DateTimeKind.Utc);
			}

			if (!OrderRules.CanTransition(current, target, OrderRules.IsCod(p?.PhuongThuc), OrderRules.IsPaid(p?.TrangThai), !string.IsNullOrWhiteSpace(proof), codCollected, returnedReceived))
			{
				return new(false, $"Không thể chuyển trạng thái từ {current} sang {target}.");
			}

			if (target == OrderRules.Cancelled) return await CancelLockedAsync(order, userId, true);
			if (target == OrderRules.DeliveryFailed && string.IsNullOrWhiteSpace(note))
				return new(false, "Nhập lý do giao thất bại vào ô Ghi chú giao nhận để khách và nhân viên nắm được.");

			order.TrangThaiDonHang = target;
			if (target == OrderRules.Completed)
			{
				if (!string.IsNullOrWhiteSpace(proof)) order.AnhXacNhanGiao = proof;
				if (p != null && !OrderRules.IsPaid(p.TrangThai))
				{
					p.TrangThai = OrderRules.Paid;
					p.NgayThanhToan = OrderRules.VietnamNow();
					if (string.IsNullOrWhiteSpace(p.MaGiaoDich)) p.MaGiaoDich = $"DONE-{id}";
				}
				if (order.HoaDon != null) order.HoaDon.TrangThaiHoaDon = OrderRules.Paid;
			}
			else if (target == OrderRules.ReturnReceived)
			{
				// Xác nhận đã nhận hàng trả về từ khách
				Log(order, target, $"Nhân viên đã xác nhận nhận lại hàng đổi trả. {note?.Trim()}", userId);
				return new(true, "Đã cập nhật: Đã nhận lại hàng trả. Bạn có thể tiến hành nhập lại kho hoặc xác nhận hoàn tiền nếu có.", Status: target);
			}
			Log(order, target, $"Chuyển từ {current} sang {target}. {note?.Trim()}", userId, target == OrderRules.Completed ? proof : null);
			return new(true, "Đã cập nhật trạng thái đơn hàng.", Status: target, ProofUsed: target == OrderRules.Completed && !string.IsNullOrWhiteSpace(proof));
		});

	public Task<OrderChangeResult> CompleteReturnAsync(int id, int userId, bool restock, bool refund, string? refundReference, string? note) => RunAsync(id, userId, true, async order =>
	{
		var current = OrderRules.Normalize(order.TrangThaiDonHang);
		if (current != OrderRules.ReturnReceived && current != OrderRules.Returning)
			return new(false, "Chỉ hoàn tất đổi trả khi đơn ở trạng thái Đang đổi trả hoặc Đã nhận đổi trả.");

		// Không cho phép hoàn tiền hoặc hoàn kho nếu đơn đã ở trạng thái hoàn tiền
		if (current == OrderRules.Refunded)
			return new(false, "Đơn hàng đã được hoàn tiền trước đó, không thể thực hiện hoàn tất đổi trả lại.");

		var p = order.ThanhToans.OrderByDescending(x => x.NgayTao).FirstOrDefault();

		if (refund)
		{
			if (string.IsNullOrWhiteSpace(refundReference) || refundReference.Length < 3 || refundReference.Length > 100)
				return new(false, "Vui lòng nhập mã giao dịch hoàn tiền thực tế hợp lệ từ ngân hàng/cổng thanh toán.");

			if (p != null && (p.TrangThai == OrderRules.Refunded || p.TrangThai == "Đã hoàn tiền"))
				return new(false, "Khoản thanh toán của đơn hàng này đã được đánh dấu hoàn tiền trước đó.");

			// Kiểm tra mã giao dịch hoàn tiền không bị trùng lặp với giao dịch hoàn tiền khác
			var cleanRef = refundReference.Trim().ToUpperInvariant();
			var isDuplicateRef = await _db.ThanhToans.AsNoTracking().AnyAsync(x => x.MaDonHang != id && x.MaGiaoDich == cleanRef && (x.TrangThai == OrderRules.Refunded || x.TrangThai == "Đã hoàn tiền"));
			if (isDuplicateRef)
				return new(false, "Mã giao dịch hoàn tiền này đã được ghi nhận cho một đơn hàng khác.");
		}

		// Nhập lại kho nếu hàng kiểm tra còn nguyên vẹn và nhân viên chọn hoàn kho
		// Kiểm tra xem đơn hàng đã từng hoàn kho chưa thông qua lịch sử vận chuyển
		if (restock)
		{
			var alreadyRestocked = await _db.LichSuVanChuyens.AsNoTracking()
				.AnyAsync(x => x.MaDonHang == id && x.MoTa != null && x.MoTa.Contains("Đã nhập lại kho"));
			if (alreadyRestocked)
				return new(false, "Sản phẩm của đơn hàng này đã được nhập lại kho trước đó, không thể hoàn kho trùng lặp.");

			foreach (var g in order.ChiTietDonHangs.GroupBy(x => x.MaBienThe).OrderBy(x => x.Key))
			{
				var qty = g.Sum(x => x.SoLuong);
				if (qty <= 0) return new(false, "Số lượng sản phẩm hoàn kho không hợp lệ.");
				var changed = await _db.BienTheSanPhams.Where(x => x.MaBienThe == g.Key && x.SoLuong <= int.MaxValue - qty)
					.ExecuteUpdateAsync(set => set.SetProperty(x => x.SoLuong, x => x.SoLuong + qty)
						.SetProperty(x => x.TrangThai, x => x.TrangThai == "Hết hàng" && x.SoLuong + qty > 0 ? "Còn hàng" : x.TrangThai));
				if (changed != 1) return new(false, "Không thể hoàn tồn kho một mặt hàng. Quy trình hoàn tất đổi trả bị hủy.");
			}
		}

		// Cập nhật trạng thái thanh toán nếu có giao dịch hoàn tiền thực tế
		if (refund && p != null)
		{
			p.TrangThai = OrderRules.Refunded;
			p.MaGiaoDich = refundReference?.Trim().ToUpperInvariant();
		}

		order.TrangThaiDonHang = refund ? OrderRules.Refunded : OrderRules.Completed;
		if (order.HoaDon != null)
		{
			order.HoaDon.TrangThaiHoaDon = order.TrangThaiDonHang;
		}

		var logDesc = $"Hoàn tất xử lý đổi trả: {(restock ? "Đã nhập lại kho." : "Không hoàn tồn kho.")} " +
					  $"{(refund ? $"Đã hoàn tiền (Giao dịch: {refundReference?.Trim().ToUpperInvariant()})." : "Đã đổi hàng/hoàn tất đơn.")} {note?.Trim()}";
		Log(order, order.TrangThaiDonHang, logDesc, userId);

		return new(true, $"Đã hoàn tất quy trình đổi trả thành công ({order.TrangThaiDonHang}).", Status: order.TrangThaiDonHang);
	});

	public Task<OrderChangeResult> DecideReturnAsync(int id, int userId, string? action, string? reason) => RunAsync(id, userId, true, order =>
	{
		if (OrderRules.Normalize(order.TrangThaiDonHang) != OrderRules.ReturnRequested)
			return Task.FromResult(new OrderChangeResult(false, "Chỉ xử lý yêu cầu đổi trả đang chờ quyết định."));
		if (action != "Accept" && action != "Reject") return Task.FromResult(new OrderChangeResult(false, "Thao tác đổi trả không hợp lệ."));
		if (action == "Reject" && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200))
			return Task.FromResult(new OrderChangeResult(false, "Nhập lý do từ chối từ 1 đến 200 ký tự."));
		order.TrangThaiDonHang = action == "Accept" ? OrderRules.Returning : OrderRules.Completed;
		Log(order, action == "Accept" ? OrderRules.Returning : "Từ chối đổi trả", action == "Accept" ?
			"Đã chấp nhận yêu cầu, chờ thu hồi và kiểm tra hàng; chưa xác nhận đã nhận hàng hoặc hoàn tiền." : $"Từ chối đổi trả: {reason?.Trim()}", userId);
		return Task.FromResult(new OrderChangeResult(true, action == "Accept" ? "Đã chấp nhận yêu cầu. Chờ nhận và kiểm tra hàng trả." : "Đã từ chối yêu cầu đổi trả. Đơn trở về trạng thái Đã hoàn thành."));
	});

	public Task<OrderChangeResult> RequestReturnAsync(int id, int userId, IReadOnlyCollection<int> variants,
		string reason, string description, string phone, IReadOnlyCollection<string> images) => RunAsync(id, userId, false, async order =>
		{
			if (OrderRules.Normalize(order.TrangThaiDonHang) == OrderRules.ReturnRequested)
			{
				var alreadySaved = images.Count > 0 && await _db.LichSuVanChuyens.AnyAsync(x => x.MaDonHang == id && images.Contains(x.HinhAnhNguoiNhan!));
				return new(true, "Yêu cầu đổi trả đã được gửi trước đó.", ProofUsed: alreadySaved);
			}
			if (OrderRules.Normalize(order.TrangThaiDonHang) != OrderRules.Completed) return new OrderChangeResult(false, "Chỉ yêu cầu đổi trả khi đơn đã hoàn tất giao hàng.");

			// Kiểm tra thời hạn đổi trả trong vòng 7 ngày kể từ khi hoàn thành đơn
			var completedLog = await _db.LichSuVanChuyens
				.Where(x => x.MaDonHang == id && (x.TrangThai == OrderRules.Completed || x.TrangThai == "Đã giao" || x.TrangThai == "Hoàn thành" || x.TrangThai == "Giao hàng thành công" || x.TrangThai == "Đã giao hàng"))
				.OrderByDescending(x => x.ThoiGian)
				.FirstOrDefaultAsync();

			DateTime completedTime;
			if (completedLog != null)
			{
				completedTime = completedLog.ThoiGian;
			}
			else
			{
				// Fallback: Lấy thời gian từ log gần nhất của đơn hoặc NgayDat
				var latestLog = await _db.LichSuVanChuyens
					.Where(x => x.MaDonHang == id)
					.OrderByDescending(x => x.ThoiGian)
					.FirstOrDefaultAsync();

				completedTime = latestLog?.ThoiGian ?? order.NgayDat ?? OrderRules.VietnamNow();
			}

			if ((OrderRules.VietnamNow() - completedTime).TotalDays > 7)
			{
				return new OrderChangeResult(false, "Đơn hàng đã hoàn thành quá 7 ngày, đã hết thời hạn hỗ trợ đổi trả theo quy định.");
			}

			var targetVariants = (variants != null && variants.Count > 0)
				? variants.Where(v => order.ChiTietDonHangs.Any(x => x.MaBienThe == v)).ToList()
				: order.ChiTietDonHangs.Select(x => x.MaBienThe).ToList();

			if (targetVariants.Count < 1)
				return new OrderChangeResult(false, "Sản phẩm đổi trả phải thuộc đơn hàng của bạn.");
			if (string.IsNullOrWhiteSpace(reason) || reason.Length > 100 || string.IsNullOrWhiteSpace(description) || description.Length > 200 || string.IsNullOrWhiteSpace(phone) || phone.Length > 20)
				return new OrderChangeResult(false, "Lý do tối đa 100 ký tự, mô tả tối đa 200 ký tự và số điện thoại tối đa 20 ký tự.");
			var text = $"SP: {string.Join(",", targetVariants)}. Lý do: {reason.Trim()}. {description.Trim()}. SĐT: {phone.Trim()}.";
			// Ảnh nằm ở trường riêng, không nhồi đường dẫn làm mất mô tả trong cột 500 ký tự.
			Log(order, OrderRules.ReturnRequested, text, userId, images.FirstOrDefault());
			foreach (var image in images.Skip(1)) Log(order, "Ảnh yêu cầu đổi trả", "Ảnh bổ sung cho yêu cầu đổi trả.", userId, image);
			order.TrangThaiDonHang = OrderRules.ReturnRequested;
			return new OrderChangeResult(true, "Đã gửi yêu cầu đổi trả.", ProofUsed: images.Count > 0);
		});
}