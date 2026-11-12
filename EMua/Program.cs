
using System.Diagnostics;
using System.IO.Compression;

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
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

// ============================================================
// EMUA - PROGRAM.CS
// ASP.NET Core MVC 8
//
// Chức năng:
// 1. Đăng ký các dịch vụ của ứng dụng
// 2. Kết nối PostgreSQL Supabase
// 3. Cấu hình Authentication và Session
// 4. Đăng ký các dịch vụ AI
// 5. Tối ưu Response Compression
// 6. Theo dõi request chậm
// 7. Cấu hình middleware và routing
//
// Lưu ý:
// - Không thay đổi nghiệp vụ bán hàng.
// - Không thay đổi cấu trúc database.
// - Không tự động chạy migration.
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. MEMORY CACHE
// ============================================================
//
// Dùng để cache dữ liệu thường xuyên được truy cập.
//
// Ví dụ:
// - Danh mục sản phẩm
// - Thương hiệu
// - Dữ liệu công khai của trang chủ
//
// Chỉ đăng ký dịch vụ tại đây.
// Controller phải sử dụng IMemoryCache thì mới có hiệu quả.
//
// Cache lưu trong RAM của từng instance.
// Khi container khởi động lại, dữ liệu cache sẽ mất.
// ============================================================

builder.Services.AddMemoryCache();

// ============================================================
// 2. DATA PROTECTION
// ============================================================
//
// Dùng để bảo vệ dữ liệu như authentication cookie.
//
// Giữ nguyên ApplicationName của hệ thống hiện tại.
//
// CẢNH BÁO:
// Cấu hình này chưa thiết lập nơi lưu key bền vững.
// Khi Render thay container, cookie cũ có thể mất hiệu lực.
//
// Không tự ý chuyển key sang thư mục tạm trong container.
// ============================================================

builder.Services.AddDataProtection()
    .SetApplicationName("EMua");

// ============================================================
// 3. EMAIL SERVICES
// ============================================================
//
// Giữ nguyên dịch vụ gửi email hiện tại.
//
// IAppEmailSender:
// Interface gửi email của EMUA.
//
// SmtpEmailSender:
// Implementation sử dụng SMTP.
//
// EmailVerificationService:
// Xử lý nghiệp vụ xác minh email.
//
// Lưu ý:
// Render Free có giới hạn kết nối SMTP.
// Response Compression không giải quyết lỗi SMTP.
// ============================================================

builder.Services.AddScoped<IAppEmailSender, SmtpEmailSender>();

builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<ISupplierQuotationService, SupplierQuotationService>();
builder.Services.AddScoped<EMua.Services.ImportOrderReceivingService>();

// ============================================================
// 4. SESSION
// ============================================================
//
// Session phục vụ các chức năng đang sử dụng Session.
//
// IdleTimeout:
// Session hết hạn sau 20 phút không hoạt động.
//
// HttpOnly:
// JavaScript không thể đọc trực tiếp session cookie.
//
// IsEssential:
// Đánh dấu cookie cần thiết cho hoạt động ứng dụng.
//
// DistributedMemoryCache ở đây thực tế vẫn lưu trong RAM
// của tiến trình, không phải Redis.
// ============================================================

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);

    options.Cookie.HttpOnly = true;

    options.Cookie.IsEssential = true;
});

// ============================================================
// 5. AI SERVICES
// ============================================================
//
// DomainClassifier:
// Dịch vụ phân loại câu hỏi thuộc lĩnh vực công nghệ.
//
// GeminiService:
// Dịch vụ gọi Gemini API (provider chính).
//
// GroqService:
// Dịch vụ gọi Groq API (provider dự phòng).
// API Key đọc từ Groq:ApiKey (User Secrets).
// Model đọc từ Groq:Model (appsettings.json).
//
// AIServiceOrchestrator:
// Cơ chế fallback: Gemini → Groq khi gặp lỗi tạm thời.
// Không chuyển provider khi lỗi cấu hình hoặc dữ liệu.
//
// RecommendationService:
// Dịch vụ tư vấn/gợi ý sản phẩm.
//
// AddHttpClient<T>() sử dụng HttpClientFactory
// để quản lý HttpClient hiệu quả hơn.
// ============================================================

builder.Services.AddSingleton<DomainClassifier>();

