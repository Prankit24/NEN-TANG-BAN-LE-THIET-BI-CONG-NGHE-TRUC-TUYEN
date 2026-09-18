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

        // Luôn chuẩn hóa email về chữ thường
        var email = model.Email
            .Trim()
            .ToLowerInvariant();

        var phone = NormalizePhone(model.PhoneNumber);

        // =====================================================
        // KIỂM TRA EMAIL
        // Không phân biệt hoa / thường
        // =====================================================
        var emailExists = await _db.NguoiDungs
            .AnyAsync(x =>
                x.Email != null &&
                x.Email.ToLower() == email);

        if (emailExists)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Email này đã được sử dụng.");
        }

        // =====================================================
        // KIỂM TRA SỐ ĐIỆN THOẠI
        // =====================================================
        var phoneExists = await _db.NguoiDungs
            .AnyAsync(x =>
                x.SoDienThoai == phone);

        if (phoneExists)
        {
            ModelState.AddModelError(
                nameof(model.PhoneNumber),
                "Số điện thoại này đã được sử dụng.");
        }

        // =====================================================
        // KIỂM TRA QUYỀN KHÁCH HÀNG
        // MaQuyen = 3
        // =====================================================
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

        // =====================================================
        // TẠO NGƯỜI DÙNG
        // =====================================================
        var user = new NguoiDung
        {
            TenNguoiDung = model.FullName.Trim(),

            // Lưu email dạng lowercase từ đây trở đi
            Email = email,

            SoDienThoai = phone,

            TenDangNhap =
                $"kh_{Guid.NewGuid():N}"[..15],

            MaQuyen = 3,
            TrangThai = true,
            NgayTao = DateTime.Now
        };

        // Hash mật khẩu
        user.MatKhau =
            _passwordHasher.HashPassword(
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

    // =====================================================
    // LOGIN - GET
    // =====================================================
    [HttpGet]
    public IActionResult Login(
        string? returnUrl = null)
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

        if (!ModelState.IsValid)
            return View(model);

        // =====================================================
        // GEETEST
        // =====================================================
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

        // =====================================================
        // EMAIL HOẶC SỐ ĐIỆN THOẠI
        // =====================================================
        var identifier = model.Identifier.Trim();

        NguoiDung? user;

        // =====================================================
        // ĐĂNG NHẬP BẰNG EMAIL
        // =====================================================
        if (identifier.Contains('@'))
        {
            var email =
                identifier.ToLowerInvariant();

            // QUAN TRỌNG:
            // PostgreSQL phân biệt hoa / thường khi dùng =
            //
            // Ví dụ:
            // Thaovy@gmail.com
            // thaovy@gmail.com
            //
            // Vì vậy phải đưa cả DB email về lowercase
            // khi so sánh.
            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x =>
                    x.Email != null &&
                    x.Email.ToLower() == email);
        }
        else
        {
            // =================================================
            // ĐĂNG NHẬP BẰNG SỐ ĐIỆN THOẠI
            // =================================================
            var phone =
                NormalizePhone(identifier);

            user = await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x =>
                    x.SoDienThoai == phone);
        }

        // =====================================================
        // KHÔNG TÌM THẤY USER
        // =====================================================
        if (user == null ||
            string.IsNullOrWhiteSpace(user.MatKhau))
        {
            ModelState.AddModelError(
                "",
                "Email, số điện thoại hoặc mật khẩu không đúng.");

            return View(model);
        }

        // =====================================================
        // KIỂM TRA MẬT KHẨU
        // =====================================================
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
            // =================================================
            // HỖ TRỢ TÀI KHOẢN CŨ
            // DB cũ có thể lưu password dạng:
            //
            // 123456
            //
            // thay vì hash.
            //
            // Nếu password đúng thì tự động chuyển sang hash.
            // =================================================
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

        // =====================================================
        // SAI PASSWORD
        // =====================================================
        if (verifyResult ==
            PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(
                "",
                "Email, số điện thoại hoặc mật khẩu không đúng.");

            return View(model);
        }

        // =====================================================
        // TÀI KHOẢN BỊ KHÓA
        // =====================================================
        if (!user.TrangThai)
        {
            ModelState.AddModelError(
                "",
                "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

            return View(model);
        }

        // =====================================================
        // HASH CŨ → HASH LẠI
        // =====================================================
        if (verifyResult ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.MatKhau =
                _passwordHasher.HashPassword(
                    user,
                    model.Password);

            await _db.SaveChangesAsync();
        }

        // =====================================================
        // ĐĂNG NHẬP THÀNH CÔNG
        // =====================================================
        await SignInUserAsync(
            user,
            model.RememberMe);

        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction(
            "Index",
            "Home");
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

        return RedirectToAction(
            "Index",
            "Home");
    }

    // =====================================================
    // EXTERNAL LOGIN
    // GOOGLE / FACEBOOK
    // =====================================================
    [HttpGet]
    public IActionResult ExternalLogin(
        string provider,
        string? returnUrl = null)
    {
        var providerScheme =
            GetProviderScheme(provider);

        if (providerScheme == null)
            return RedirectToAction(nameof(Login));

        var redirectUrl = Url.Action(
            nameof(ExternalLoginCallback),
            "Auth",
            new
            {
                provider,
                returnUrl
            });

        var properties =
            new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

        return Challenge(
            properties,
            providerScheme);
    }

    // =====================================================
    // EXTERNAL LOGIN CALLBACK
    // =====================================================
    [HttpGet]
    public async Task<IActionResult>
        ExternalLoginCallback(
            string? provider = null,
            string? returnUrl = null,
            string? remoteError = null)
    {
        var providerName =
            string.Equals(
                provider,
                "Facebook",
                StringComparison.OrdinalIgnoreCase)
                ? "Facebook"
                : "Google";

        // =====================================================
        // PROVIDER ERROR
        // =====================================================
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ModelState.AddModelError(
                "",
                $"Đăng nhập {providerName} không thành công.");

            return View(
                "Login",
                new LoginViewModel());
        }

        // =====================================================
        // LẤY EXTERNAL USER
        // =====================================================
        var externalResult =
            await HttpContext.AuthenticateAsync(
                "External");

        if (!externalResult.Succeeded ||
            externalResult.Principal == null)
        {
            ModelState.AddModelError(
                "",
                $"Không thể lấy thông tin tài khoản {providerName}.");

            return View(
                "Login",
                new LoginViewModel());
        }

        // =====================================================
        // PROVIDER ID
        // =====================================================
        var providerId =
            externalResult.Principal
                .FindFirstValue(
                    ClaimTypes.NameIdentifier);

        // =====================================================
        // EMAIL
        // =====================================================
        var email =
            externalResult.Principal
                .FindFirstValue(
                    ClaimTypes.Email)?
                .Trim()
                .ToLowerInvariant();

        // =====================================================
        // FACEBOOK ĐÔI KHI KHÔNG TRẢ EMAIL
        // =====================================================
        if (string.IsNullOrWhiteSpace(email) &&
            providerName == "Facebook" &&
            !string.IsNullOrWhiteSpace(providerId))
        {
            email =
                $"facebook_{providerId}@social.emua.local";
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync(
                "External");

            ModelState.AddModelError(
                "",
                $"Không thể lấy thông tin tài khoản {providerName}.");

            return View(
                "Login",
                new LoginViewModel());
        }

        // =====================================================
        // TÌM USER
        // Không phân biệt hoa / thường
        // =====================================================
        var user =
            await _db.NguoiDungs
                .Include(x => x.MaQuyenNavigation)
                .FirstOrDefaultAsync(x =>
                    x.Email != null &&
                    x.Email.ToLower() == email);

        // =====================================================
        // LẦN ĐẦU LOGIN GOOGLE / FACEBOOK
        // =====================================================
        if (user == null)
        {
            var customerRole =
                await _db.PhanQuyens
                    .FirstOrDefaultAsync(
                        x => x.MaQuyen == 3);

            if (customerRole == null)
            {
                await HttpContext.SignOutAsync(
                    "External");

                ModelState.AddModelError(
                    "",
                    "Hệ thống chưa có quyền Khách hàng (Mã quyền 3).");

                return View(
                    "Login",
                    new LoginViewModel());
            }

            // =================================================
            // FULL NAME
            // =================================================
            var fullName =
                externalResult.Principal
                    .FindFirstValue(
                        ClaimTypes.Name);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName =
                    email.Split('@')[0];
            }

            var usernamePrefix =
                providerName == "Facebook"
                    ? "fb"
                    : "gg";

            user = new NguoiDung
            {
                TenNguoiDung = fullName,
                Email = email,
                SoDienThoai = null,

                TenDangNhap =
                    $"{usernamePrefix}_{Guid.NewGuid():N}"[..15],

                MaQuyen = 3,
                MaQuyenNavigation = customerRole,

                TrangThai = true,
                NgayTao = DateTime.Now
            };

            // =================================================
            // SOCIAL ACCOUNT VẪN CÓ HASH PASSWORD
            // =================================================
            user.MatKhau =
                _passwordHasher.HashPassword(
                    user,
                    Guid.NewGuid().ToString("N"));

            _db.NguoiDungs.Add(user);

            await _db.SaveChangesAsync();
        }

        // =====================================================
        // ACCOUNT LOCKED
        // =====================================================
        if (!user.TrangThai)
        {
            await HttpContext.SignOutAsync(
                "External");

            ModelState.AddModelError(
                "",
                "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

            return View(
                "Login",
                new LoginViewModel());
        }

        // =====================================================
        // LOGIN
        // =====================================================
        await SignInUserAsync(
            user,
            true);

        await HttpContext.SignOutAsync(
            "External");

        if (Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl!);

        return RedirectToAction(
            "Index",
            "Home");
    }

    // =====================================================
    // SIGN IN USER
    // =====================================================
    private async Task SignInUserAsync(
        NguoiDung user,
        bool rememberMe)
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    user.MaNguoiDung.ToString()),

                new(
                    ClaimTypes.Name,
                    user.TenNguoiDung ??
                    "Khách hàng"),

                new(
                    ClaimTypes.Email,
                    user.Email ??
                    string.Empty),

                new(
                    ClaimTypes.Role,
                    user.MaQuyenNavigation?.TenQuyen
                    ?? "KhachHang")
            };

        var identity =
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,

            new ClaimsPrincipal(identity),

            new AuthenticationProperties
            {
                IsPersistent = rememberMe,

                ExpiresUtc =
                    rememberMe
                        ? DateTimeOffset.UtcNow
                            .AddDays(7)
                        : null
            });
    }

    // =====================================================
    // GET PROVIDER
    // =====================================================
    private static string?
        GetProviderScheme(
            string provider)
    {
        if (string.Equals(
                provider,
                "Google",
                StringComparison.OrdinalIgnoreCase))
        {
            return GoogleDefaults
                .AuthenticationScheme;
        }

        if (string.Equals(
                provider,
                "Facebook",
                StringComparison.OrdinalIgnoreCase))
        {
            return FacebookDefaults
                .AuthenticationScheme;
        }

        return null;
    }

    // =====================================================
    // GEETEST VERIFY
    // =====================================================
    private async Task<bool>
        VerifyGeeTestAsync(
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

        var captchaId =
            _configuration[
                $"GeeTest:{action}:CaptchaId"];

        var captchaKey =
            _configuration[
                $"GeeTest:{action}:CaptchaKey"];

        if (string.IsNullOrWhiteSpace(captchaId) ||
            string.IsNullOrWhiteSpace(captchaKey))
        {
            return false;
        }

        // =====================================================
        // SIGN TOKEN
        // =====================================================
        using var hmac =
            new HMACSHA256(
                Encoding.UTF8.GetBytes(
                    captchaKey));

        var signToken =
            Convert.ToHexString(
                    hmac.ComputeHash(
                        Encoding.UTF8.GetBytes(
                            lotNumber)))
                .ToLowerInvariant();

        var data =
            new Dictionary<string, string>
            {
                ["lot_number"] =
                    lotNumber,

                ["captcha_output"] =
                    captchaOutput,

                ["pass_token"] =
                    passToken,

                ["gen_time"] =
                    genTime,

                ["captcha_id"] =
                    captchaId,

                ["sign_token"] =
                    signToken
            };

        try
        {
            var client =
                _httpClientFactory
                    .CreateClient();

            var response =
                await client.PostAsync(
                    "https://gcaptcha4.geetest.com/validate",
                    new FormUrlEncodedContent(
                        data));

            var result =
                await response.Content
                    .ReadFromJsonAsync<
                        GeeTestValidationResult>();

            return
                response.IsSuccessStatusCode &&
                string.Equals(
                    result?.Result,
                    "success",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // =====================================================
    // GEETEST RESPONSE
    // =====================================================
    private sealed class
        GeeTestValidationResult
    {
        [JsonPropertyName("result")]
        public string?
            Result
        {
            get;
            set;
        }
    }

    // =====================================================
    // NORMALIZE PHONE
    // =====================================================
    private static string
        NormalizePhone(
            string phone)
    {
        var result =
            Regex.Replace(
                phone,
                @"[\s\-.()]",
                "");

        if (result.StartsWith("+84"))
        {
            result =
                "0" + result[3..];
        }

        return result;
    }
}