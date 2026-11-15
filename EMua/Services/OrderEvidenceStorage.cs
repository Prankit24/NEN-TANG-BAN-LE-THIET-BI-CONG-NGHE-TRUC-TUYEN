namespace EMua.Services;

public static class OrderEvidenceStorage
{
    public static async Task<string> SaveAsync(string webRoot, IFormFile file, string folder)
    {
        if (folder is not ("returns" or "delivery-proof")) throw new ArgumentException("Thư mục ảnh không hợp lệ.");
        if (file.Length <= 0 || file.Length > 5 * 1024 * 1024) throw new ArgumentException("Mỗi ảnh phải có dung lượng từ 1 byte đến 5 MB.");
        await using var input = file.OpenReadStream();
        var header = new byte[12]; var count = 0;
        while (count < header.Length)
        {
            var n = await input.ReadAsync(header.AsMemory(count)); if (n == 0) break; count += n;
        }
        var png = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
        var jpg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
        var webp = count >= 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
        if (!png && !jpg && !webp) throw new ArgumentException("Chỉ nhận ảnh JPG, PNG hoặc WebP có định dạng hợp lệ.");
        var ext = png ? ".png" : jpg ? ".jpg" : ".webp";
        var name = Guid.NewGuid().ToString("N") + ext;
        var directory = Path.Combine(webRoot, "uploads", folder); Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, name);
        try
        {
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
            await output.WriteAsync(header.AsMemory(0, count)); await input.CopyToAsync(output);
        }
        catch { if (File.Exists(fullPath)) File.Delete(fullPath); throw; }
        return $"/uploads/{folder}/{name}";
    }
    public static void Delete(string webRoot, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "uploads" || parts[1] is not ("returns" or "delivery-proof") ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(parts[2]), "N", out _)) return;
        var fullPath = Path.Combine(webRoot, parts[0], parts[1], parts[2]);
        try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
