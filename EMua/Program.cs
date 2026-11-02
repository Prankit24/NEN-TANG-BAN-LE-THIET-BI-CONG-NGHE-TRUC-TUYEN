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

// =====================================================
// EMAIL / EMAIL VERIFICATION
// =====================================================
builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<EmailVerificationService>();

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
// FORWARDED HEADERS (Tối ưu cho Somee / IIS Reverse Proxy)
// =====================================================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // Xóa KnownNetworks và KnownProxies để chấp nhận header từ proxy của Somee
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// =====================================================
// HTTPS REDIRECTION
// =====================================================
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
});

// =====================================================
// DATABASE - POSTGRESQL / SUPABASE
// =====================================================
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Chưa có ConnectionStrings:DefaultConnection trong cấu hình.");
}

builder.Services.AddDbContext<EMuaDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
});

// =====================================================
// PASSWORD HASHER
// =====================================================
builder.Services.AddScoped<
    IPasswordHasher<NguoiDung>,
    PasswordHasher<NguoiDung>>();

// =====================================================
// REQUIRED CONFIGURATION
// =====================================================
string RequiredConfiguration(string key)
{
    var value = builder.Configuration[key];

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Chưa có {key} trong cấu hình.");
    }

    return value.Trim();
}

var googleClientId =
    RequiredConfiguration("Authentication:Google:ClientId");

var googleClientSecret =
    RequiredConfiguration("Authentication:Google:ClientSecret");

var facebookAppId =
    RequiredConfiguration("Authentication:Facebook:AppId");

var facebookAppSecret =
    RequiredConfiguration("Authentication:Facebook:AppSecret");

// =====================================================
// AUTHENTICATION
// =====================================================
builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)

    // Cookie đăng nhập chính
    .AddCookie(
        CookieAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Headers.Accept.ToString()
                        .Contains("application/json") ||
                    context.Request.ContentType?
                        .Contains("application/json") == true)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status401Unauthorized;
                }
                else
                {
                    context.Response.Redirect(context.RedirectUri);
                }

                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Headers.Accept.ToString()
                        .Contains("application/json") ||
                    context.Request.ContentType?
                        .Contains("application/json") == true)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status403Forbidden;
                }
                else
                {
                    context.Response.Redirect(context.RedirectUri);
                }

                return Task.CompletedTask;
            };

            options.LoginPath = "/Auth/Login";
            options.AccessDeniedPath = "/Auth/Login";

            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;

            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;
        })

    // Cookie tạm cho đăng nhập Google/Facebook
    .AddCookie("External", options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);

        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy =
            CookieSecurePolicy.Always;
    })

    // GOOGLE LOGIN
    .AddGoogle(
        GoogleDefaults.AuthenticationScheme,
        options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;

            options.SignInScheme = "External";
            options.CallbackPath = "/signin-google";

            options.CorrelationCookie.HttpOnly = true;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.CorrelationCookie.SecurePolicy =
                CookieSecurePolicy.Always;
        })

    // FACEBOOK LOGIN
    .AddFacebook(
        FacebookDefaults.AuthenticationScheme,
        options =>
        {
            options.AppId = facebookAppId;
            options.AppSecret = facebookAppSecret;

            options.SignInScheme = "External";
            options.CallbackPath = "/signin-facebook";

            options.Scope.Add("email");

            options.Fields.Add("id");
            options.Fields.Add("name");
            options.Fields.Add("email");

            options.CorrelationCookie.HttpOnly = true;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.CorrelationCookie.SecurePolicy =
                CookieSecurePolicy.Always;
        });

var app = builder.Build();

// =====================================================
// ERROR HANDLING
// =====================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// =====================================================
// FORWARDED HEADERS (Phải đặt ở đầu tiên trong pipeline)
// =====================================================
app.UseForwardedHeaders();

// =====================================================
// HSTS
// =====================================================
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

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
    if (!File.Exists(domainDataPath))
    {
        throw new FileNotFoundException(
            "Thiếu dữ liệu huấn luyện AI. Hãy upload " +
            "Data/AITraining/domain-data.csv hoặc model " +
            "MLModels/domain-model.zip.",
            domainDataPath);
    }

    Directory.CreateDirectory(
        Path.GetDirectoryName(domainModelPath)!);

    DomainModelTrainer.Train(
        domainDataPath,
        domainModelPath);
}

// =====================================================
// MIDDLEWARE PIPELINE
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
    pattern:
        "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");

app.Run();