builder.Services.AddHttpClient<GeminiService>(client =>
{
    // Giới hạn timeout mỗi request Gemini là 15 giây để tránh treo 100s mặc định của HttpClient
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<GroqService>(client =>
{
    // Giới hạn timeout mỗi request Groq là 15 giây
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddScoped<AIServiceOrchestrator>();

builder.Services.AddScoped<RecommendationService>();

// ============================================================
// 6. MVC VÀ HTTP CLIENT
// ============================================================
//
// AddControllersWithViews:
// Đăng ký ASP.NET Core MVC và Razor Views.
//
// AddHttpClient:
// Cho phép các dịch vụ khác sử dụng IHttpClientFactory.
// ============================================================

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();

// ============================================================
// 7. RESPONSE COMPRESSION
// ============================================================
//
// MỤC ĐÍCH:
// Giảm dung lượng dữ liệu HTTP trả về trình duyệt.
//
// Brotli:
// Thuật toán nén tốt cho tài nguyên web.
//
// Gzip:
// Thuật toán nén tương thích rộng.
//
// CompressionLevel.Fastest:
// Ưu tiên giảm chi phí CPU khi nén.
//
// EnableForHttps:
// Cho phép nén phản hồi HTTPS.
//
// LƯU Ý BẢO MẬT:
// Không nên nén các phản hồi chứa dữ liệu bí mật
// cùng với dữ liệu do người dùng kiểm soát.
//
// Cần kiểm tra Content-Encoding thực tế sau deploy.
// ============================================================

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;

    options.Providers.Add<BrotliCompressionProvider>();

    options.Providers.Add<GzipCompressionProvider>();

    // Chỉ nén các MIME type mặc định của ASP.NET Core.
    // Không tự thêm image/jpeg, image/png, image/webp.
});

builder.Services.Configure<BrotliCompressionProviderOptions>(
    options =>
    {
        options.Level = CompressionLevel.Fastest;
    });

builder.Services.Configure<GzipCompressionProviderOptions>(
    options =>
    {
        options.Level = CompressionLevel.Fastest;
    });

// ============================================================
// 8. FORWARDED HEADERS
// ============================================================
//
// Render sử dụng reverse proxy phía trước ứng dụng.
//
// X-Forwarded-For:
// Thông tin IP gốc của client.
//
// X-Forwarded-Proto:
// Thông tin giao thức HTTP/HTTPS ban đầu.
//
// GIỮ NGUYÊN CẤU HÌNH TỪ SOURCE GỐC.
//
// CẢNH BÁO BẢO MẬT:
// Clear KnownNetworks và KnownProxies khiến ứng dụng
// không giới hạn proxy tin cậy.
//
// Không nên sử dụng lâu dài trong Production.
// Cần xác định proxy tin cậy của môi trường Render
// trước khi thay bằng cấu hình giới hạn phù hợp.
// ============================================================

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();

    options.KnownProxies.Clear();
});

// ============================================================
// 9. HTTPS REDIRECTION
// ============================================================
//
// Giữ nguyên cổng HTTPS từ source hiện tại.
//
// Trên Render, HTTPS được xử lý tại reverse proxy.
// Forwarded Headers cần chạy trước HTTPS Redirection.
// ============================================================

builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
});

// ============================================================
// 10. DATABASE - POSTGRESQL SUPABASE
// ============================================================
//
// Đọc connection string từ configuration.
//
// Có thể cung cấp qua environment variable:
//
// ConnectionStrings__DefaultConnection
//
// Không ghi mật khẩu trực tiếp trong Program.cs.
//
// EnableRetryOnFailure:
// Tự thử lại một số lỗi kết nối tạm thời.
//
// Lưu ý:
// Retry không giải quyết truy vấn SQL chậm.
// ============================================================

var connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Chưa có ConnectionStrings:DefaultConnection " +
        "trong cấu hình.");
}

builder.Services.AddDbContext<EMuaDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsqlOptions.CommandTimeout(30);
        });
});

// ============================================================
// 11. PASSWORD HASHER
// ============================================================
//
// Giữ nguyên cơ chế hash mật khẩu của EMUA.
//
// Không tự ý đổi thuật toán hoặc cấu hình hash
// vì có thể ảnh hưởng tài khoản hiện tại.
// ============================================================

builder.Services.AddScoped<
    IPasswordHasher<NguoiDung>,
    PasswordHasher<NguoiDung>>();

