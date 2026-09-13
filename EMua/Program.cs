using EMua.Services.AI;

var builder = WebApplication.CreateBuilder(args);


// ==========================
// SERVICES
// ==========================

builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<DomainClassifier>();
builder.Services.AddHttpClient<GeminiService>();


var app = builder.Build();


// ==========================
// TRAIN DOMAIN AI MODEL
// ==========================

var domainDataPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "AITraining",
    "domain-data.csv");

var domainModelPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "MLModels",
    "domain-model.zip");


// Chỉ train nếu model chưa tồn tại
if (!File.Exists(domainModelPath))
{
    DomainModelTrainer.Train(
        domainDataPath,
        domainModelPath);
}


// ==========================
// HTTP PIPELINE
// ==========================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();


// ==========================
// ROUTING
// ==========================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();