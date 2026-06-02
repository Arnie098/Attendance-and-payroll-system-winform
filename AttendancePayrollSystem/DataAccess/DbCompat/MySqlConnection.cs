using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AttendancePayrollSystem.DataAccess.DbCompat
{
    public sealed class MySqlConnection : IDisposable
    {
        private readonly DbConnection _inner;

        public MySqlConnection(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            Provider = DatabaseProviderResolver.DetectProvider(connectionString);
            ConnectionString = connectionString;
            EnsureSqliteDirectoryExists(connectionString, Provider);
            _inner = Provider == DatabaseProvider.Sqlite
                ? new SqliteConnection(DatabaseProviderResolver.NormalizeSqliteConnectionString(connectionString))
                : new MySqlConnector.MySqlConnection(DatabaseProviderResolver.NormalizeMySqlConnectionString(connectionString));
        }

        internal MySqlConnection(DbConnection inner, DatabaseProvider provider)
        {
            _inner = inner;
            Provider = provider;
            ConnectionString = inner.ConnectionString;
        }

        public string ConnectionString { get; }

        public string DataSource => _inner.DataSource;

        public string Database => _inner.Database;

        public DatabaseProvider Provider { get; }

        internal DbConnection Inner => _inner;

        public void Open()
        {
            _inner.Open();
        }

        public void Close()
        {
            _inner.Close();
        }

        public MySqlTransaction BeginTransaction()
        {
            return new MySqlTransaction(_inner.BeginTransaction(), Provider);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }

        private static void EnsureSqliteDirectoryExists(string connectionString, DatabaseProvider provider)
        {
            if (provider != DatabaseProvider.Sqlite)
            {
                return;
            }

            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = Environment.ExpandEnvironmentVariables(builder.DataSource ?? string.Empty);
            if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            {
                return;
            }

            var fullPath = Path.GetFullPath(dataSource);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
