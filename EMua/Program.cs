using EMua.Data;
using EMua.Models.Database;
using EMua.Services.AI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// DATA PROTECTION
// Lưu encryption keys để Antiforgery Token và Cookie
// không bị mất sau khi ứng dụng restart.
// =====================================================
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo("/app/data-protection-keys"))
    .SetApplicationName("EMua");

// =====================================================
// MVC
// =====================================================
builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();

// =====================================================
// FORWARDED HEADERS - RENDER
// =====================================================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// =====================================================
// DATABASE - POSTGRESQL
// =====================================================
builder.Services.AddDbContext<EMuaDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

// =====================================================
// PASSWORD HASHER
// =====================================================
builder.Services.AddScoped<IPasswordHasher<NguoiDung>,
    PasswordHasher<NguoiDung>>();

// =====================================================
// AUTHENTICATION
// =====================================================
builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)

    // -------------------------
    // Cookie Authentication
    // -------------------------
    .AddCookie(
        CookieAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.LoginPath = "/Auth/Login";
            options.AccessDeniedPath = "/Auth/Login";

            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
        })

    // -------------------------
    // External Login Cookie
    // -------------------------
    .AddCookie("External")

    // =================================================
    // GOOGLE LOGIN
    // =================================================
    .AddGoogle(
        GoogleDefaults.AuthenticationScheme,
        options =>
        {
            options.ClientId =
                builder.Configuration[
                    "Authentication:Google:ClientId"]
                ?? throw new InvalidOperationException(
                    "Chưa có Google ClientId trong cấu hình.");

            options.ClientSecret =
                builder.Configuration[
                    "Authentication:Google:ClientSecret"]
                ?? throw new InvalidOperationException(
                    "Chưa có Google ClientSecret trong cấu hình.");

            options.SignInScheme = "External";

            options.CallbackPath = "/signin-google";
        })

    // =================================================
    // FACEBOOK LOGIN
    // =================================================
    .AddFacebook(
        FacebookDefaults.AuthenticationScheme,
        options =>
        {
            options.AppId =
                builder.Configuration[
                    "Authentication:Facebook:AppId"]
                ?? throw new InvalidOperationException(
                    "Chưa có Facebook AppId trong cấu hình.");

            options.AppSecret =
                builder.Configuration[
                    "Authentication:Facebook:AppSecret"]
                ?? throw new InvalidOperationException(
                    "Chưa có Facebook AppSecret trong cấu hình.");

            options.SignInScheme = "External";

            options.CallbackPath = "/signin-facebook";

            options.Scope.Add("email");

            options.Fields.Add("id");
            options.Fields.Add("name");
            options.Fields.Add("email");
        });

// =====================================================
// AI - DOMAIN CLASSIFIER
// =====================================================
builder.Services.AddSingleton<DomainClassifier>();

// =====================================================
// GEMINI AI
// =====================================================
builder.Services.AddHttpClient<GeminiService>();

// =====================================================
// BUILD APPLICATION
// =====================================================
var app = builder.Build();

// =====================================================
// FORWARDED HEADERS
// =====================================================
app.UseForwardedHeaders();

// =====================================================
// AI TRAINING DATA
// =====================================================
var domainDataPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "AITraining",
    "domain-data.csv");

var domainModelPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "MLModels",
    "domain-model.zip");

// =====================================================
// TRAIN MODEL IF NOT EXISTS
// =====================================================
if (!File.Exists(domainModelPath))
{
    DomainModelTrainer.Train(
        domainDataPath,
        domainModelPath);
}

<<<<<<< Updated upstream
// =====================================================
// ERROR HANDLING
// =====================================================
=======
// Tạo dữ liệu đăng nhập thử nghiệm cho khu vực Nhân viên khi phát triển.
// Không ghi đè tài khoản nếu quản trị viên đã thay đổi hoặc xóa dữ liệu này.
if (app.Environment.IsDevelopment())
{
    await SeedDevelopmentStaffAccountAsync(app);
}

>>>>>>> Stashed changes
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

// =====================================================
// HTTPS
// =====================================================
app.UseHttpsRedirection();

// =====================================================
// STATIC FILES
// =====================================================
app.UseStaticFiles();

// =====================================================
// ROUTING
// =====================================================
app.UseRouting();

// =====================================================
// AUTHENTICATION
// =====================================================
app.UseAuthentication();

// =====================================================
// AUTHORIZATION
// =====================================================
app.UseAuthorization();

// =====================================================
// AREA ROUTE
// =====================================================
app.MapControllerRoute(
    name: "areas",
    pattern:
        "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

// =====================================================
// DEFAULT ROUTE
// =====================================================
app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");

<<<<<<< Updated upstream
// =====================================================
// RUN
// =====================================================
app.Run();
=======
app.Run();

static async Task SeedDevelopmentStaffAccountAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<EMuaDbContext>();
    var passwordHasher = scope.ServiceProvider
        .GetRequiredService<IPasswordHasher<NguoiDung>>();

    var staffRole = await db.PhanQuyens
        .FirstOrDefaultAsync(x =>
            x.TenQuyen == "NhanVien" ||
            x.TenQuyen == "Nhân viên");

    if (staffRole == null)
    {
        staffRole = new PhanQuyen
        {
            TenQuyen = "NhanVien",
            MoTa = "Nhân viên quản lý bán hàng"
        };

        db.PhanQuyens.Add(staffRole);
        await db.SaveChangesAsync();
    }

    const string staffEmail = "nhanvien@emua.local";

    var staffUser = await db.NguoiDungs
        .FirstOrDefaultAsync(x => x.Email == staffEmail);

    if (staffUser != null)
        return;

    staffUser = new NguoiDung
    {
        TenDangNhap = "nhanvien",
        TenNguoiDung = "Nhân viên EMUA",
        Email = staffEmail,
        SoDienThoai = "0900000000",
        MaQuyen = staffRole.MaQuyen,
        TrangThai = true,
        NgayTao = DateTime.Now
    };

    staffUser.MatKhau = passwordHasher.HashPassword(
        staffUser,
        "NhanVien@123");

    db.NguoiDungs.Add(staffUser);
    await db.SaveChangesAsync();
}
>>>>>>> Stashed changes
