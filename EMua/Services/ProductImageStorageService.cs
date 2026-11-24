using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMua.Services;

public interface IProductImageStorageService
{
    Task<string> SaveProductImageAsync(IFormFile file, string? subFolder = null);
    Task<bool> DeleteProductImageAsync(string? imageUrl);
}

public class ProductImageStorageService : IProductImageStorageService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ProductImageStorageService> _logger;
    private readonly HttpClient _httpClient;

    public ProductImageStorageService(
        IConfiguration config,
        IWebHostEnvironment environment,
        ILogger<ProductImageStorageService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _environment = environment;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<string> SaveProductImageAsync(IFormFile file, string? subFolder = null)
    {
        if (file == null || file.Length == 0)
        {
            return "/images/no-image.webp";
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".jpg";
        }

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var objectPath = string.IsNullOrWhiteSpace(subFolder) ? fileName : $"{subFolder.Trim('/')}/{fileName}";

        // 1. Kiểm tra cấu hình Supabase Storage
        var supabaseUrl = (_config["Supabase:Url"] ?? "").Trim().TrimEnd('/');
        var supabaseKey = (_config["Supabase:Key"]
            ?? _config["Supabase:ServiceRoleKey"]
            ?? _config["Supabase:ApiKey"]
            ?? Environment.GetEnvironmentVariable("SUPABASE_KEY")
            ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
            ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
            ?? "").Trim();
        var bucket = (_config["Supabase:Bucket"] ?? "products").Trim();

        if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
        {
            try
            {
                var endpoint = $"{supabaseUrl}/storage/v1/object/{bucket}/{objectPath}";
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("x-upsert", "true");

                await using var stream = file.OpenReadStream();
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(ext));
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var publicUrl = $"{supabaseUrl}/storage/v1/object/public/{bucket}/{objectPath}";
                    _logger.LogInformation("Đã tải ảnh lên Supabase Storage thành công: {Url}", publicUrl);
                    return publicUrl;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Supabase Storage phản hồi mã lỗi {StatusCode}: {Error}. Fallback sang lưu cục bộ.",
                        response.StatusCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi kết nối Supabase Storage khi upload ảnh. Fallback sang lưu cục bộ.");
            }
        }

        // 2. Fallback lưu nội bộ (đảm bảo an toàn đường dẫn trên cả Linux/Docker/Windows)
        var webRoot = OrderEvidenceStorage.ResolveWebRoot(_environment.WebRootPath);
        var targetFolder = string.IsNullOrWhiteSpace(subFolder)
            ? Path.Combine(webRoot, "images", "products")
            : Path.Combine(webRoot, "images", "products", subFolder.Trim('/'));

        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var localPath = Path.Combine(targetFolder, fileName);
        await using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
        {
            await file.CopyToAsync(fileStream);
        }

        var relativeUrl = string.IsNullOrWhiteSpace(subFolder)
            ? $"/images/products/{fileName}"
            : $"/images/products/{subFolder.Trim('/')}/{fileName}";

        return relativeUrl;
    }

    public async Task<bool> DeleteProductImageAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || imageUrl == "/images/no-image.webp")
        {
            return false;
        }

        var supabaseUrl = (_config["Supabase:Url"] ?? "").Trim().TrimEnd('/');
        var supabaseKey = (_config["Supabase:Key"]
            ?? _config["Supabase:ServiceRoleKey"]
            ?? _config["Supabase:ApiKey"]
            ?? Environment.GetEnvironmentVariable("SUPABASE_KEY")
            ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
            ?? "").Trim();
        var bucket = (_config["Supabase:Bucket"] ?? "products").Trim();

        // Xóa trên Supabase nếu là URL Supabase Storage
        if (!string.IsNullOrEmpty(supabaseUrl) &&
            imageUrl.StartsWith(supabaseUrl, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(supabaseKey))
        {
            try
            {
                var marker = $"/storage/v1/object/public/{bucket}/";
                var idx = imageUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var objectPath = imageUrl.Substring(idx + marker.Length);
                    var deleteEndpoint = $"{supabaseUrl}/storage/v1/object/{bucket}/{objectPath}";
                    using var request = new HttpRequestMessage(HttpMethod.Delete, deleteEndpoint);
                    request.Headers.Add("Authorization", $"Bearer {supabaseKey}");
                    request.Headers.Add("apikey", supabaseKey);
                    var response = await _httpClient.SendAsync(request);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi xóa ảnh trên Supabase Storage: {Url}", imageUrl);
            }
            return false;
        }

        // Xóa file nội bộ
        try
        {
            if (imageUrl.StartsWith("/images/products/", StringComparison.OrdinalIgnoreCase))
            {
                var webRoot = OrderEvidenceStorage.ResolveWebRoot(_environment.WebRootPath);
                var relative = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(webRoot, relative);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi xóa ảnh cục bộ: {Url}", imageUrl);
        }

        return false;
    }

    private static string GetMimeType(string ext) => ext switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "image/jpeg"
    };
}
