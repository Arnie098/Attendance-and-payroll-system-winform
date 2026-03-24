using System;
using System.Configuration;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public static class SchoolDatabaseHelper
    {
        private const string SchoolDbConnectionEnvVar = "SCHOOL_DB_CONNECTION";
        private const string ConnectionStringName = "SchoolManagementDb";

        public static bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(ResolveRawConnectionString());
        }

        public static MySqlConnection GetConnection()
        {
            return new MySqlConnection(BuildConnectionString());
        }

        public static void VerifyConnection(string rawConnectionString)
        {
            using var connection = new MySqlConnection(NormalizeConnectionString(rawConnectionString));
            connection.Open();
        }

        public static string GetConnectionSummary(string? rawConnectionString = null)
        {
            rawConnectionString ??= ResolveRawConnectionString();
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                return "Not configured";
            }

            try
            {
                var builder = new MySqlConnectionStringBuilder(rawConnectionString);
                var port = builder.Port == 0 ? 3306 : builder.Port;
                var server = string.IsNullOrWhiteSpace(builder.Server) ? "<missing host>" : builder.Server;
                var database = string.IsNullOrWhiteSpace(builder.Database) ? "<missing database>" : builder.Database;
                return $"{server}:{port} / {database}";
            }
            catch
            {
                return "Invalid connection string";
            }
        }

        public static string NormalizeConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    $"Missing school management connection string. Configure {SchoolDbConnectionEnvVar} or set {ConnectionStringName} in App.config.");
            }

            var builder = new MySqlConnectionStringBuilder(rawConnectionString);
            ValidateConnectionString(builder);
            return builder.ConnectionString;
        }

        private static string BuildConnectionString()
        {
            return NormalizeConnectionString(ResolveRawConnectionString() ?? string.Empty);
        }

        private static string? ResolveRawConnectionString()
        {
            var fromEnv = Environment.GetEnvironmentVariable(SchoolDbConnectionEnvVar);
            var fromConfig = ConfigurationManager.ConnectionStrings[ConnectionStringName]?.ConnectionString;
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : fromConfig;
        }

        private static void ValidateConnectionString(MySqlConnectionStringBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(builder.Server))
            {
                throw new InvalidOperationException("School DB host is required in the connection string.");
            }

            if (string.IsNullOrWhiteSpace(builder.UserID))
            {
                throw new InvalidOperationException("School DB username is required in the connection string.");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new InvalidOperationException("School DB name is required in the connection string.");
            }

            if (builder.Port == 0)
            {
                builder.Port = 3306;
            }

            if (builder.SslMode == MySqlSslMode.None)
            {
                builder.SslMode = MySqlSslMode.Preferred;
            }
        }
    }
}
