using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EMua.Data;
using EMua.Models.Database;
using EMua.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMua.Controllers;

public class AuthController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IPasswordHasher<NguoiDung> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthController(
        EMuaDbContext db,
        IPasswordHasher<NguoiDung> passwordHasher,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        string? lotNumber,
        string? captchaOutput,
        string? passToken,
        string? genTime)
    {
        if (!model.AcceptTerms)
        {
            ModelState.AddModelError(
                nameof(model.AcceptTerms),
                "Bạn cần đồng ý điều khoản để đăng ký.");
        }

        if (!ModelState.IsValid)
            return View(model);

        if (!await VerifyGeeTestAsync("Register", lotNumber, captchaOutput, passToken, genTime))
        {
            ModelState.AddModelError("", "Xác minh bảo mật không thành công. Vui lòng thử lại.");
            return View(model);
        }

        var email = model.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(model.PhoneNumber);

        var emailExists = await _db.NguoiDungs
            .AnyAsync(x => x.Email == email);

        if (emailExists)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Email này đã được sử dụng.");
        }

        var phoneExists = await _db.NguoiDungs
            .AnyAsync(x => x.SoDienThoai == phone);

        if (phoneExists)
        {
            ModelState.AddModelError(
                nameof(model.PhoneNumber),
                "Số điện thoại này đã được sử dụng.");
        }

        var customerRoleExists = await _db.PhanQuyens
            .AnyAsync(x => x.MaQuyen == 3);

        if (!customerRoleExists)
        {
            ModelState.AddModelError(
                "",
                "Hệ thống chưa có quyền Khách hàng (Mã quyền 3).");
        }

        if (!ModelState.IsValid)
            return View(model);

        var user = new NguoiDung
        {
            TenNguoiDung = model.FullName.Trim(),
            Email = email,
            SoDienThoai = phone,
            TenDangNhap = $"kh_{Guid.NewGuid():N}"[..15],
            MaQuyen = 3,
            TrangThai = true,
            NgayTao = DateTime.Now
        };

        user.MatKhau = _passwordHasher.HashPassword(
            user,
            model.Password);

        try
        {
            _db.NguoiDungs.Add(user);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(
                "",
                "Không thể tạo tài khoản. Email hoặc số điện thoại có thể đã tồn tại.");

            return View(model);
        }

        TempData["SuccessMessage"] =
            "Đăng ký thành công. Hãy đăng nhập để tiếp tục.";

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;

        return View(new LoginViewModel());
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        string? lotNumber = null,
        string? captchaOutput = null,
        string? passToken = null,
        string? genTime = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        if (!await VerifyGeeTestAsync(
            "Login",
            lotNumber,
            captchaOutput,
            passToken,
            genTime))
        {
            ModelState.AddModelError(
                "",
                "Xác minh bảo mật không thành công. Vui lòng thử lại.");

            return View(model);
        }

        // Có thể là Email hoặc SĐT
        var identifier = model.Identifier.Trim();

        NguoiDung? user;

        // Nếu có @ => xử lý như Email
        if (identifier.Contains('@'))
        {
            var email = identifier.ToLowerInvariant();

            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x => x.Email == email);
        }
        else
        {
            // Nếu không có @ => xử lý như SĐT
            var phone = NormalizePhone(identifier);

            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x => x.SoDienThoai == phone);
        }

        if (user == null ||
            string.IsNullOrWhiteSpace(user.MatKhau))
        {
            ModelState.AddModelError(
                "",
                "Email, số điện thoại hoặc mật khẩu không đúng.");

            return View(model);
        }

        PasswordVerificationResult verifyResult;

        try
        {
            verifyResult =
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.MatKhau,
                    model.Password);
        }
        catch (FormatException)
        {
            // Tài khoản cũ đang lưu password chưa hash
            if (user.MatKhau == model.Password)
            {
                user.MatKhau =
                    _passwordHasher.HashPassword(
                        user,
                        model.Password);

                await _db.SaveChangesAsync();

                verifyResult =
                    PasswordVerificationResult.Success;
            }
            else
            {
                ModelState.AddModelError(
                    "",
                    "Email, số điện thoại hoặc mật khẩu không đúng.");

                return View(model);
            }
        }

        if (verifyResult ==
            PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(
                "",
                "Email, số điện thoại hoặc mật khẩu không đúng.");

            return View(model);
        }

        if (!user.TrangThai)
        {
            ModelState.AddModelError(
                "",
                "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

            return View(model);
        }

        if (verifyResult ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.MatKhau =
                _passwordHasher.HashPassword(
                    user,
                    model.Password);

            await _db.SaveChangesAsync();
        }

        await SignInUserAsync(
            user,
            model.RememberMe);

        if (IsStaff(user))
        {
            return RedirectToAction(
                "Index",
                "Dashboard",
                new { area = "Staff" });
        }

        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction(
            "Index",
            "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
    }

    // Dùng chung cho nút Google và Facebook.
    [HttpGet]
    public IActionResult ExternalLogin(
        string provider,
        string? returnUrl = null)
    {
        var providerScheme = GetProviderScheme(provider);

        if (providerScheme == null)
            return RedirectToAction(nameof(Login));

        var redirectUrl = Url.Action(
            nameof(ExternalLoginCallback),
            "Auth",
            new { provider, returnUrl });

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        };

        return Challenge(properties, providerScheme);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(
        string? provider = null,
        string? returnUrl = null,
        string? remoteError = null)
    {
        var providerName = string.Equals(
            provider,
            "Facebook",
            StringComparison.OrdinalIgnoreCase)
            ? "Facebook"
            : "Google";

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ModelState.AddModelError(
                "",
                $"Đăng nhập {providerName} không thành công.");

            return View("Login", new LoginViewModel());
        }

        var externalResult = await HttpContext.AuthenticateAsync("External");

        if (!externalResult.Succeeded || externalResult.Principal == null)
        {
            ModelState.AddModelError(
                "",
                $"Không thể lấy thông tin tài khoản {providerName}.");

            return View("Login", new LoginViewModel());
        }

        var providerId = externalResult.Principal
    .FindFirstValue(ClaimTypes.NameIdentifier);

        var email = externalResult.Principal
            .FindFirstValue(ClaimTypes.Email)?
            .Trim()
            .ToLowerInvariant();

        // Facebook đôi khi không trả email.
        // Tạo định danh nội bộ duy nhất để tài khoản vẫn đăng nhập được.
        if (string.IsNullOrWhiteSpace(email) &&
            providerName == "Facebook" &&
            !string.IsNullOrWhiteSpace(providerId))
        {
            email = $"facebook_{providerId}@social.emua.local";
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync("External");

            ModelState.AddModelError(
                "",
                $"Không thể lấy thông tin tài khoản {providerName}.");

            return View("Login", new LoginViewModel());
        }

        var user = await _db.NguoiDungs
            .Include(x => x.MaQuyenNavigation)
            .FirstOrDefaultAsync(x => x.Email == email);

        // Lần đầu đăng nhập Google/Facebook: tự tạo Khách hàng.
        if (user == null)
        {
            var customerRole = await _db.PhanQuyens
                .FirstOrDefaultAsync(x => x.MaQuyen == 3);

            if (customerRole == null)
            {
                await HttpContext.SignOutAsync("External");

                ModelState.AddModelError(
                    "",
                    "Hệ thống chưa có quyền Khách hàng (Mã quyền 3).");

                return View("Login", new LoginViewModel());
            }

            var fullName = externalResult.Principal
                .FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = email.Split('@')[0];
            }

            var usernamePrefix = providerName == "Facebook" ? "fb" : "gg";

            user = new NguoiDung
            {
                TenNguoiDung = fullName,
                Email = email,
                SoDienThoai = null,
                TenDangNhap = $"{usernamePrefix}_{Guid.NewGuid():N}"[..15],
                MaQuyen = 3,
                MaQuyenNavigation = customerRole,
                TrangThai = true,
                NgayTao = DateTime.Now
            };

            // Cột MatKhau vẫn được lưu giá trị băm an toàn.
            user.MatKhau = _passwordHasher.HashPassword(
                user,
                Guid.NewGuid().ToString("N"));

            _db.NguoiDungs.Add(user);
            await _db.SaveChangesAsync();
        }

        if (!user.TrangThai)
        {
            await HttpContext.SignOutAsync("External");

            ModelState.AddModelError(
                "",
                "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

            return View("Login", new LoginViewModel());
        }

        await SignInUserAsync(user, true);
        await HttpContext.SignOutAsync("External");

        if (IsStaff(user))
        {
            return RedirectToAction(
                "Index",
                "Dashboard",
                new { area = "Staff" });
        }

        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction("Index", "Home");
    }

    private async Task SignInUserAsync(
        NguoiDung user,
        bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.MaNguoiDung.ToString()),
            new(ClaimTypes.Name, user.TenNguoiDung ?? "Khách hàng"),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(
                ClaimTypes.Role,
                user.MaQuyenNavigation?.TenQuyen ?? "KhachHang")
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(7)
                    : null
            });
    }

    private static string? GetProviderScheme(string provider)
    {
        if (string.Equals(
            provider,
            "Google",
            StringComparison.OrdinalIgnoreCase))
        {
            return GoogleDefaults.AuthenticationScheme;
        }

        if (string.Equals(
            provider,
            "Facebook",
            StringComparison.OrdinalIgnoreCase))
        {
            return FacebookDefaults.AuthenticationScheme;
        }

        return null;
    }

    private async Task<bool> VerifyGeeTestAsync(
        string action,
        string? lotNumber,
        string? captchaOutput,
        string? passToken,
        string? genTime)
    {
        if (string.IsNullOrWhiteSpace(lotNumber) ||
            string.IsNullOrWhiteSpace(captchaOutput) ||
            string.IsNullOrWhiteSpace(passToken) ||
            string.IsNullOrWhiteSpace(genTime))
            return false;

        var captchaId = _configuration[$"GeeTest:{action}:CaptchaId"];
        var captchaKey = _configuration[$"GeeTest:{action}:CaptchaKey"];

        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(captchaKey))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(captchaKey));
        var signToken = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(lotNumber))).ToLowerInvariant();

        var data = new Dictionary<string, string>
        {
            ["lot_number"] = lotNumber,
            ["captcha_output"] = captchaOutput,
            ["pass_token"] = passToken,
            ["gen_time"] = genTime,
            ["captcha_id"] = captchaId,
            ["sign_token"] = signToken
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                "https://gcaptcha4.geetest.com/validate",
                new FormUrlEncodedContent(data));

            var result = await response.Content.ReadFromJsonAsync<GeeTestValidationResult>();
            return response.IsSuccessStatusCode &&
                   string.Equals(result?.Result, "success", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed class GeeTestValidationResult
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }

    private static string NormalizePhone(string phone)
    {
        var result = Regex.Replace(phone, @"[\s\-.()]", "");

        if (result.StartsWith("+84"))
            result = "0" + result[3..];

        return result;
    }
<<<<<<< Updated upstream
=======

    private static bool IsStaff(NguoiDung user)
    {
        var roleName = user.MaQuyenNavigation?.TenQuyen;

        return roleName is "NhanVien" or "Nhân viên";
    }
>>>>>>> Stashed changes
}
