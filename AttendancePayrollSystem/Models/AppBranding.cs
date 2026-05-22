namespace AttendancePayrollSystem.Models
{
    public class AppBranding
    {
        public const int DefaultBrandingSettingsId = 1;

        public int BrandingSettingsId { get; set; } = DefaultBrandingSettingsId;
        public byte[]? LogoImage { get; set; }
    }
}
