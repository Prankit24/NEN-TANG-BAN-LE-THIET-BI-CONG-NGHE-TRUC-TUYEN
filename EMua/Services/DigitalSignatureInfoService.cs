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
    // Có chứng thư trên máy không chứng minh hóa đơn này đã được ký.
    // Chưa có dữ liệu chữ ký gắn với nội dung hóa đơn để xác minh.
    public static DigitalSignatureVerificationResult InspectAvailableDigitalCertificate() => new()
    {
        IsSigned = false,
        IsLegalEnterpriseEInvoice = false,
        CertificateTypeDescription = "Chưa ký số",
        LegalNote = "BẢN MÔ PHỎNG — CHƯA KÝ SỐ. Hệ thống chưa tạo và xác minh chữ ký gắn với nội dung hóa đơn."
    };
}
