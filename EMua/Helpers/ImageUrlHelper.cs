using System;
using System.IO;
using System.Linq;
using EMua.Models.Database;
using EMua.Services;

namespace EMua.Helpers
{
    public static class ImageUrlHelper
    {
        private static string? _webRootPath;

        public const string DefaultPlaceholderSvg = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 320 320' width='320' height='320'><rect width='100%25' height='100%25' fill='%23F8FAFC'/><g transform='translate(110,110)' stroke='%2394A3B8' stroke-width='4' fill='none' stroke-linecap='round' stroke-linejoin='round'><rect x='5' y='10' width='90' height='60' rx='8'/><path d='M35 85h30'/><circle cx='50' cy='40' r='14'/><path d='M25 70h50'/></g><text x='50%25' y='215' fill='%2394A3B8' font-family='Inter,Segoe UI,sans-serif' font-size='12.5' font-weight='600' text-anchor='middle' letter-spacing='0.5'>EMUA TECH STORE</text></svg>";

        public static void Initialize(string? webRootPath)
        {
            _webRootPath = webRootPath;
        }

        public static string FixImageUrl(string? rawUrl, bool checkPhysicalFile = true)
        {
            if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl == "/images/no-image.webp")
            {
                return DefaultPlaceholderSvg;
            }

            var clean = rawUrl.Trim().Replace('\\', '/');

            // 1. Hỗ trợ link tuyệt đối (http, https, //) hoặc base64 data URI: giữ nguyên, tuyệt đối không kiểm tra đĩa local
            if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return clean;
            }

            // Loại bỏ dấu ~ nếu có
            if (clean.StartsWith("~/"))
            {
                clean = clean.Substring(1);
            }

            // Nếu bắt đầu bằng wwwroot/ thì bỏ
            if (clean.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(7);
            }
            else if (clean.StartsWith("/wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(8);
            }

            // Nếu không có dấu / ở đầu và không thuộc thư mục thông dụng, thêm tiền tố
            if (!clean.StartsWith("/"))
            {
                if (clean.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ||
                    clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ||
                    clean.StartsWith("img/", StringComparison.OrdinalIgnoreCase))
                {
                    clean = "/" + clean;
                }
                else
                {
                    clean = "/images/products/" + clean;
                }
            }

            // 2. Kiểm tra file vật lý trên đĩa cho đường dẫn nội bộ (hỗ trợ Linux/Docker/Windows)
            if (checkPhysicalFile)
            {
                try
                {
                    var webRoot = !string.IsNullOrWhiteSpace(_webRootPath) && Directory.Exists(_webRootPath)
                        ? _webRootPath
                        : OrderEvidenceStorage.ResolveWebRoot(_webRootPath);

                    if (!string.IsNullOrWhiteSpace(webRoot))
                    {
                        var relativePath = clean.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                        var fullPhysicalPath = Path.Combine(webRoot, relativePath);
                        if (!File.Exists(fullPhysicalPath))
                        {
                            // Kiểm tra đối sánh không phân biệt hoa thường nếu chạy trên Linux / Docker
                            var foundPath = ResolveCaseInsensitivePath(webRoot, relativePath);
                            if (foundPath != null)
                            {
                                return "/" + Path.GetRelativePath(webRoot, foundPath).Replace('\\', '/');
                            }

                            return DefaultPlaceholderSvg;
                        }
                    }
                }
                catch
                {
                    // Trường hợp đường dẫn không hợp lệ
                    return DefaultPlaceholderSvg;
                }
            }

            return clean;
        }

        private static string? ResolveCaseInsensitivePath(string rootDir, string relativePath)
        {
            try
            {
                var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, '/' }, StringSplitOptions.RemoveEmptyEntries);
                var currentDir = rootDir;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var part = parts[i];
                    var subDirs = Directory.GetDirectories(currentDir);
                    var match = subDirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), part, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return null;
                    currentDir = match;
                }

                var fileName = parts[^1];
                var files = Directory.GetFiles(currentDir);
                return files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        public static string ResolveProductImage(SanPham? product, BienTheSanPham? variant = null, bool checkPhysicalFile = true)
        {
            // 1. Ảnh biến thể nếu có
            if (!string.IsNullOrWhiteSpace(variant?.HinhAnh))
            {
                var variantUrl = FixImageUrl(variant.HinhAnh, checkPhysicalFile);
                if (variantUrl != DefaultPlaceholderSvg && variantUrl != "/images/no-image.webp")
                {
                    return variantUrl;
                }
            }

            if (product != null)
            {
                // 2. Ảnh chính của sản phẩm (LaAnhChinh == true)
                var mainImg = product.SanPhamHinhAnhs?
                    .Where(x => x.LaAnhChinh == true && !string.IsNullOrWhiteSpace(x.DuongDanAnh))
                    .OrderBy(x => x.ThuTu)
                    .FirstOrDefault();

                if (mainImg != null)
                {
                    var mainUrl = FixImageUrl(mainImg.DuongDanAnh, checkPhysicalFile);
                    if (mainUrl != DefaultPlaceholderSvg && mainUrl != "/images/no-image.webp")
                    {
                        return mainUrl;
                    }
                }

                // 3. Ảnh đầu tiên trong SanPhamHinhAnhs
                var firstImg = product.SanPhamHinhAnhs?
                    .Where(x => !string.IsNullOrWhiteSpace(x.DuongDanAnh))
                    .OrderBy(x => x.ThuTu)
                    .FirstOrDefault();

                if (firstImg != null)
                {
                    var firstUrl = FixImageUrl(firstImg.DuongDanAnh, checkPhysicalFile);
                    if (firstUrl != DefaultPlaceholderSvg && firstUrl != "/images/no-image.webp")
                    {
                        return firstUrl;
                    }
                }

                // Fallback nếu sản phẩm có biến thể bất kỳ có ảnh
                var anyVariantImg = product.BienTheSanPhams?
                    .Where(b => !string.IsNullOrWhiteSpace(b.HinhAnh))
                    .Select(b => b.HinhAnh)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(anyVariantImg))
                {
                    var anyVarUrl = FixImageUrl(anyVariantImg, checkPhysicalFile);
                    if (anyVarUrl != DefaultPlaceholderSvg && anyVarUrl != "/images/no-image.webp")
                    {
                        return anyVarUrl;
                    }
                }
            }

            return DefaultPlaceholderSvg;
        }
    }
}
