using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class AppBrandingRepository
    {
        public AppBranding GetBranding()
        {
            return SupabaseConfig.UseApi
                ? GetBrandingViaApi()
                : GetBrandingViaDatabase();
        }

        public void UpdateLogoImage(byte[]? logoImage)
        {
            if (SupabaseConfig.UseApi)
            {
                UpdateLogoImageViaApi(logoImage);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    INSERT INTO AppBrandingSettings
                    (BrandingSettingsId, LogoImage)
                    VALUES
                    (@BrandingSettingsId, @LogoImage)
                    ON CONFLICT(BrandingSettingsId) DO UPDATE SET
                        LogoImage = excluded.LogoImage"
                : @"
                    INSERT INTO AppBrandingSettings
                    (BrandingSettingsId, LogoImage)
                    VALUES
                    (@BrandingSettingsId, @LogoImage)
                    ON DUPLICATE KEY UPDATE
                        LogoImage = VALUES(LogoImage)";
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BrandingSettingsId", AppBranding.DefaultBrandingSettingsId);
            command.Parameters.AddWithValue("@LogoImage", logoImage is null ? DBNull.Value : logoImage);
            connection.Open();
            command.ExecuteNonQuery();
            MySqlOfflineSyncService.QueueBrandingUpsert();
        }

        public void UpdateCertifyingOfficer(string name, string title)
        {
            using var connection = DatabaseHelper.GetConnection();
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, CertifyingOfficerName, CertifyingOfficerTitle)
                    VALUES (@BrandingSettingsId, @Name, @Title)
                    ON CONFLICT(BrandingSettingsId) DO UPDATE SET
                        CertifyingOfficerName = excluded.CertifyingOfficerName,
                        CertifyingOfficerTitle = excluded.CertifyingOfficerTitle"
                : @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, CertifyingOfficerName, CertifyingOfficerTitle)
                    VALUES (@BrandingSettingsId, @Name, @Title)
                    ON DUPLICATE KEY UPDATE
                        CertifyingOfficerName = VALUES(CertifyingOfficerName),
                        CertifyingOfficerTitle = VALUES(CertifyingOfficerTitle)";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BrandingSettingsId", AppBranding.DefaultBrandingSettingsId);
            command.Parameters.AddWithValue("@Name", name.Trim());
            command.Parameters.AddWithValue("@Title", title.Trim());
            connection.Open();
            command.ExecuteNonQuery();
        }

        private static AppBranding GetBrandingViaDatabase()
        {
            const string sql = @"
                SELECT BrandingSettingsId, LogoImage, CertifyingOfficerName, CertifyingOfficerTitle
                FROM AppBrandingSettings
                WHERE BrandingSettingsId = @BrandingSettingsId
                LIMIT 1";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BrandingSettingsId", AppBranding.DefaultBrandingSettingsId);
            connection.Open();
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return new AppBranding();
            }

            return new AppBranding
            {
                BrandingSettingsId = Convert.ToInt32(reader["BrandingSettingsId"]),
                LogoImage = reader["LogoImage"] is DBNull ? null : (byte[])reader["LogoImage"],
                CertifyingOfficerName = Convert.ToString(reader["CertifyingOfficerName"]) ?? string.Empty,
                CertifyingOfficerTitle = Convert.ToString(reader["CertifyingOfficerTitle"]) ?? string.Empty
            };
        }

        private static AppBranding GetBrandingViaApi()
        {
            var record = SupabaseRestClient.GetSingleOrDefault<ApiBrandingRecord>(
                "appbrandingsettings",
                new Dictionary<string, string>
                {
                    ["select"] = "brandingsettingsid,logoimage",
                    ["brandingsettingsid"] = $"eq.{AppBranding.DefaultBrandingSettingsId}",
                    ["limit"] = "1"
                });

            return record == null
                ? new AppBranding()
                : new AppBranding
                {
                    BrandingSettingsId = record.BrandingSettingsId,
                    LogoImage = ParseBytea(record.LogoImage)
                };
        }

        private static void UpdateLogoImageViaApi(byte[]? logoImage)
        {
            var filter = new Dictionary<string, string>
            {
                ["brandingsettingsid"] = $"eq.{AppBranding.DefaultBrandingSettingsId}"
            };

            var existing = SupabaseRestClient.GetSingleOrDefault<ApiBrandingRecord>(
                "appbrandingsettings",
                new Dictionary<string, string>(filter)
                {
                    ["select"] = "brandingsettingsid",
                    ["limit"] = "1"
                });

            if (existing == null)
            {
                SupabaseRestClient.InsertAndReturnSingle<ApiBrandingRecord>(
                    "appbrandingsettings",
                    new
                    {
                        brandingsettingsid = AppBranding.DefaultBrandingSettingsId,
                        logoimage = ToApiByteaValue(logoImage)
                    });
                return;
            }

            SupabaseRestClient.Update(
                "appbrandingsettings",
                new
                {
                    logoimage = ToApiByteaValue(logoImage)
                },
                filter);
        }

        private static string? ToApiByteaValue(byte[]? value)
        {
            return value == null || value.Length == 0
                ? null
                : $@"\x{Convert.ToHexString(value).ToLowerInvariant()}";
        }

        private static byte[]? ParseBytea(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (value.StartsWith(@"\x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.FromHexString(value[2..]);
            }

            return Convert.FromBase64String(value);
        }

        private sealed class ApiBrandingRecord
        {
            public int BrandingSettingsId { get; set; }
            public string? LogoImage { get; set; }
        }
    }
}
