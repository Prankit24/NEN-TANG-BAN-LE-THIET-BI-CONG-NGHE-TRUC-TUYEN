using System;
using System.Security.Cryptography.X509Certificates;

namespace EMua.Services;

public class DigitalSignatureVerificationResult
{
    public bool IsSigned { get; set; }
    public string CertificateSubject { get; set; } = string.Empty;
    public string CertificateIssuer { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string CertificateTypeDescription { get; set; } = string.Empty;
    public bool IsLegalEnterpriseEInvoice { get; set; } = false;
    public string LegalNote { get; set; } = string.Empty;
}

public static class DigitalSignatureInfoService
{
    /// <summary>
    /// Kiểm tra môi trường chữ ký số có sẵn trên hệ thống (ví dụ: máy trạm nhân viên hoặc chứng thư demo trong kho lưu trữ).
    /// Tuân thủ quy định:
    /// - Không lưu private key hoặc mật khẩu chứng thư trong source code / database công khai.
    /// - Phân biệt rành mạch "Đã ký số bản demo (Technical Signature)" và "Hóa đơn điện tử hợp pháp được cơ quan thuế cấp mã/chấp nhận".
    /// - Không kết nối gửi dữ liệu giả sang Cục Thuế.
    /// </summary>
    public static DigitalSignatureVerificationResult InspectAvailableDigitalCertificate()
    {
        var result = new DigitalSignatureVerificationResult
        {
            IsSigned = false,
            CertificateTypeDescription = "Bản mô phỏng kỹ thuật (Demo Certificate)",
            IsLegalEnterpriseEInvoice = false,
            LegalNote = "BẢN MÔ PHỎNG – KHÔNG CÓ GIÁ TRỊ PHÁP LÝ. Hóa đơn demo phục vụ nghiên cứu và trình diễn khóa luận, chưa phát hành qua cổng Cục Thuế."
        };

        try
        {
            // Kiểm tra chứng thư số trong Windows Certificate Store của người dùng hiện tại
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            if (store.Certificates.Count > 0)
            {
                // Lấy chứng thư số hợp lệ còn hạn gần nhất
                foreach (var cert in store.Certificates)
                {
                    if (cert.NotAfter >= DateTime.Now)
                    {
                        result.IsSigned = true;
                        result.CertificateSubject = cert.Subject;
                        result.CertificateIssuer = cert.Issuer;
                        result.SerialNumber = cert.SerialNumber;
                        result.ValidFrom = cert.NotBefore;
                        result.ValidTo = cert.NotAfter;
                        result.SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "SHA256RSA";

                        // Nhận diện chữ ký cá nhân vs doanh nghiệp
                        bool isPersonal = !cert.Subject.Contains("MST", StringComparison.OrdinalIgnoreCase) 
                                       && !cert.Subject.Contains("MÃ SỐ THUẾ", StringComparison.OrdinalIgnoreCase)
                                       && !cert.Subject.Contains("CTY", StringComparison.OrdinalIgnoreCase);

                        result.CertificateTypeDescription = isPersonal
                            ? "Chứng thư số cá nhân (Personal Digital Certificate) – Sử dụng ký duyệt nội bộ kỹ thuật demo"
                            : "Chứng thư số thử nghiệm doanh nghiệp (Enterprise Demo Certificate)";
                        break;
                    }
                }
            }
        }
        catch
        {
            // Fallback sang thông tin ký số mô phỏng chuẩn X.509 khi máy chủ không cài sẵn cert
            result.IsSigned = true;
            result.CertificateSubject = "CN=NGUYEN VAN QUAN LY, O=EMUA TECHNOLOGY CORP (DEMO), C=VN";
            result.CertificateIssuer = "CN=VNPT-CA / VIETTEL-CA DEMO TEST ROOT, O=DEMO CERTIFICATE AUTHORITY, C=VN";
            result.SerialNumber = "54019283746581920394857261";
            result.ValidFrom = new DateTime(2025, 1, 1);
            result.ValidTo = new DateTime(2027, 12, 31);
            result.SignatureAlgorithm = "sha256RSA (2048 bits)";
            result.CertificateTypeDescription = "Chứng thư số cá nhân / Khóa USB Token mô phỏng (X.509 Demo)";
        }

        return result;
    }
}
