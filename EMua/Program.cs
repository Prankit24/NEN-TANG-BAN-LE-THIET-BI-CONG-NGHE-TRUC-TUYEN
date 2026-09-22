using EMua.Data;
using EMua.Models.Database;
using EMua.Services.AI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<EMuaDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));

// Đăng ký Service PasswordHasher cho NguoiDung
builder.Services.AddScoped<IPasswordHasher<NguoiDung>, PasswordHasher<NguoiDung>>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    })
    .AddCookie("External")
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId =
            builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException(
                "Chưa có Google ClientId trong cấu hình.");

        options.ClientSecret =
            builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Chưa có Google ClientSecret trong cấu hình.");

        options.SignInScheme = "External";
        options.CallbackPath = "/signin-google";
    })
    .AddFacebook(FacebookDefaults.AuthenticationScheme, options =>
    {
        options.AppId =
            builder.Configuration["Authentication:Facebook:AppId"]
            ?? throw new InvalidOperationException(
                "Chưa có Facebook AppId trong cấu hình.");

        options.AppSecret =
            builder.Configuration["Authentication:Facebook:AppSecret"]
            ?? throw new InvalidOperationException(
                "Chưa có Facebook AppSecret trong cấu hình.");

        options.SignInScheme = "External";
        options.CallbackPath = "/signin-facebook";

        options.Scope.Add("email");
        options.Fields.Add("id");
        options.Fields.Add("name");
        options.Fields.Add("email");
    });

builder.Services.AddSingleton<DomainClassifier>();
builder.Services.AddHttpClient<GeminiService>();

var app = builder.Build();

var domainDataPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data", "AITraining", "domain-data.csv");

var domainModelPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "MLModels", "domain-model.zip");

if (!File.Exists(domainModelPath))
{
    DomainModelTrainer.Train(domainDataPath, domainModelPath);
}

if (app.Environment.IsDevelopment())
{
    await SeedDevelopmentStaffAccountAsync(app);
}

// =====================================================
// ERROR HANDLING & MIDDLEWARE
// =====================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// =====================================================
// RUN
// =====================================================
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