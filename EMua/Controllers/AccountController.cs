using System.Security.Claims;
using System.Text.RegularExpressions;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] BuiltInAvatars =
    {
        "/images/avatar/conbo.jpg",
        "/images/avatar/conchoo.jpg",
        "/images/avatar/conkhi.jpg",
        "/images/avatar/convit.jpg",
        "/images/avatar/conhuu.jpg"
    };

    public AccountController(
        EMuaDbContext db,
        IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await GetCurrentUserAsync();

        if (user == null)
            return RedirectToAction("Login", "Auth");

        return View(ToProfileViewModel(user));
    }

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await GetCurrentUserAsync();

        if (user == null)
            return RedirectToAction("Login", "Auth");

        return View(new EditAccountProfileViewModel
        {
            FullName = user.TenNguoiDung ?? string.Empty,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.SoDienThoai,
            Address = user.DiaChi,
            CurrentAvatarUrl = user.AnhDaiDien,
            SelectedAvatar = user.AnhDaiDien
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditAccountProfileViewModel model)
    {
        var user = await GetCurrentUserAsync();

        if (user == null)
            return RedirectToAction("Login", "Auth");

        model.Email = user.Email ?? string.Empty;
        model.CurrentAvatarUrl = user.AnhDaiDien;

        if (!string.IsNullOrWhiteSpace(model.SelectedAvatar) &&
            !BuiltInAvatars.Contains(model.SelectedAvatar))
        {
            ModelState.AddModelError(
                nameof(model.SelectedAvatar),
                "Ảnh đại diện được chọn không hợp lệ."
            );
        }

        if (model.AvatarFile is { Length: > 0 })
        {
            var allowedExtensions = new[]
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            var extension = Path.GetExtension(model.AvatarFile.FileName)
                .ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError(
                    nameof(model.AvatarFile),
                    "Chỉ chấp nhận ảnh JPG, JPEG, PNG hoặc WEBP."
                );
            }

            if (model.AvatarFile.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError(
                    nameof(model.AvatarFile),
                    "Ảnh đại diện không được vượt quá 2 MB."
                );
            }
        }

        var phone = string.IsNullOrWhiteSpace(model.PhoneNumber)
            ? null
            : NormalizePhone(model.PhoneNumber);

        var currentPhone = string.IsNullOrWhiteSpace(user.SoDienThoai)
            ? null
            : NormalizePhone(user.SoDienThoai);

        // Chỉ kiểm tra trùng khi người dùng đổi sang số điện thoại khác.
        if (!string.IsNullOrWhiteSpace(phone) && phone != currentPhone)
        {
            var phoneExists = await _db.NguoiDungs.AnyAsync(x =>
                x.MaNguoiDung != user.MaNguoiDung &&
                x.SoDienThoai == phone);

            if (phoneExists)
            {
                ModelState.AddModelError(
                    nameof(model.PhoneNumber),
                    "Số điện thoại này đã được sử dụng."
                );
            }
        }

        if (!ModelState.IsValid)
            return View(model);

        // Nếu tải ảnh từ máy, ưu tiên ảnh tải lên.
        if (model.AvatarFile is { Length: > 0 })
        {
            var extension = Path.GetExtension(model.AvatarFile.FileName)
                .ToLowerInvariant();

            var avatarFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "avatars"
            );

            Directory.CreateDirectory(avatarFolder);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(avatarFolder, fileName);

            await using var stream = new FileStream(
                filePath,
                FileMode.Create
            );

            await model.AvatarFile.CopyToAsync(stream);

            user.AnhDaiDien = $"/uploads/avatars/{fileName}";
        }
        // Nếu chọn avatar có sẵn.
        else if (!string.IsNullOrWhiteSpace(model.SelectedAvatar))
        {
            user.AnhDaiDien = model.SelectedAvatar;
        }

        user.TenNguoiDung = model.FullName.Trim();
        user.SoDienThoai = phone;
        user.DiaChi = string.IsNullOrWhiteSpace(model.Address)
            ? null
            : model.Address.Trim();

        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Cập nhật hồ sơ thành công.";

        return RedirectToAction(nameof(Profile));
    }

    private async Task<NguoiDung?> GetCurrentUserAsync()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdText, out var userId))
            return null;

        return await _db.NguoiDungs
            .Include(x => x.MaQuyenNavigation)
            .Include(x => x.DonHangs)
            .Include(x => x.YeuThiches)
            .FirstOrDefaultAsync(x => x.MaNguoiDung == userId);
    }

    private static AccountProfileViewModel ToProfileViewModel(
        NguoiDung user)
    {
        var databaseRoleName = user.MaQuyenNavigation?.TenQuyen
            ?? "KhachHang";

        var roleName = databaseRoleName switch
        {
            "KhachHang" => "Khách hàng",
            "NhanVien" => "Nhân viên",
            "Admin" => "Quản trị viên",
            _ => databaseRoleName
        };

        var isCustomer = databaseRoleName is "KhachHang" or "Khách hàng";

        return new AccountProfileViewModel
        {
            UserId = user.MaNguoiDung,
            FullName = user.TenNguoiDung ?? "Người dùng",
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.SoDienThoai,
            Address = user.DiaChi,
            AvatarUrl = user.AnhDaiDien,
            RoleName = roleName,
            MemberSince = user.NgayTao,
            OrderCount = user.DonHangs.Count,
            FavoriteCount = user.YeuThiches.Count,
            IsCustomer = isCustomer
        };
    }

    private static string NormalizePhone(string phone)
    {
        var result = Regex.Replace(phone, @"[\s\-.()]", "");

        if (result.StartsWith("+84"))
            result = "0" + result[3..];

        return result;
    }
}