using EMua.Data;
using EMua.Models.Database;
using EMua.Services;
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
// =====================================================
builder.Services.AddDataProtection()
    .SetApplicationName("EMua");

builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();

// =====================================================
// SESSION
// =====================================================
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// =====================================================
// AI SERVICES
// =====================================================
builder.Services.AddSingleton<DomainClassifier>();
builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddScoped<RecommendationService>();

// =====================================================
// MVC / HTTP CLIENT
// =====================================================
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

// =====================================================
// FORWARDED HEADERS
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
builder.Services.AddScoped<
    IPasswordHasher<NguoiDung>,
    PasswordHasher<NguoiDung>>();

// =====================================================
// AUTHENTICATION
// =====================================================
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

var app = builder.Build();

// =====================================================
// KIỂM TRA CẤU HÌNH - KHÔNG GHI GIÁ TRỊ BÍ MẬT
// Dùng LogWarning để xuất hiện với mức log hiện tại.
// =====================================================
var configPath = Path.Combine(
    app.Environment.ContentRootPath,
    "appsettings.json");

var productionConfigPath = Path.Combine(
    app.Environment.ContentRootPath,
    "appsettings.Production.json");

app.Logger.LogWarning(
    "CONFIG DIAGNOSTICS VERSION: EMUA-CONFIG-01");

app.Logger.LogWarning(
    "CONFIG ROOT: {ContentRoot}",
    app.Environment.ContentRootPath);

app.Logger.LogWarning(
    "CONFIG ENVIRONMENT: {EnvironmentName}",
    app.Environment.EnvironmentName);

app.Logger.LogWarning(
    "CONFIG FILE EXISTS: {Exists}",
    File.Exists(configPath));

app.Logger.LogWarning(
    "PRODUCTION CONFIG FILE EXISTS: {Exists}",
    File.Exists(productionConfigPath));

app.Logger.LogWarning(
    "GOOGLE CLIENT ID PRESENT: {Present}",
    !string.IsNullOrWhiteSpace(
        app.Configuration["Authentication:Google:ClientId"]));

app.Logger.LogWarning(
    "GOOGLE CLIENT SECRET PRESENT: {Present}",
    !string.IsNullOrWhiteSpace(
        app.Configuration["Authentication:Google:ClientSecret"]));

// Kiểm tra riêng appsettings.json để phân biệt:
// file không chứa ClientId hay cấu hình sau đó ghi đè.
if (File.Exists(configPath))
{
    try
    {
        var fileConfig = new ConfigurationBuilder()
    .SetBasePath(app.Environment.ContentRootPath)
    .AddJsonFile(
        "appsettings.json",
        optional: false,
        reloadOnChange: false)
    .Build();

        app.Logger.LogWarning(
            "GOOGLE CLIENT ID IN APPSETTINGS FILE: {Present}",
            !string.IsNullOrWhiteSpace(
                fileConfig["Authentication:Google:ClientId"]));
    }
    catch (Exception ex)
    {
        // Chỉ ghi loại lỗi để tránh đưa nội dung cấu hình vào log.
        app.Logger.LogWarning(
            "APPSETTINGS READ FAILED: {ExceptionType}",
            ex.GetType().Name);
    }
}

// =====================================================
// FORWARDED HEADERS
// =====================================================
app.UseForwardedHeaders();

// =====================================================
// AI TRAINING DATA
// =====================================================
var domainDataPath = Path.Combine(
    app.Environment.ContentRootPath,
    "Data",
    "AITraining",
    "domain-data.csv");

var domainModelPath = Path.Combine(
    app.Environment.ContentRootPath,
    "MLModels",
    "domain-model.zip");

if (!File.Exists(domainModelPath))
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(domainModelPath)!);

    DomainModelTrainer.Train(
        domainDataPath,
        domainModelPath);
}

// =====================================================
// ERROR HANDLING
// =====================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// =====================================================
// MIDDLEWARE
// =====================================================
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// =====================================================
// ROUTES
// =====================================================
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();