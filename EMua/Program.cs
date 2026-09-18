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

// =====================================================
// ERROR HANDLING
// =====================================================
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

// =====================================================
// RUN
// =====================================================
app.Run();