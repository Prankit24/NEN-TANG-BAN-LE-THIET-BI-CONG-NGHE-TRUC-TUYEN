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
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPasswordHasher<NguoiDung>,
    PasswordHasher<NguoiDung>>();

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
                "Chưa có Google ClientId trong secrets.json.");

        options.ClientSecret =
            builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Chưa có Google ClientSecret trong secrets.json.");

        options.SignInScheme = "External";
        options.CallbackPath = "/signin-google";
    })
    .AddFacebook(FacebookDefaults.AuthenticationScheme, options =>
    {
        options.AppId =
            builder.Configuration["Authentication:Facebook:AppId"]
            ?? throw new InvalidOperationException(
                "Chưa có Facebook AppId trong secrets.json.");

        options.AppSecret =
            builder.Configuration["Authentication:Facebook:AppSecret"]
            ?? throw new InvalidOperationException(
                "Chưa có Facebook AppSecret trong secrets.json.");

        options.SignInScheme = "External";
        options.CallbackPath = "/signin-facebook";
        options.Scope.Add("email");
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
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();