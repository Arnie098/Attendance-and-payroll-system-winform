using System;
using System.Configuration;
using System.IO;
using AttendancePayrollSystem.DataAccess;
using MySqlConnector;

namespace AttendancePayrollSystem.Services
{
    public static class DatabaseConnectionSettingsStore
    {
        private const string DbConnectionEnvVar = "ATTENDANCE_DB_CONNECTION";
        private const string ConnectionStringName = "AttendanceDb";
        private const string DefaultSslMode = "Preferred";
        private const string SettingsFileName = "database.override.env";

        public static string SettingsFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AttendancePayrollSystem",
                SettingsFileName);

        public static bool HasSavedOverride()
        {
            return File.Exists(SettingsFilePath);
        }

        public static DatabaseConnectionSettings LoadCurrent()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable(DbConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                rawConnectionString = ConfigurationManager.ConnectionStrings[ConnectionStringName]?.ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                return new DatabaseConnectionSettings();
            }

            try
            {
                var builder = new MySqlConnectionStringBuilder(rawConnectionString);
                return new DatabaseConnectionSettings
                {
                    Server = builder.Server ?? string.Empty,
                    Port = builder.Port == 0 ? 3306 : builder.Port,
                    Database = builder.Database ?? string.Empty,
                    Username = builder.UserID ?? string.Empty,
                    Password = builder.Password ?? string.Empty,
                    SslMode = builder.SslMode == MySqlSslMode.None ? "None" : builder.SslMode.ToString()
                };
            }
            catch
            {
                return new DatabaseConnectionSettings();
            }
        }

        public static string BuildConnectionString(DatabaseConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var sslMode = Enum.TryParse(settings.SslMode, true, out MySqlSslMode parsedSslMode)
                ? parsedSslMode
                : MySqlSslMode.Preferred;

            var builder = new MySqlConnectionStringBuilder
            {
                Server = settings.Server.Trim(),
                Port = settings.Port == 0 ? 3306 : settings.Port,
                Database = settings.Database.Trim(),
                UserID = settings.Username.Trim(),
                Password = settings.Password,
                SslMode = sslMode
            };

            return DatabaseHelper.NormalizeConnectionString(builder.ConnectionString);
        }

        public static void Save(DatabaseConnectionSettings settings)
        {
            var connectionString = BuildConnectionString(settings);
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                SettingsFilePath,
                $"{DbConnectionEnvVar}={connectionString}{Environment.NewLine}");

            Environment.SetEnvironmentVariable(DbConnectionEnvVar, connectionString);
        }

        public static void ClearOverride()
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Delete(SettingsFilePath);
            }

            Environment.SetEnvironmentVariable(DbConnectionEnvVar, null);
            DotEnv.Load();
        }

        public static string[] GetSupportedSslModes()
        {
            return
            [
                DefaultSslMode,
                "Required",
                "None"
            ];
        }
    }
}
