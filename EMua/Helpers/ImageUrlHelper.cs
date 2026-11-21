using System;
using System.IO;
using System.Linq;
using EMua.Models.Database;

namespace EMua.Helpers
{
    public static class ImageUrlHelper
    {
        private static string? _webRootPath;

        public static void Initialize(string? webRootPath)
        {
            _webRootPath = webRootPath;
        }

        public static string FixImageUrl(string? rawUrl, bool checkPhysicalFile = false)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return "/images/no-image.png";
            }

            var clean = rawUrl.Trim().Replace('\\', '/');

            // Hỗ trợ link ngoài (http, https, //) hoặc base64 data URI
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

            // Tùy chọn kiểm tra file vật lý trên đĩa (nếu đã cấu hình webRootPath)
            if (checkPhysicalFile && !string.IsNullOrWhiteSpace(_webRootPath))
            {
                try
                {
                    var relativePath = clean.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    var fullPhysicalPath = Path.Combine(_webRootPath, relativePath);
                    if (!File.Exists(fullPhysicalPath))
                    {
                        return "/images/no-image.png";
                    }
                }
                catch
                {
                    // Trường hợp đường dẫn không hợp lệ
                    return "/images/no-image.png";
                }
            }

            return clean;
        }

        public static string ResolveProductImage(SanPham? product, BienTheSanPham? variant = null, bool checkPhysicalFile = false)
        {
            // 1. Ảnh biến thể nếu có
            if (!string.IsNullOrWhiteSpace(variant?.HinhAnh))
            {
                var variantUrl = FixImageUrl(variant.HinhAnh, checkPhysicalFile);
                if (variantUrl != "/images/no-image.png")
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
                    if (mainUrl != "/images/no-image.png")
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
                    if (firstUrl != "/images/no-image.png")
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
                    if (anyVarUrl != "/images/no-image.png")
                    {
                        return anyVarUrl;
                    }
                }
            }

            return "/images/no-image.png";
        }
    }
}
