using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseBackupService
    {
        private const string ManifestFileSuffix = ".manifest.json";
        private static readonly JsonSerializerOptions _manifestSerializerOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static DatabaseBackupResult ExportToSql(string filePath, DatabaseBackupMode mode = DatabaseBackupMode.Full)
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

            if (DatabaseHelper.UsesSqlite())
            {
                throw new InvalidOperationException(
                    "SQL export backup is currently available only for MySQL. The offline SQLite database file can be copied directly as a backup.");
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            var tables = LoadTablesInDependencyOrder(connection);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = CaptureSnapshot(connection, tables);
            var referenceManifest = ResolveReferenceManifest(filePath, snapshot, mode);
            DeltaBackupPlan? deltaPlan = null;

            if (mode != DatabaseBackupMode.Full)
            {
                deltaPlan = BuildDeltaPlan(snapshot, referenceManifest!);
            }

            using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
            WriteHeader(writer, snapshot, mode, referenceManifest?.BackupFilePath);

            if (mode == DatabaseBackupMode.Full)
            {
                WriteFullBackup(writer, snapshot);
            }
            else
            {
                WriteDeltaBackup(writer, snapshot, deltaPlan!);
            }

            writer.WriteLine("SET FOREIGN_KEY_CHECKS = 1;");
            writer.Flush();

            var manifest = BuildManifest(snapshot, mode, filePath, referenceManifest?.BackupFilePath);
            var manifestPath = SaveManifest(filePath, manifest);

            return new DatabaseBackupResult(
                FilePath: filePath,
                ManifestPath: manifestPath,
                Mode: mode,
                TableCount: mode == DatabaseBackupMode.Full ? snapshot.TableOrder.Count : deltaPlan!.ImpactedTableCount,
                RowCount: mode == DatabaseBackupMode.Full ? snapshot.TotalRows : deltaPlan!.UpsertCount,
                DeletedRowCount: mode == DatabaseBackupMode.Full ? 0 : deltaPlan!.DeleteCount,
                ReferenceBackupPath: referenceManifest?.BackupFilePath);
        }

        private static DatabaseBackupSnapshot CaptureSnapshot(MySqlConnection connection, IReadOnlyList<string> tableOrder)
        {
            var primaryKeys = LoadPrimaryKeyColumns(connection);
            var tables = new Dictionary<string, BackupTableSnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (var tableName in tableOrder)
            {
                var createTableStatement = GetCreateTableStatement(connection, tableName);
                using var command = new MySqlCommand($"SELECT * FROM {QuoteIdentifier(tableName)}", connection);
                using var reader = command.ExecuteReader();

                var columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetName)
                    .ToList();
                var primaryKeyColumns = primaryKeys.TryGetValue(tableName, out var keyColumns) && keyColumns.Count > 0
                    ? keyColumns
                    : columns;
                var rows = new Dictionary<string, BackupRowSnapshot>(StringComparer.Ordinal);

                while (reader.Read())
                {
                    var values = new List<string>(reader.FieldCount);
                    var valuesByColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var formattedValue = FormatValue(reader.GetValue(i));
                        values.Add(formattedValue);
                        valuesByColumn[columns[i]] = formattedValue;
                    }

                    var rowKey = BuildRowKey(primaryKeyColumns, valuesByColumn);
                    rows[rowKey] = new BackupRowSnapshot { Values = values };
                }

                tables[tableName] = new BackupTableSnapshot
                {
                    CreateTableStatement = createTableStatement,
                    Columns = columns,
                    PrimaryKeyColumns = primaryKeyColumns,
                    Rows = rows
                };
            }

            return new DatabaseBackupSnapshot
            {
                SourceServer = connection.DataSource,
                SourceDatabase = connection.Database,
                ConnectionSummary = DatabaseHelper.GetActiveConnectionSummary(),
                TableOrder = tableOrder.ToList(),
                Tables = tables,
                TotalRows = tables.Values.Sum(table => table.Rows.Count)
            };
        }

        private static DeltaBackupPlan BuildDeltaPlan(DatabaseBackupSnapshot current, BackupSnapshotManifest reference)
        {
            var currentTables = new HashSet<string>(current.TableOrder, StringComparer.OrdinalIgnoreCase);
            var referenceTables = new HashSet<string>(reference.TableOrder, StringComparer.OrdinalIgnoreCase);

            if (!currentTables.SetEquals(referenceTables))
            {
                throw new InvalidOperationException(
                    "The database schema changed since the reference backup. Create a new full backup first.");
            }

            var plan = new DeltaBackupPlan();

            foreach (var tableName in current.TableOrder)
            {
                if (!reference.Tables.TryGetValue(tableName, out var referenceTable))
                {
                    throw new InvalidOperationException(
                        $"Missing reference metadata for table '{tableName}'. Create a new full backup first.");
                }

                var currentTable = current.Tables[tableName];
                if (!string.Equals(currentTable.CreateTableStatement, referenceTable.CreateTableStatement, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Schema for table '{tableName}' changed since the reference backup. Create a new full backup first.");
                }

                var tablePlan = new TableDeltaPlan();

                foreach (var pair in currentTable.Rows)
                {
                    if (!referenceTable.Rows.TryGetValue(pair.Key, out var referenceRow) ||
                        !RowsEqual(pair.Value, referenceRow))
                    {
                        tablePlan.UpsertRows.Add(pair.Value);
                    }
                }

                foreach (var referenceRow in referenceTable.Rows)
                {
                    if (!currentTable.Rows.ContainsKey(referenceRow.Key))
                    {
                        tablePlan.DeleteRows.Add(referenceRow.Value);
                    }
                }

                if (tablePlan.UpsertRows.Count == 0 && tablePlan.DeleteRows.Count == 0)
                {
                    continue;
                }

                plan.Tables[tableName] = tablePlan;
                plan.UpsertCount += tablePlan.UpsertRows.Count;
                plan.DeleteCount += tablePlan.DeleteRows.Count;
            }

            return plan;
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

                if (reader["REFERENCED_TABLE_NAME"] is DBNull)
                {
                    continue;
                }

                var referencedTable = Convert.ToString(reader["REFERENCED_TABLE_NAME"]);
                if (!string.IsNullOrWhiteSpace(referencedTable))
                {
                    tableDependencies.Add(referencedTable);
                }
            }

            var ordered = new List<string>(dependencies.Count);
            var remaining = dependencies.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        private static Dictionary<string, List<string>> LoadPrimaryKeyColumns(MySqlConnection connection)
        {
            const string sql = @"
                SELECT TABLE_NAME, COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE()
                  AND CONSTRAINT_NAME = 'PRIMARY'
                ORDER BY TABLE_NAME, ORDINAL_POSITION";

            var keys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var tableName = Convert.ToString(reader["TABLE_NAME"]) ?? string.Empty;
                var columnName = Convert.ToString(reader["COLUMN_NAME"]) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
                {
                    continue;
                }

                if (!keys.TryGetValue(tableName, out var columns))
                {
                    columns = [];
                    keys[tableName] = columns;
                }

                columns.Add(columnName);
            }

            return keys;
        }

        private static void WriteHeader(StreamWriter writer, DatabaseBackupSnapshot snapshot, DatabaseBackupMode mode, string? referenceBackupPath)
        {
            writer.WriteLine("-- Attendance Payroll System database backup");
            writer.WriteLine($"-- Backup mode: {mode}");
            writer.WriteLine($"-- Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"-- Source server: {snapshot.SourceServer}");
            writer.WriteLine($"-- Source database: {snapshot.SourceDatabase}");
            if (!string.IsNullOrWhiteSpace(referenceBackupPath))
            {
                writer.WriteLine($"-- Reference backup: {referenceBackupPath}");
            }

            writer.WriteLine("-- Keep the .manifest.json file beside this SQL file for future differential or incremental backups.");
            writer.WriteLine();
            writer.WriteLine("SET NAMES utf8mb4;");
            writer.WriteLine("SET FOREIGN_KEY_CHECKS = 0;");
            writer.WriteLine();
        }

        private static void WriteFullBackup(StreamWriter writer, DatabaseBackupSnapshot snapshot)
        {
            foreach (var table in snapshot.TableOrder.AsEnumerable().Reverse())
            {
                writer.WriteLine($"DROP TABLE IF EXISTS {QuoteIdentifier(table)};");
            }

            if (snapshot.TableOrder.Count > 0)
            {
                writer.WriteLine();
            }

            foreach (var tableName in snapshot.TableOrder)
            {
                var table = snapshot.Tables[tableName];
                writer.WriteLine($"-- Table structure for {QuoteIdentifier(tableName)}");
                writer.WriteLine($"{table.CreateTableStatement};");
                writer.WriteLine();
                writer.WriteLine($"-- Data for {QuoteIdentifier(tableName)}");

                if (table.Rows.Count == 0)
                {
                    writer.WriteLine("-- No rows.");
                }
                else
                {
                    foreach (var row in table.Rows.Values)
                    {
                        writer.WriteLine(BuildInsertStatement(tableName, table, row));
                    }
                }

                writer.WriteLine();
            }
        }

        private static void WriteDeltaBackup(StreamWriter writer, DatabaseBackupSnapshot snapshot, DeltaBackupPlan plan)
        {
            if (plan.Tables.Count == 0)
            {
                writer.WriteLine("-- No data changes since the selected reference backup.");
                writer.WriteLine();
                return;
            }

            writer.WriteLine("-- Delete rows removed since the reference backup.");
            foreach (var tableName in snapshot.TableOrder.AsEnumerable().Reverse())
            {
                if (!plan.Tables.TryGetValue(tableName, out var tablePlan) || tablePlan.DeleteRows.Count == 0)
                {
                    continue;
                }

                var table = snapshot.Tables[tableName];
                foreach (var row in tablePlan.DeleteRows)
                {
                    writer.WriteLine(BuildDeleteStatement(tableName, table, row));
                }
            }

            writer.WriteLine();
            writer.WriteLine("-- Insert or update rows changed since the reference backup.");
            foreach (var tableName in snapshot.TableOrder)
            {
                if (!plan.Tables.TryGetValue(tableName, out var tablePlan) || tablePlan.UpsertRows.Count == 0)
                {
                    continue;
                }

                var table = snapshot.Tables[tableName];
                foreach (var row in tablePlan.UpsertRows)
                {
                    writer.WriteLine(BuildUpsertStatement(tableName, table, row));
                }
            }

            writer.WriteLine();
        }

        private static string GetCreateTableStatement(MySqlConnection connection, string tableName)
        {
            using var command = new MySqlCommand($"SHOW CREATE TABLE {QuoteIdentifier(tableName)}", connection);
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                throw new InvalidOperationException($"Could not load schema for table '{tableName}'.");
            }

            var createStatement = Convert.ToString(reader["Create Table"]) ??
                                  throw new InvalidOperationException($"Could not load schema for table '{tableName}'.");

            return NormalizeCreateTableStatement(createStatement);
        }

        private static string NormalizeCreateTableStatement(string createStatement)
        {
            return Regex.Replace(
                createStatement,
                @"\sAUTO_INCREMENT=\d+",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildInsertStatement(string tableName, BackupTableSnapshot table, BackupRowSnapshot row)
        {
            var columns = string.Join(", ", table.Columns.Select(QuoteIdentifier));
            var values = string.Join(", ", row.Values);
            return $"INSERT INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({values});";
        }

        private static string BuildUpsertStatement(string tableName, BackupTableSnapshot table, BackupRowSnapshot row)
        {
            var columns = string.Join(", ", table.Columns.Select(QuoteIdentifier));
            var values = string.Join(", ", row.Values);
            var nonPrimaryKeyColumns = table.Columns
                .Where(column => !table.PrimaryKeyColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (nonPrimaryKeyColumns.Count == 0)
            {
                return $"INSERT IGNORE INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({values});";
            }

            var updates = string.Join(", ",
                nonPrimaryKeyColumns.Select(column => $"{QuoteIdentifier(column)} = VALUES({QuoteIdentifier(column)})"));

            return $"INSERT INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({values}) ON DUPLICATE KEY UPDATE {updates};";
        }

        private static string BuildDeleteStatement(string tableName, BackupTableSnapshot table, BackupRowSnapshot row)
        {
            var predicates = table.PrimaryKeyColumns
                .Select(column => $"{QuoteIdentifier(column)} = {GetColumnValue(table, row, column)}");

            return $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", predicates)};";
        }

        private static string GetColumnValue(BackupTableSnapshot table, BackupRowSnapshot row, string columnName)
        {
            var index = table.Columns.FindIndex(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Could not resolve column '{columnName}' in backup metadata.");
            }

            return row.Values[index];
        }

        private static string BuildRowKey(IReadOnlyList<string> primaryKeyColumns, IReadOnlyDictionary<string, string> valuesByColumn)
        {
            var parts = primaryKeyColumns.Select(column =>
            {
                if (!valuesByColumn.TryGetValue(column, out var value))
                {
                    throw new InvalidOperationException($"Could not resolve primary key column '{column}' while preparing backup metadata.");
                }

                return value;
            });

            return string.Join("\u001F", parts);
        }

        private static bool RowsEqual(BackupRowSnapshot current, BackupRowSnapshot reference)
        {
            return current.Values.SequenceEqual(reference.Values, StringComparer.Ordinal);
        }

        private static BackupSnapshotManifest? ResolveReferenceManifest(string filePath, DatabaseBackupSnapshot snapshot, DatabaseBackupMode mode)
        {
            if (mode == DatabaseBackupMode.Full)
            {
                return null;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    $"{mode} backup requires a previous backup in the destination folder. Create a full backup first.");
            }

            var manifests = new List<BackupSnapshotManifest>();
            foreach (var manifestPath in Directory.EnumerateFiles(directory, $"*{ManifestFileSuffix}", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<BackupSnapshotManifest>(File.ReadAllText(manifestPath), _manifestSerializerOptions);
                    if (manifest == null)
                    {
                        continue;
                    }

                    if (!string.Equals(manifest.SourceDatabase, snapshot.SourceDatabase, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(manifest.SourceServer, snapshot.SourceServer, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (mode == DatabaseBackupMode.Differential && manifest.Mode != DatabaseBackupMode.Full)
                    {
                        continue;
                    }

                    manifests.Add(manifest);
                }
                catch
                {
                    // Ignore malformed metadata files in the folder.
                }
            }

            var reference = manifests
                .OrderByDescending(manifest => manifest.CreatedAtUtc)
                .FirstOrDefault();

            if (reference != null)
            {
                return reference;
            }

            throw new InvalidOperationException(
                mode == DatabaseBackupMode.Differential
                    ? "Differential backup requires a previous full backup in the selected folder."
                    : "Incremental backup requires a previous full, differential, or incremental backup in the selected folder.");
        }

        private static BackupSnapshotManifest BuildManifest(DatabaseBackupSnapshot snapshot, DatabaseBackupMode mode, string backupFilePath, string? referenceBackupPath)
        {
            return new BackupSnapshotManifest
            {
                Mode = mode,
                BackupFilePath = Path.GetFullPath(backupFilePath),
                ReferenceBackupFilePath = referenceBackupPath,
                SourceServer = snapshot.SourceServer,
                SourceDatabase = snapshot.SourceDatabase,
                ConnectionSummary = snapshot.ConnectionSummary,
                CreatedAtUtc = DateTime.UtcNow,
                TableOrder = snapshot.TableOrder,
                Tables = snapshot.Tables
            };
        }

        private static string SaveManifest(string filePath, BackupSnapshotManifest manifest)
        {
            var manifestPath = GetManifestPath(filePath);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _manifestSerializerOptions), new UTF8Encoding(false));
            return manifestPath;
        }

        private static string GetManifestPath(string filePath) => $"{filePath}{ManifestFileSuffix}";

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

        private static string QuoteIdentifier(string identifier) =>
            $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

        private sealed class DeltaBackupPlan
        {
            public Dictionary<string, TableDeltaPlan> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int UpsertCount { get; set; }
            public int DeleteCount { get; set; }
            public int ImpactedTableCount => Tables.Count;
        }

        private sealed class TableDeltaPlan
        {
            public List<BackupRowSnapshot> UpsertRows { get; } = [];
            public List<BackupRowSnapshot> DeleteRows { get; } = [];
        }

        private sealed class DatabaseBackupSnapshot
        {
            public string SourceServer { get; init; } = string.Empty;
            public string SourceDatabase { get; init; } = string.Empty;
            public string ConnectionSummary { get; init; } = string.Empty;
            public List<string> TableOrder { get; init; } = [];
            public Dictionary<string, BackupTableSnapshot> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public int TotalRows { get; init; }
        }

        private sealed class BackupSnapshotManifest
        {
            public DatabaseBackupMode Mode { get; init; }
            public string BackupFilePath { get; init; } = string.Empty;
            public string? ReferenceBackupFilePath { get; init; }
            public string SourceServer { get; init; } = string.Empty;
            public string SourceDatabase { get; init; } = string.Empty;
            public string ConnectionSummary { get; init; } = string.Empty;
            public DateTime CreatedAtUtc { get; init; }
            public List<string> TableOrder { get; init; } = [];
            public Dictionary<string, BackupTableSnapshot> Tables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class BackupTableSnapshot
        {
            public string CreateTableStatement { get; init; } = string.Empty;
            public List<string> Columns { get; init; } = [];
            public List<string> PrimaryKeyColumns { get; init; } = [];
            public Dictionary<string, BackupRowSnapshot> Rows { get; init; } = new(StringComparer.Ordinal);
        }

        private sealed class BackupRowSnapshot
        {
            public List<string> Values { get; init; } = [];
        }
    }

    public enum DatabaseBackupMode
    {
        Full,
        Differential,
        Incremental
    }

    public sealed record DatabaseBackupResult(
        string FilePath,
        string ManifestPath,
        DatabaseBackupMode Mode,
        int TableCount,
        int RowCount,
        int DeletedRowCount,
        string? ReferenceBackupPath);
}
