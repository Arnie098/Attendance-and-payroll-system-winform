namespace AttendancePayrollSystem.Models
{
    public class AppBranding
    {
        public const int DefaultBrandingSettingsId = 1;

        public int BrandingSettingsId { get; set; } = DefaultBrandingSettingsId;
        public byte[]? LogoImage { get; set; }
        public string CertifyingOfficerName { get; set; } = string.Empty;
        public string CertifyingOfficerTitle { get; set; } = string.Empty;
    }
}