// ============================================================
// 12. REQUIRED CONFIGURATION
// ============================================================
//
// Hàm đọc các cấu hình bắt buộc.
//
// Nếu thiếu, ứng dụng sẽ báo lỗi khi khởi động.
//
// Giữ nguyên yêu cầu Google/Facebook từ source gốc.
//
// Khi muốn tắt đăng nhập mạng xã hội hoàn toàn,
// cần sửa đồng bộ Controller, View và cấu hình.
// ============================================================

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
    RequiredConfiguration(
        "Authentication:Google:ClientId");

var googleClientSecret =
    RequiredConfiguration(
        "Authentication:Google:ClientSecret");

var facebookAppId =
    RequiredConfiguration(
        "Authentication:Facebook:AppId");

var facebookAppSecret =
    RequiredConfiguration(
        "Authentication:Facebook:AppSecret");

// ============================================================
// 13. AUTHENTICATION
// ============================================================
//
// Giữ nguyên:
// - Cookie đăng nhập chính
// - External Cookie
// - Google Authentication
// - Facebook Authentication
//
// API trả về 401/403 thay vì redirect khi request JSON.
//
// CookieSecurePolicy.Always:
// Chỉ gửi authentication cookie qua HTTPS.
// ============================================================

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)

    // --------------------------------------------------------
    // 13.1. COOKIE ĐĂNG NHẬP CHÍNH
    // --------------------------------------------------------

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
                    context.Response.Redirect(
                        context.RedirectUri);
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
                    context.Response.Redirect(
                        context.RedirectUri);
                }

                return Task.CompletedTask;
            };

            options.LoginPath = "/Auth/Login";

            options.AccessDeniedPath = "/Auth/Login";

            options.ExpireTimeSpan =
                TimeSpan.FromMinutes(30);

            options.SlidingExpiration = true;

            options.Cookie.HttpOnly = true;

            options.Cookie.SameSite =
                SameSiteMode.Lax;

            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;
        })

    // --------------------------------------------------------
    // 13.2. EXTERNAL COOKIE
    // --------------------------------------------------------

    .AddCookie("External", options =>
    {
        options.ExpireTimeSpan =
            TimeSpan.FromMinutes(10);

        options.Cookie.HttpOnly = true;

        options.Cookie.SameSite =
            SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            CookieSecurePolicy.Always;
    })

    // --------------------------------------------------------
    // 13.3. GOOGLE LOGIN
    // --------------------------------------------------------

    .AddGoogle(
        GoogleDefaults.AuthenticationScheme,
        options =>
        {
            options.ClientId = googleClientId;

            options.ClientSecret =
                googleClientSecret;

            options.SignInScheme = "External";

            options.CallbackPath =
                "/signin-google";

            options.CorrelationCookie.HttpOnly = true;

            options.CorrelationCookie.SameSite =
                SameSiteMode.None;

            options.CorrelationCookie.SecurePolicy =
                CookieSecurePolicy.Always;
        })

    // --------------------------------------------------------
    // 13.4. FACEBOOK LOGIN
    // --------------------------------------------------------

    .AddFacebook(
        FacebookDefaults.AuthenticationScheme,
        options =>
        {
            options.AppId = facebookAppId;

            options.AppSecret =
                facebookAppSecret;

            options.SignInScheme = "External";

            options.CallbackPath =
                "/signin-facebook";

            options.Scope.Add("email");

            options.Fields.Add("id");
            options.Fields.Add("name");
            options.Fields.Add("email");

            options.CorrelationCookie.HttpOnly = true;

            options.CorrelationCookie.SameSite =
                SameSiteMode.None;

            options.CorrelationCookie.SecurePolicy =
                CookieSecurePolicy.Always;
        });

// ============================================================
// 14. HEALTH CHECKS
// ============================================================
//
// /health:
// Kiểm tra ứng dụng có thể phản hồi HTTP.
//
// Chưa kiểm tra Supabase, SMTP hoặc Gemini.
//
// /ping:
// Endpoint nhẹ dùng để đo độ trễ mạng và middleware.
// ============================================================

builder.Services.AddHealthChecks();

// ============================================================
// 15. BUILD APPLICATION
// ============================================================

var app = builder.Build();

// ============================================================
// 16. FORWARDED HEADERS
// ============================================================
//
// Đặt trước HTTPS Redirection và Authentication.
//
// Middleware sẽ đọc thông tin proxy.
// ============================================================

