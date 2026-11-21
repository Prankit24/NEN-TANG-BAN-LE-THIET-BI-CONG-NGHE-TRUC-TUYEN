using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EMua.Services;

public static class OrderEvidenceStorage
{
    /// <summary>
    /// Xác định chính xác đường dẫn thư mục wwwroot có quyền ghi, tự động fallback nếu WebRootPath bị null/rỗng.
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
    /// Lưu ảnh bằng chứng giao hàng hoặc đổi trả vào thư mục uploads trên máy chủ.
    /// </summary>
    public static async Task<string> SaveAsync(string? webRoot, IFormFile file, string folder)
    {
        if (folder is not ("returns" or "delivery-proof"))
            throw new ArgumentException("Thư mục ảnh không hợp lệ.");

        if (file == null || file.Length <= 0)
            throw new ArgumentException("Vui lòng chọn ảnh hợp lệ.");

        if (file.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Mỗi ảnh phải có dung lượng từ 1 byte đến tối đa 5 MB.");

        var root = ResolveWebRoot(webRoot);

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

        var directory = Path.Combine(root, "uploads", folder);
        if (!Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                if (!Directory.Exists(directory))
                {
                    throw new IOException($"Không thể tạo thư mục lưu trữ '{directory}'. Kiểm tra phân quyền thư mục trên container/máy chủ: {ex.Message}", ex);
                }
            }
        }

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
        catch
        {
            if (File.Exists(fullPath))
            {
                try { File.Delete(fullPath); } catch { }
            }
            throw;
        }

        return $"/uploads/{folder}/{name}";
    }

    /// <summary>
    /// Xóa ảnh bằng chứng khi đơn hàng bị rollback hoặc cập nhật thất bại.
    /// </summary>
    public static void Delete(string? webRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var root = ResolveWebRoot(webRoot);
        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3) return;

        var folder = parts[^2];
        var fileName = parts[^1];

        if (folder is not ("returns" or "delivery-proof")) return;

        var fullPath = Path.Combine(root, "uploads", folder, fileName);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bỏ qua lỗi xóa dọn dẹp tạm
        }
    }
}
