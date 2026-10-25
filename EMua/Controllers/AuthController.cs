using System.Security.Claims;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.Encodings.Web;
using EMua.Services;
using Microsoft.AspNetCore.WebUtilities;
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
using Microsoft.Extensions.Logging;

namespace EMua.Controllers;

public class AuthController : Controller
{
    private readonly EMuaDbContext _db;
    private readonly IPasswordHasher<NguoiDung> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;
    private readonly IAppEmailSender _emailSender;

    public AuthController(
        EMuaDbContext db,
        IPasswordHasher<NguoiDung> passwordHasher,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AuthController> logger,
        IAppEmailSender emailSender)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _emailSender = emailSender;
    }

    // =====================================================
    // REGISTER - GET
    // =====================================================
    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new RegisterViewModel());
    }

    // =====================================================
    // REGISTER - POST
    // =====================================================
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

        // GEETEST CAPTCHA
        if (!await VerifyGeeTestAsync(
                "Register",
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

        var email = model.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(model.PhoneNumber);

        var emailExists = await _db.NguoiDungs
            .AnyAsync(x => x.Email != null && x.Email.ToLower() == email);

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

        user.MatKhau = _passwordHasher.HashPassword(user, model.Password);

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

    // =====================================================
    // LOGIN - GET
    // =====================================================
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["ReturnUrl"] = returnUrl;

        return View(new LoginViewModel());
    }

    // =====================================================
    // LOGIN - POST
    // =====================================================
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

        // 1. KIỂM TRA MÔ HÌNH VÀ THỜI GIAN KHÓA TÀI KHOẢN/SESSION
        var lockUntilTicks = HttpContext.Session.GetString("LockUntil");
        if (!string.IsNullOrEmpty(lockUntilTicks) && long.TryParse(lockUntilTicks, out long ticks))
        {
            var lockUntil = new DateTime(ticks);
            if (lockUntil > DateTime.Now)
            {
                var remainingMinutes = Math.Ceiling((lockUntil - DateTime.Now).TotalMinutes);
                ModelState.AddModelError("", $"Tài khoản tạm khóa do nhập sai quá 5 lần. Vui lòng thử lại sau {remainingMinutes} phút.");
                return View(model);
            }
        }

        if (!ModelState.IsValid)
            return View(model);

        // 2. BƯỚC 1: XÁC MINH CAPTCHA
        if (!await VerifyGeeTestAsync("Login", lotNumber, captchaOutput, passToken, genTime))
        {
            ModelState.AddModelError("", "Xác minh bảo mật (Captcha) không thành công hoặc đã hết hạn. Vui lòng thử lại.");
            return View(model);
        }

        // 3. BƯỚC 2: TÌM NGƯỜI DÙNG
        var identifier = model.Identifier.Trim();
        NguoiDung? user;

        if (identifier.Contains('@'))
        {
            var email = identifier.ToLowerInvariant();
            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x => x.Email != null && x.Email.ToLower() == email);
        }
        else
        {
            var phone = NormalizePhone(identifier);
            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x => x.SoDienThoai == phone);
        }

        if (user == null || string.IsNullOrWhiteSpace(user.MatKhau))
        {
            HandleFailedLogin();
            return View(model);
        }

        // 4. BƯỚC 3: KIỂM TRA MẬT KHẨU
        PasswordVerificationResult verifyResult;

        try
        {
            verifyResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.MatKhau,
                model.Password);
        }
        catch (FormatException)
        {
            if (user.MatKhau == model.Password)
            {
                // Chuyển mật khẩu cũ dạng plain-text sang hash một cách an toàn.
                user.MatKhau = _passwordHasher.HashPassword(user, model.Password);
                if (!await TrySavePasswordHashAsync(user))
                    return View(model);

                verifyResult = PasswordVerificationResult.Success;
            }
            else
            {
                HandleFailedLogin();
                return View(model);
            }
        }

        if (verifyResult == PasswordVerificationResult.Failed)
        {
            HandleFailedLogin();
            return View(model);
        }

        if (!user.TrangThai)
        {
            ModelState.AddModelError("", "Tài khoản đã bị khóa bởi quản trị viên.");
            return View(model);
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.MatKhau = _passwordHasher.HashPassword(user, model.Password);
            if (!await TrySavePasswordHashAsync(user))
                return View(model);
        }

        // 5. ĐĂNG NHẬP THÀNH CÔNG -> CLEAR SESSION
        HttpContext.Session.Remove("FailedPasswordCount");
        HttpContext.Session.Remove("LockUntil");

        await SignInUserAsync(user, model.RememberMe);

        // =====================================================
        // ÉP CHUYỂN HƯỚNG TRỰC TIẾP THEO QUYỀN
        // =====================================================

        // 1. ADMIN (MaQuyen == 1) -> Vào thẳng Admin Dashboard ngay lập tức
        if (IsAdmin(user))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        // 2. STAFF (MaQuyen == 2) -> Vào thẳng Staff Dashboard
        if (IsStaff(user))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Staff" });
        }

        // 3. Khách hàng nếu có returnUrl hợp lệ
        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        // 4. Mặc định là Khách hàng -> Về trang chủ
        return RedirectToAction("Index", "Home");
    }

    // =====================================================
    // FORGOT PASSWORD - GET
    // =====================================================
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    // =====================================================
    // FORGOT PASSWORD - POST
    // Dùng GeeTest của trang Login để hạn chế spam email.
    // =====================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordViewModel model,
        string? lotNumber,
        string? captchaOutput,
        string? passToken,
        string? genTime)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (!await VerifyGeeTestAsync(
                "Login", lotNumber, captchaOutput, passToken, genTime))
        {
            ModelState.AddModelError("",
                "Xác minh bảo mật không thành công. Vui lòng thử lại.");
            return View(model);
        }

        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _db.NguoiDungs.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Email != null && x.Email.ToLower() == email);

        // Không tiết lộ email nào đã đăng ký hoặc tài khoản nào đang bị khóa.
        if (user != null && user.TrangThai && !string.IsNullOrWhiteSpace(user.MatKhau))
        {
            try
            {
                var token = CreatePasswordResetToken(user);
                var path = Url.Action(
                    nameof(ResetPassword), "Auth",
                    new { email, token });

                var baseUrl = _configuration["App:BaseUrl"];
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    throw new InvalidOperationException("Thiếu App:BaseUrl hợp lệ trong cấu hình.");

                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("Không tạo được đường dẫn đặt lại mật khẩu.");

                var resetUrl = new Uri(uri, path).AbsoluteUri;
                var safeUrl = HtmlEncoder.Default.Encode(resetUrl);

                var htmlBody =
                    "<h2>Đặt lại mật khẩu EMUA</h2>" +
                    "<p>Chúng tôi nhận được yêu cầu khôi phục mật khẩu.</p>" +
                    $"<p><a href=\"{safeUrl}\">Nhấn vào đây để đặt mật khẩu mới</a></p>" +
                    "<p>Liên kết có hiệu lực trong 30 phút.</p>" +
                    "<p>Nếu không yêu cầu, bạn có thể bỏ qua email này.</p>";

                await _emailSender.SendAsync(email, "EMUA - Đặt lại mật khẩu", htmlBody);
            }
            catch (Exception ex)
            {
                // Không lộ tình trạng email hoặc chi tiết cấu hình cho người ngoài.
                _logger.LogError(ex, "Không gửi được email đặt lại mật khẩu EMUA.");
            }
        }

        ViewBag.Notice = "Nếu email đã đăng ký và đủ điều kiện, " +
                         "hệ thống sẽ gửi một liên kết khôi phục mật khẩu.";
        return View(model);
    }

    // =====================================================
    // RESET PASSWORD - GET
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? email, string? token)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var user = string.IsNullOrWhiteSpace(normalizedEmail)
            ? null
            : await _db.NguoiDungs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Email != null &&
                                          x.Email.ToLower() == normalizedEmail);

        if (user == null || !user.TrangThai ||
            !IsValidPasswordResetToken(user, token))
        {
            TempData["ErrorMessage"] = "Liên kết không hợp lệ hoặc đã hết hạn.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new ResetPasswordViewModel
        {
            Email = normalizedEmail!,
            Token = token!
        });
    }

    // =====================================================
    // RESET PASSWORD - POST
    // =====================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _db.NguoiDungs.FirstOrDefaultAsync(x =>
            x.Email != null && x.Email.ToLower() == email);

        if (user == null || !user.TrangThai ||
            !IsValidPasswordResetToken(user, model.Token))
        {
            ModelState.AddModelError("", "Liên kết không hợp lệ hoặc đã hết hạn.");
            return View(model);
        }

        // Thay đổi hash mật khẩu cũng vô hiệu hóa các liên kết cũ.
        user.MatKhau = _passwordHasher.HashPassword(user, model.NewPassword);

        if (!await TrySavePasswordHashAsync(user))
            return View(model);

        TempData["SuccessMessage"] =
            "Đặt lại mật khẩu thành công. Vui lòng đăng nhập.";
        return RedirectToAction(nameof(Login));
    }

    // =====================================================
    // PASSWORD RESET TOKEN HELPERS
    // Token HMAC gắn với user ID, email, hash mật khẩu và hạn dùng.
    // Không lưu token trong database và không để lộ password hash trong URL.
    // =====================================================
    private string CreatePasswordResetToken(NguoiDung user)
    {
        long expiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        byte[] signature = ComputePasswordResetSignature(user, expiresAt);
        return $"{expiresAt}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    private bool IsValidPasswordResetToken(NguoiDung user, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(user.MatKhau))
            return false;

        var pieces = token.Split('.', 2);
        if (pieces.Length != 2 ||
            !long.TryParse(pieces[0], NumberStyles.None,
                           CultureInfo.InvariantCulture, out var expiresAt))
            return false;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiresAt <= now || expiresAt > now + 30 * 60)
            return false;

        byte[] provided;
        try
        {
            provided = WebEncoders.Base64UrlDecode(pieces[1]);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        byte[] expected = ComputePasswordResetSignature(user, expiresAt);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private byte[] ComputePasswordResetSignature(NguoiDung user, long expiresAt)
    {
        var keyText = _configuration["PasswordReset:SigningKey"]
                   ?? _configuration["GeeTest:PasswordReset:SigningKey"];
        if (string.IsNullOrWhiteSpace(keyText))
            throw new InvalidOperationException(
                "Thiếu PasswordReset:SigningKey trong cấu hình.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyText);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "PasswordReset:SigningKey phải ở dạng Base64.", ex);
        }

        if (key.Length < 32)
            throw new InvalidOperationException(
                "PasswordReset:SigningKey phải dài tối thiểu 32 byte.");

        // Chuỗi được ký chứa password hash, nhưng token gửi email
        // chỉ chứa expiry và chữ ký HMAC, không chứa hash.
        var payload = $"{user.MaNguoiDung}\n" +
                      $"{user.Email?.Trim().ToLowerInvariant()}\n" +
                      $"{expiresAt}\n{user.MatKhau}";

        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
    }

    // =====================================================
    // LOGOUT
    // =====================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
    }

    // =====================================================
    // EXTERNAL LOGIN (GOOGLE / FACEBOOK)
    // =====================================================
    [HttpGet]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
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
        var providerName = string.Equals(provider, "Facebook", StringComparison.OrdinalIgnoreCase)
            ? "Facebook"
            : "Google";

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ModelState.AddModelError("", $"Đăng nhập {providerName} không thành công.");
            return View("Login", new LoginViewModel());
        }

        var externalResult = await HttpContext.AuthenticateAsync("External");

        if (!externalResult.Succeeded || externalResult.Principal == null)
        {
            ModelState.AddModelError("", $"Không thể lấy thông tin tài khoản {providerName}.");
            return View("Login", new LoginViewModel());
        }

        var providerId = externalResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = externalResult.Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) && providerName == "Facebook" && !string.IsNullOrWhiteSpace(providerId))
        {
            email = $"facebook_{providerId}@social.emua.local";
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync("External");
            ModelState.AddModelError("", $"Không thể lấy thông tin tài khoản {providerName}.");
            return View("Login", new LoginViewModel());
        }

        var user = await _db.NguoiDungs
            .Include(x => x.MaQuyenNavigation)
            .FirstOrDefaultAsync(x => x.Email != null && x.Email.ToLower() == email);

        if (user == null)
        {
            var customerRole = await _db.PhanQuyens.FirstOrDefaultAsync(x => x.MaQuyen == 3);

            if (customerRole == null)
            {
                await HttpContext.SignOutAsync("External");
                ModelState.AddModelError("", "Hệ thống chưa có quyền Khách hàng (Mã quyền 3).");
                return View("Login", new LoginViewModel());
            }

            var fullName = externalResult.Principal.FindFirstValue(ClaimTypes.Name);
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

            user.MatKhau = _passwordHasher.HashPassword(user, Guid.NewGuid().ToString("N"));

            _db.NguoiDungs.Add(user);
            await _db.SaveChangesAsync();
        }

        if (!user.TrangThai)
        {
            await HttpContext.SignOutAsync("External");
            ModelState.AddModelError("", "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
            return View("Login", new LoginViewModel());
        }

        await SignInUserAsync(user, true);
        await HttpContext.SignOutAsync("External");

        // ÉP CHUYỂN HƯỚNG TRỰC TIẾP
        if (IsAdmin(user))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        if (IsStaff(user))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Staff" });
        }

        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction("Index", "Home");
    }

    // =====================================================
    // HELPER METHODS
    // =====================================================
    // Tránh lỗi HTTP 500 khi hash mật khẩu không lưu được.
    // Lưu ý: cần xem log Render/PostgreSQL để tìm nguyên nhân gốc.
    private async Task<bool> TrySavePasswordHashAsync(NguoiDung user)
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex,
                "Password hash update affected no rows. UserId={UserId}",
                user.MaNguoiDung);
            ModelState.AddModelError("",
                "Không thể cập nhật thông tin tài khoản. Vui lòng thử lại hoặc liên hệ hỗ trợ.");
            return false;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error while updating password hash. UserId={UserId}",
                user.MaNguoiDung);
            ModelState.AddModelError("",
                "Có lỗi lưu thông tin tài khoản. Vui lòng thử lại sau.");
            return false;
        }
    }

    private void HandleFailedLogin()
    {
        int failedAttempts = HttpContext.Session.GetInt32("FailedPasswordCount") ?? 0;
        failedAttempts++;
        HttpContext.Session.SetInt32("FailedPasswordCount", failedAttempts);

        if (failedAttempts >= 5)
        {
            var lockUntil = DateTime.Now.AddMinutes(15);
            HttpContext.Session.SetString("LockUntil", lockUntil.Ticks.ToString());
            ModelState.AddModelError("", "Bạn đã nhập sai mật khẩu 5 lần liên tiếp. Tạm thời bị khóa 15 phút.");
        }
        else if (failedAttempts >= 3)
        {
            int remaining = 5 - failedAttempts;
            ModelState.AddModelError("", $"Tài khoản hoặc mật khẩu không đúng. Bạn còn {remaining} lần thử.");
        }
        else
        {
            ModelState.AddModelError("", "Tài khoản hoặc mật khẩu không đúng.");
        }
    }

    private async Task SignInUserAsync(NguoiDung user, bool rememberMe)
    {
        var roleName = user.MaQuyenNavigation?.TenQuyen ?? (user.MaQuyen switch
        {
            1 => "Quản trị viên",
            2 => "Nhân viên",
            _ => "Khách hàng"
        });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.MaNguoiDung.ToString()),
            new(ClaimTypes.Name, user.TenNguoiDung ?? "Người dùng"),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Role, roleName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
            });
    }

    private static string? GetProviderScheme(string provider)
    {
        if (string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
            return GoogleDefaults.AuthenticationScheme;

        if (string.Equals(provider, "Facebook", StringComparison.OrdinalIgnoreCase))
            return FacebookDefaults.AuthenticationScheme;

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
        {
            return false;
        }

        var captchaId = _configuration[$"GeeTest:{action}:CaptchaId"];
        var captchaKey = _configuration[$"GeeTest:{action}:CaptchaKey"];

        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(captchaKey))
        {
            return false;
        }

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

    private static string NormalizePhone(string phone)
    {
        var result = Regex.Replace(phone, @"[\s\-.()]", "");

        if (result.StartsWith("+84"))
        {
            result = "0" + result[3..];
        }

        return result;
    }

    private static bool IsAdmin(NguoiDung user)
    {
        var roleName = user.MaQuyenNavigation?.TenQuyen;
        return user.MaQuyen == 1 || roleName is "QuanTriVien" or "Quản trị viên" or "Admin";
    }

    private static bool IsStaff(NguoiDung user)
    {
        var roleName = user.MaQuyenNavigation?.TenQuyen;
        return user.MaQuyen == 2 || roleName is "NhanVien" or "Nhân viên";
    }

    private sealed class GeeTestValidationResult
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }
}