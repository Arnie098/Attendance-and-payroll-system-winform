using System;
using System.Configuration;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseHelper
    {
        private const string DbConnectionEnvVar = "ATTENDANCE_DB_CONNECTION";
        private const string SupabaseUrlSetting = "SupabaseUrl";
        private const string SupabasePublishableKeySetting = "SupabasePublishableKey";
        private static readonly string[] _coreSchemaStatements =
        [
            @"
                CREATE TABLE IF NOT EXISTS Employees
                (
                    EmployeeId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    EmployeeCode VARCHAR(20) NOT NULL UNIQUE,
                    FullName VARCHAR(150) NOT NULL,
                    Email VARCHAR(150) NULL,
                    Phone VARCHAR(50) NULL,
                    Position VARCHAR(100) NULL,
                    Department VARCHAR(100) NULL,
                    HourlyRate DECIMAL(18, 2) NOT NULL,
                    HireDate DATE NOT NULL,
                    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
                    SourceTeacherId BIGINT NULL,
                    SourceUserId BIGINT NULL,
                    ProfileImage LONGBLOB NULL,
                    BiometricTemplate LONGBLOB NULL,
                    CONSTRAINT UQ_Employees_SourceTeacherId UNIQUE (SourceTeacherId),
                    CONSTRAINT UQ_Employees_SourceUserId UNIQUE (SourceUserId)
                ) ENGINE=InnoDB;",
            @"
                CREATE TABLE IF NOT EXISTS AttendanceRecords
                (
                    AttendanceId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    EmployeeId INT NOT NULL,
                    AttendanceDate DATE NOT NULL,
                    TimeIn DATETIME NULL,
                    TimeOut DATETIME NULL,
                    Status VARCHAR(30) NOT NULL DEFAULT 'Present',
                    IsBiometricVerified BOOLEAN NOT NULL DEFAULT FALSE,
                    CONSTRAINT FK_AttendanceRecords_Employees FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(EmployeeId),
                    CONSTRAINT UQ_Attendance_EmployeeDate UNIQUE (EmployeeId, AttendanceDate)
                ) ENGINE=InnoDB;",
            @"
                CREATE TABLE IF NOT EXISTS PayrollRecords
                (
                    PayrollId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    EmployeeId INT NOT NULL,
                    PayPeriodStart DATE NOT NULL,
                    PayPeriodEnd DATE NOT NULL,
                    RegularHours DECIMAL(10, 2) NOT NULL,
                    OvertimeHours DECIMAL(10, 2) NOT NULL,
                    GrossPay DECIMAL(18, 2) NOT NULL,
                    Deductions DECIMAL(18, 2) NOT NULL,
                    NetPay DECIMAL(18, 2) NOT NULL,
                    Status VARCHAR(30) NOT NULL DEFAULT 'Pending',
                    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_PayrollRecords_Employees FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(EmployeeId)
                ) ENGINE=InnoDB;"
        ];

        public static MySqlConnection GetConnection()
        {
            return new MySqlConnection(BuildConnectionString());
        }

        public static void EnsureCoreSchema(MySqlConnection connection, MySqlTransaction transaction)
        {
            foreach (var sql in _coreSchemaStatements)
            {
                using var command = new MySqlCommand(sql, connection, transaction);
                command.ExecuteNonQuery();
            }

            EnsureEmployeeIntegrationColumns(connection, transaction);
        }

        public static void VerifyConnection(string rawConnectionString)
        {
            using var connection = new MySqlConnection(NormalizeConnectionString(rawConnectionString));
            connection.Open();
        }

        public static string NormalizeConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    $"Missing database connection string. Configure {DbConnectionEnvVar} with a Hostinger/MySQL connection string or set AttendanceDb in App.config.");
            }

            var builder = new MySqlConnectionStringBuilder(rawConnectionString);
            ValidateConnectionString(builder);
            return builder.ConnectionString;
        }

        public static string GetConnectionSummary(string? rawConnectionString = null)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                rawConnectionString = ResolveRawConnectionString();
            }

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

        private static string BuildConnectionString()
        {
            return NormalizeConnectionString(ResolveRawConnectionString() ?? string.Empty);
        }

        private static string? ResolveRawConnectionString()
        {
            var fromEnv = Environment.GetEnvironmentVariable(DbConnectionEnvVar);
            var fromConfig = ConfigurationManager.ConnectionStrings["AttendanceDb"]?.ConnectionString;
            return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : fromConfig;
        }

        private static void ValidateConnectionString(MySqlConnectionStringBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(builder.Server))
            {
                throw new InvalidOperationException("Database host is required in the connection string.");
            }

            if (string.IsNullOrWhiteSpace(builder.UserID))
            {
                throw new InvalidOperationException("Database username is required in the connection string.");
            }

            if ((builder.Password ?? string.Empty).Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            {
                var supabaseUrl = ConfigurationManager.AppSettings[SupabaseUrlSetting];
                var hasSupabaseKey = !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings[SupabasePublishableKeySetting]);
                if (!string.IsNullOrWhiteSpace(supabaseUrl) && hasSupabaseKey)
                {
                    throw new InvalidOperationException(
                        $"Supabase URL/key are configured, but this app still needs a real MySQL connection string in {DbConnectionEnvVar}. The publishable key alone cannot open the database.");
                }

                throw new InvalidOperationException("Connection string contains placeholder password. Set a real database secret before startup.");
            }

            if (string.IsNullOrWhiteSpace(builder.Database))
            {
                throw new InvalidOperationException("Database name is required in the connection string.");
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

        private static void EnsureEmployeeIntegrationColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            EnsureEmployeeColumnExists(connection, transaction, "SourceTeacherId", "BIGINT NULL");
            EnsureEmployeeColumnExists(connection, transaction, "SourceUserId", "BIGINT NULL");
            EnsureEmployeeIndexExists(connection, transaction, "UQ_Employees_SourceTeacherId", "SourceTeacherId", isUnique: true);
            EnsureEmployeeIndexExists(connection, transaction, "UQ_Employees_SourceUserId", "SourceUserId", isUnique: true);
        }

        private static void EnsureEmployeeColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            const string checkSql = @"
                SELECT 1
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND LOWER(TABLE_NAME) = LOWER('Employees')
                  AND LOWER(COLUMN_NAME) = LOWER(@ColumnName)
                LIMIT 1";

            using var checkCommand = new MySqlCommand(checkSql, connection, transaction);
            checkCommand.Parameters.AddWithValue("@ColumnName", columnName);
            if (checkCommand.ExecuteScalar() != null)
            {
                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE Employees ADD COLUMN {columnName} {definition}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }

        private static void EnsureEmployeeIndexExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string indexName,
            string columnName,
            bool isUnique)
        {
            const string checkSql = @"
                SELECT 1
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND LOWER(TABLE_NAME) = LOWER('Employees')
                  AND LOWER(INDEX_NAME) = LOWER(@IndexName)
                LIMIT 1";

            using var checkCommand = new MySqlCommand(checkSql, connection, transaction);
            checkCommand.Parameters.AddWithValue("@IndexName", indexName);
            if (checkCommand.ExecuteScalar() != null)
            {
                return;
            }

            var uniqueKeyword = isUnique ? "UNIQUE " : string.Empty;
            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE Employees ADD {uniqueKeyword}INDEX {indexName} ({columnName})",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }
    }
}
