using System;
using System.Configuration;

namespace AttendancePayrollSystem.Services
{
    public static class SupabaseConfig
    {
        private const string SupabaseUrlEnvVar = "SUPABASE_URL";
        private const string SupabasePublishableKeyEnvVar = "SUPABASE_PUBLISHABLE_KEY";
        private const string SupabaseUseApiEnvVar = "SUPABASE_USE_API";

        public static string Url => GetRequiredValue("SupabaseUrl", SupabaseUrlEnvVar);

        public static string PublishableKey => GetRequiredValue("SupabasePublishableKey", SupabasePublishableKeyEnvVar);

        public static bool UseApi => GetOptionalBool("SupabaseUseApi", SupabaseUseApiEnvVar);

        public static Uri RestApiUrl => new($"{Url.TrimEnd('/')}/rest/v1/");

        private static string GetRequiredValue(string appSettingKey, string envVar)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = ConfigurationManager.AppSettings[appSettingKey];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"Missing Supabase configuration. Set {envVar} or add {appSettingKey} to App.config.");
        }

        private static bool GetOptionalBool(string appSettingKey, string envVar)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = ConfigurationManager.AppSettings[appSettingKey];
            }

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