app.UseForwardedHeaders();

// ============================================================
// 17. ERROR HANDLING
// ============================================================
//
// Production:
// Hiển thị trang lỗi chung.
//
// Development:
// Giữ hành vi xử lý lỗi mặc định của môi trường dev.
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// ============================================================
// 18. HSTS
// ============================================================
//
// Yêu cầu trình duyệt ưu tiên HTTPS.
//
// Chỉ bật ngoài Development.
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// ============================================================
// 19. AI TRAINING DATA
// ============================================================
//
// Giữ nguyên cơ chế hiện tại:
//
// Nếu domain-model.zip tồn tại:
// - Không huấn luyện lại.
//
// Nếu model không tồn tại:
// - Kiểm tra domain-data.csv.
// - Huấn luyện mô hình.
//
// Lưu ý:
// Việc train khi startup có thể làm tăng cold start.
//
// Sau này nên chuyển training sang build pipeline
// nếu đo được đây là nguyên nhân gây chậm.
// ============================================================

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

    var trainingTimer = Stopwatch.StartNew();

    app.Logger.LogWarning(
        "EMUA AI: Model chưa tồn tại, bắt đầu huấn luyện.");

    DomainModelTrainer.Train(
        domainDataPath,
        domainModelPath);

    trainingTimer.Stop();

    app.Logger.LogInformation(
        "EMUA AI: Huấn luyện hoàn thành sau {Elapsed}ms",
        trainingTimer.ElapsedMilliseconds);
}
else
{
    app.Logger.LogInformation(
        "EMUA AI: Đã tìm thấy domain-model.zip.");
}

// ============================================================
// 20. RESPONSE COMPRESSION
// ============================================================
//
// Nén các HTTP response đủ điều kiện.
//
// Đặt trước StaticFiles để có thể nén tài nguyên tĩnh.
//
// Không thay đổi nội dung response.
// ============================================================

app.UseResponseCompression();

// ============================================================
// 21. HTTPS REDIRECTION
// ============================================================

app.UseHttpsRedirection();

// ============================================================
// 22. STATIC FILES
// ============================================================
//
// Phục vụ CSS, JavaScript, ảnh từ wwwroot.
//
// Lưu ý:
// Compression không thay thế tối ưu kích thước ảnh.
// ============================================================

app.UseStaticFiles();

// ============================================================
// 23. ROUTING
// ============================================================

app.UseRouting();

// ============================================================
// 24. REQUEST TIMING
// ============================================================
//
// Ghi log những request mất từ 500ms trở lên.
//
// Mục đích:
// - Phát hiện Controller xử lý chậm
// - Theo dõi endpoint cần tối ưu
// - So sánh trước/sau khi sửa code
//
// Không ghi query string hoặc dữ liệu nhạy cảm.
//
// Lưu ý:
// Middleware đặt tại đây không đo các request
// đã được StaticFiles xử lý trước đó.
// ============================================================

app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();

    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds >= 500)
        {
            app.Logger.LogWarning(
                "SLOW REQUEST: {Method} {Path} | " +
                "Status: {Status} | Duration: {Duration}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
});

// ============================================================
// 25. SESSION
// ============================================================

app.UseSession();

// ============================================================
// 26. AUTHENTICATION
// ============================================================

app.UseAuthentication();

// ============================================================
// 27. AUTHORIZATION
// ============================================================

app.UseAuthorization();

// ============================================================
// 28. HEALTH ENDPOINT
// ============================================================
//
// Endpoint công khai, không chứa thông tin nhạy cảm.
// ============================================================

app.MapHealthChecks("/health").AllowAnonymous();

// ============================================================
// 29. PING ENDPOINT
// ============================================================
//
// Dùng để đo request không truy vấn database.
// ============================================================

app.MapGet("/ping", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        timestamp = DateTime.UtcNow
    });
})
.AllowAnonymous();

// ============================================================
// 30. AREA ROUTING
// ============================================================
//
// Giữ nguyên route cho Admin và Staff.
// ============================================================

app.MapControllerRoute(
    name: "areas",
    pattern:
        "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

// ============================================================
// 31. DEFAULT ROUTING
// ============================================================
//
// Trang chủ:
// /Home/Index
//
// Các controller khác giữ nguyên route hiện tại.
// ============================================================

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");

// ============================================================
// 32. START APPLICATION
// ============================================================

app.Run();
