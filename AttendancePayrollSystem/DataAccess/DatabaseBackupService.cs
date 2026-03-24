using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseBackupService
    {
        public static DatabaseBackupResult ExportToSql(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A backup file path is required.", nameof(filePath));
            }

            if (SupabaseConfig.UseApi)
            {
                throw new InvalidOperationException(
                    "Database backup export is only available when the app is connected directly to MySQL.");
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            var tables = LoadTablesInDependencyOrder(connection);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
            WriteHeader(writer, connection);

            foreach (var table in tables.AsEnumerable().Reverse())
            {
                writer.WriteLine($"DROP TABLE IF EXISTS {QuoteIdentifier(table)};");
            }

            if (tables.Count > 0)
            {
                writer.WriteLine();
            }

            var totalRows = 0;
            foreach (var table in tables)
            {
                totalRows += WriteTableBackup(writer, connection, table);
            }

            writer.WriteLine("SET FOREIGN_KEY_CHECKS = 1;");
            writer.Flush();

            return new DatabaseBackupResult(filePath, tables.Count, totalRows);
        }

        private static IReadOnlyList<string> LoadTablesInDependencyOrder(MySqlConnection connection)
        {
            const string sql = @"
                SELECT t.TABLE_NAME, k.REFERENCED_TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES t
                LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                    ON k.TABLE_SCHEMA = t.TABLE_SCHEMA
                   AND k.TABLE_NAME = t.TABLE_NAME
                   AND k.REFERENCED_TABLE_NAME IS NOT NULL
                WHERE t.TABLE_SCHEMA = DATABASE()
                  AND t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY t.TABLE_NAME";

            var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var tableName = Convert.ToString(reader["TABLE_NAME"]) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    continue;
                }

                if (!dependencies.TryGetValue(tableName, out var tableDependencies))
                {
                    tableDependencies = [];
                    dependencies[tableName] = tableDependencies;
                }

                if (reader["REFERENCED_TABLE_NAME"] is not DBNull)
                {
                    var referencedTable = Convert.ToString(reader["REFERENCED_TABLE_NAME"]);
                    if (!string.IsNullOrWhiteSpace(referencedTable))
                    {
                        tableDependencies.Add(referencedTable);
                    }
                }
            }

            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var remaining = dependencies.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>(remaining.Count);

            while (remaining.Count > 0)
            {
                var readyTables = remaining
                    .Where(pair => pair.Value.All(resolved.Contains))
                    .Select(pair => pair.Key)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (readyTables.Count == 0)
                {
                    readyTables.Add(remaining.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).First());
                }

                foreach (var table in readyTables)
                {
                    ordered.Add(table);
                    resolved.Add(table);
                    remaining.Remove(table);
                }
            }

            return ordered;
        }

        private static void WriteHeader(StreamWriter writer, MySqlConnection connection)
        {
            writer.WriteLine("-- Attendance Payroll System database backup");
            writer.WriteLine($"-- Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"-- Source server: {connection.DataSource}");
            writer.WriteLine($"-- Source database: {connection.Database}");
            writer.WriteLine("-- Import this file into a blank or replacement MySQL database.");
            writer.WriteLine();
            writer.WriteLine("SET NAMES utf8mb4;");
            writer.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
            writer.WriteLine();
        }

        private static int WriteTableBackup(StreamWriter writer, MySqlConnection connection, string tableName)
        {
            writer.WriteLine($"-- Table structure for {QuoteIdentifier(tableName)}");
            writer.WriteLine($"{GetCreateTableStatement(connection, tableName)};");
            writer.WriteLine();
            writer.WriteLine($"-- Data for {QuoteIdentifier(tableName)}");

            var rowCount = WriteTableData(writer, connection, tableName);
            if (rowCount == 0)
            {
                writer.WriteLine("-- No rows.");
            }

            writer.WriteLine();
            return rowCount;
        }

        private static string GetCreateTableStatement(MySqlConnection connection, string tableName)
        {
            using var command = new MySqlCommand($"SHOW CREATE TABLE {QuoteIdentifier(tableName)}", connection);
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                throw new InvalidOperationException($"Could not load schema for table '{tableName}'.");
            }

            return Convert.ToString(reader["Create Table"]) ??
                   throw new InvalidOperationException($"Could not load schema for table '{tableName}'.");
        }

        private static int WriteTableData(StreamWriter writer, MySqlConnection connection, string tableName)
        {
            using var command = new MySqlCommand($"SELECT * FROM {QuoteIdentifier(tableName)}", connection);
            using var reader = command.ExecuteReader();

            var rowCount = 0;
            var columnNames = Enumerable.Range(0, reader.FieldCount)
                .Select(index => QuoteIdentifier(reader.GetName(index)))
                .ToArray();
            var insertPrefix = $"INSERT INTO {QuoteIdentifier(tableName)} ({string.Join(", ", columnNames)}) VALUES ";

            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = FormatValue(reader.GetValue(i));
                }

                writer.Write(insertPrefix);
                writer.Write('(');
                writer.Write(string.Join(", ", values));
                writer.WriteLine(");");
                rowCount++;
            }

            return rowCount;
        }

        private static string FormatValue(object value)
        {
            if (value is null or DBNull)
            {
                return "NULL";
            }

            return value switch
            {
                bool boolValue => boolValue ? "1" : "0",
                byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
                sbyte or byte or short or ushort or int or uint or long or ulong =>
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
                float floatValue => floatValue.ToString("R", CultureInfo.InvariantCulture),
                double doubleValue => doubleValue.ToString("R", CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
                DateOnly dateOnly => $"'{dateOnly:yyyy-MM-dd}'",
                DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.ffffff}'",
                DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:yyyy-MM-dd HH:mm:ss.ffffff zzz}'",
                TimeSpan timeSpan => $"'{timeSpan:c}'",
                Guid guid => $"'{guid:D}'",
                _ => $"'{MySqlHelper.EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}'"
            };
        }

        private static string QuoteIdentifier(string identifier)
        {
            return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
        }
    }

    public sealed record DatabaseBackupResult(string FilePath, int TableCount, int RowCount);
}
