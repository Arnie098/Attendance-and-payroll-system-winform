using System;
using Microsoft.Data.Sqlite;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseProviderResolver
    {
        public static DatabaseProvider DetectProvider(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                return DatabaseProvider.MySql;
            }

            return rawConnectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                   rawConnectionString.Contains("Filename=", StringComparison.OrdinalIgnoreCase) ||
                   rawConnectionString.Contains(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                   rawConnectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
                ? DatabaseProvider.Sqlite
                : DatabaseProvider.MySql;
        }

        public static string NormalizeMySqlConnectionString(string rawConnectionString)
        {
            return DatabaseHelper.NormalizeMySqlConnectionString(rawConnectionString);
        }

        public static string NormalizeSqliteConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException("SQLite connection string is required.");
            }

            var builder = new SqliteConnectionStringBuilder(rawConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                throw new InvalidOperationException("SQLite Data Source is required.");
            }

            builder.DataSource = Environment.ExpandEnvironmentVariables(builder.DataSource);

            return builder.ConnectionString;
        }
    }
}
