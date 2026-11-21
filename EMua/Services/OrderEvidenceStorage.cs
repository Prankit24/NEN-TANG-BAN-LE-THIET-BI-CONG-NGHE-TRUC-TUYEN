using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EMua.Services;

public static class OrderEvidenceStorage
{
    private static readonly string FallbackUploadDirectory = Path.Combine(Path.GetTempPath(), "emua-uploads");

    /// <summary>
    /// Thư mục dự phòng có quyền ghi chắc chắn trên Linux/Windows (Path.GetTempPath()/emua-uploads).
    /// </summary>
    public static string FallbackDirectory => FallbackUploadDirectory;

    /// <summary>
    /// Kiểm tra xem một thư mục có thực sự ghi và xóa file được hay không.
    /// </summary>
    public static bool CanWriteToDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var testFile = Path.Combine(dir, $".write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Xác định chính xác đường dẫn thư mục wwwroot, tự động fallback nếu WebRootPath bị null/rỗng.
    /// </summary>
    public static string ResolveWebRoot(string? webRoot)
    {
        if (!string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot))
        {
            return webRoot;
        }

        var cwdWwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        if (Directory.Exists(cwdWwwroot))
        {
            return cwdWwwroot;
        }

        var baseWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(baseWwwroot))
        {
            return baseWwwroot;
        }

        var fallback = !string.IsNullOrWhiteSpace(webRoot) ? webRoot : cwdWwwroot;
        try
        {
            if (!Directory.Exists(fallback))
            {
                Directory.CreateDirectory(fallback);
            }
        }
        catch
        {
            // Bỏ qua lỗi nếu thư mục đã tồn tại hoặc quyền hạn bị hạn chế
        }
        return fallback;
    }

    /// <summary>
    /// Lấy thư mục vật lý cụ thể để lưu file upload (ví dụ: uploads/returns).
    /// Tự động kiểm tra quyền ghi thực tế, nếu wwwroot/uploads/{folder} không ghi được sẽ fallback sang /tmp/emua-uploads/{folder}.
    /// </summary>
    public static string GetTargetDirectory(string? webRoot, string folder)
    {
        var primaryFolder = Path.Combine(ResolveWebRoot(webRoot), "uploads", folder);
        if (CanWriteToDirectory(primaryFolder))
        {
            return primaryFolder;
        }

        var fallbackFolder = Path.Combine(FallbackUploadDirectory, folder);
        try
        {
            if (!Directory.Exists(fallbackFolder))
            {
                Directory.CreateDirectory(fallbackFolder);
            }
        }
        catch
        {
            // Bỏ qua nếu đã tồn tại
        }
        return fallbackFolder;
    }

    /// <summary>
    /// Lưu ảnh bằng chứng giao hàng hoặc đổi trả vào thư mục uploads (hoặc fallback nếu bị khóa quyền).
    /// </summary>
    public static async Task<string> SaveAsync(string? webRoot, IFormFile file, string folder)
    {
        if (folder is not ("returns" or "delivery-proof"))
            throw new ArgumentException("Thư mục ảnh không hợp lệ.");

        if (file == null || file.Length <= 0)
            throw new ArgumentException("Vui lòng chọn ảnh hợp lệ.");

        if (file.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Mỗi ảnh phải có dung lượng từ 1 byte đến tối đa 5 MB.");

        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var count = 0;
        while (count < header.Length)
        {
            var n = await input.ReadAsync(header.AsMemory(count));
            if (n == 0) break;
            count += n;
        }

        // Kiểm tra magic bytes
        var isPng = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var isJpg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
        var isWebp = count >= 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP";

        var rawExt = Path.GetExtension(file.FileName).ToLowerInvariant();
        var isAllowedExt = rawExt is ".jpg" or ".jpeg" or ".png" or ".webp";

        if (!isPng && !isJpg && !isWebp && !isAllowedExt)
            throw new ArgumentException("Chỉ nhận ảnh JPG, JPEG, PNG hoặc WebP có định dạng hợp lệ.");

        var ext = isPng ? ".png" : (isJpg ? ".jpg" : (isWebp ? ".webp" : rawExt));
        var name = $"{Guid.NewGuid():N}{ext}";

        // Tự động kiểm tra quyền ghi trên chính thư mục đích (ví dụ /app/wwwroot/uploads/returns)
        // Nếu không ghi được, tự động chuyển sang /tmp/emua-uploads/returns
        var directory = GetTargetDirectory(webRoot, folder);

        var fullPath = Path.Combine(directory, name);
        try
        {
            await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (count > 0)
            {
                await output.WriteAsync(header.AsMemory(0, count));
            }
            await input.CopyToAsync(output);
        }
        catch (UnauthorizedAccessException)
        {
            // Nếu vẫn bị chặn quyền ghi, fallback khẩn cấp sang /tmp/emua-uploads
            var emergencyDir = Path.Combine(FallbackUploadDirectory, folder);
            if (!Directory.Exists(emergencyDir))
            {
                Directory.CreateDirectory(emergencyDir);
            }
            var emergencyPath = Path.Combine(emergencyDir, name);
            await using var output = new FileStream(emergencyPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (count > 0)
            {
                await output.WriteAsync(header.AsMemory(0, count));
            }
            await input.CopyToAsync(output);
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                try { File.Delete(fullPath); } catch { }
            }
            throw;
        }

        // Đường dẫn truy cập web luôn là /uploads/{folder}/{name}
        return $"/uploads/{folder}/{name}";
    }

    /// <summary>
    /// Xóa ảnh bằng chứng khi đơn hàng bị rollback hoặc cập nhật thất bại.
    /// Tìm và xóa ở cả wwwroot/uploads và /tmp/emua-uploads.
    /// </summary>
    public static void Delete(string? webRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return;

        var folder = parts[^2];
        var fileName = parts[^1];

        if (folder is not ("returns" or "delivery-proof")) return;

        // Thử xóa ở thư mục primary (wwwroot/uploads)
        var primaryRoot = Path.Combine(ResolveWebRoot(webRoot), "uploads");
        var primaryFile = Path.Combine(primaryRoot, folder, fileName);
        try
        {
            if (File.Exists(primaryFile))
            {
                File.Delete(primaryFile);
            }
        }
        catch { }

        // Thử xóa ở thư mục fallback (/tmp/emua-uploads)
        var fallbackFile = Path.Combine(FallbackUploadDirectory, folder, fileName);
        try
        {
            if (File.Exists(fallbackFile))
            {
                File.Delete(fallbackFile);
            }
        }
        catch { }
    }
}
