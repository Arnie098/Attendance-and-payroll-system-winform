using System;
using System.Configuration;
using AttendancePayrollSystem.Services;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseHelper
    {
        private const string DbConnectionEnvVar = "ATTENDANCE_DB_CONNECTION";
        private const string OfflineDbConnectionEnvVar = "ATTENDANCE_OFFLINE_DB_CONNECTION";
        private const string OnlineConnectionStringName = "AttendanceDb";
        private const string OfflineConnectionStringName = "OfflineAttendanceDb";
        private const string SupabaseUrlSetting = "SupabaseUrl";
        private const string SupabasePublishableKeySetting = "SupabasePublishableKey";

        private static readonly string[] _mySqlCoreSchemaStatements =
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
                    TimeInAM DATETIME NULL,
                    TimeOutAM DATETIME NULL,
                    TimeInPM DATETIME NULL,
                    TimeOutPM DATETIME NULL,
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
                    SssDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    PhilHealthDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    PagIbigDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    WithholdingTax DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    TardinessDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    AbsentDays INT NOT NULL DEFAULT 0,
                    AbsenceDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    Deductions DECIMAL(18, 2) NOT NULL,
                    NetPay DECIMAL(18, 2) NOT NULL,
                    ManualDeduction DECIMAL(18, 2) NOT NULL DEFAULT 0,
                    ManualDeductionNote VARCHAR(255) NOT NULL DEFAULT '',
                    Status VARCHAR(30) NOT NULL DEFAULT 'Pending',
                    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_PayrollRecords_Employees FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(EmployeeId)
                ) ENGINE=InnoDB;",
            @"
                CREATE TABLE IF NOT EXISTS LeaveRequests
                (
                    LeaveRequestId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    EmployeeId INT NOT NULL,
                    LeaveType VARCHAR(40) NOT NULL,
                    IsPaid BOOLEAN NOT NULL DEFAULT FALSE,
                    StartDate DATE NOT NULL,
                    EndDate DATE NOT NULL,
                    Reason TEXT NOT NULL,
                    Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
                    ReviewerNotes TEXT NULL,
                    ReviewedBy VARCHAR(80) NULL,
                    ReviewedAt DATETIME NULL,
                    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX IDX_LeaveRequests_EmployeeId (EmployeeId),
                    INDEX IDX_LeaveRequests_Status (Status),
                    INDEX IDX_LeaveRequests_DateRange (StartDate, EndDate),
                    CONSTRAINT FK_LeaveRequests_Employees FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(EmployeeId)
                        ON DELETE CASCADE
                ) ENGINE=InnoDB;",
            @"
                CREATE TABLE IF NOT EXISTS AppBrandingSettings
                (
                    BrandingSettingsId INT NOT NULL PRIMARY KEY,
                    LogoImage LONGBLOB NULL
                ) ENGINE=InnoDB;"
        ];

        private static readonly string[] _sqliteCoreSchemaStatements =
        [
            "PRAGMA foreign_keys = ON;",
            @"
                CREATE TABLE IF NOT EXISTS Employees
                (
                    EmployeeId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    EmployeeCode TEXT NOT NULL UNIQUE,
                    FullName TEXT NOT NULL,
                    Email TEXT NULL,
                    Phone TEXT NULL,
                    Position TEXT NULL,
                    Department TEXT NULL,
                    HourlyRate REAL NOT NULL,
                    HireDate TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    SourceTeacherId INTEGER NULL UNIQUE,
                    SourceUserId INTEGER NULL UNIQUE,
                    ProfileImage BLOB NULL,
                    BiometricTemplate BLOB NULL
                );",
            @"
                CREATE TABLE IF NOT EXISTS AttendanceRecords
                (
                    AttendanceId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    EmployeeId INTEGER NOT NULL,
                    AttendanceDate TEXT NOT NULL,
                    TimeInAM TEXT NULL,
                    TimeOutAM TEXT NULL,
                    TimeInPM TEXT NULL,
                    TimeOutPM TEXT NULL,
                    Status TEXT NOT NULL DEFAULT 'Present',
                    IsBiometricVerified INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId),
                    UNIQUE (EmployeeId, AttendanceDate)
                );",
            @"
                CREATE TABLE IF NOT EXISTS PayrollRecords
                (
                    PayrollId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    EmployeeId INTEGER NOT NULL,
                    PayPeriodStart TEXT NOT NULL,
                    PayPeriodEnd TEXT NOT NULL,
                    RegularHours REAL NOT NULL,
                    OvertimeHours REAL NOT NULL,
                    GrossPay REAL NOT NULL,
                    SssDeduction REAL NOT NULL DEFAULT 0,
                    PhilHealthDeduction REAL NOT NULL DEFAULT 0,
                    PagIbigDeduction REAL NOT NULL DEFAULT 0,
                    WithholdingTax REAL NOT NULL DEFAULT 0,
                    TardinessDeduction REAL NOT NULL DEFAULT 0,
                    AbsentDays INTEGER NOT NULL DEFAULT 0,
                    AbsenceDeduction REAL NOT NULL DEFAULT 0,
                    Deductions REAL NOT NULL,
                    NetPay REAL NOT NULL,
                    ManualDeduction REAL NOT NULL DEFAULT 0,
                    ManualDeductionNote TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId)
                );",
            @"
                CREATE TABLE IF NOT EXISTS LeaveRequests
                (
                    LeaveRequestId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    EmployeeId INTEGER NOT NULL,
                    LeaveType TEXT NOT NULL,
                    IsPaid INTEGER NOT NULL DEFAULT 0,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    ReviewerNotes TEXT NULL,
                    ReviewedBy TEXT NULL,
                    ReviewedAt TEXT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId) ON DELETE CASCADE
                );",
            "CREATE INDEX IF NOT EXISTS IDX_LeaveRequests_EmployeeId ON LeaveRequests (EmployeeId);",
            "CREATE INDEX IF NOT EXISTS IDX_LeaveRequests_Status ON LeaveRequests (Status);",
            "CREATE INDEX IF NOT EXISTS IDX_LeaveRequests_DateRange ON LeaveRequests (StartDate, EndDate);",
            @"
                CREATE TABLE IF NOT EXISTS AppBrandingSettings
                (
                    BrandingSettingsId INTEGER NOT NULL PRIMARY KEY,
                    LogoImage BLOB NULL
                );"
        ];

        public static MySqlConnection GetConnection(DatabaseConnectionTarget target = DatabaseConnectionTarget.Active)
        {
            return new MySqlConnection(BuildConnectionString(target));
        }

        public static DatabaseProvider GetProvider(DatabaseConnectionTarget target = DatabaseConnectionTarget.Active)
        {
            var rawConnectionString = ResolveRawConnectionString(target);
            return DatabaseProviderResolver.DetectProvider(rawConnectionString ?? string.Empty);
        }

        public static bool UsesSqlite(DatabaseConnectionTarget target = DatabaseConnectionTarget.Active)
        {
            return GetProvider(target) == DatabaseProvider.Sqlite;
        }

        public static void EnsureCoreSchema(MySqlConnection connection, MySqlTransaction transaction)
        {
            var statements = connection.Provider == DatabaseProvider.Sqlite
                ? _sqliteCoreSchemaStatements
                : _mySqlCoreSchemaStatements;

            foreach (var sql in statements)
            {
                using var command = new MySqlCommand(sql, connection, transaction);
                command.ExecuteNonQuery();
            }

            if (connection.Provider == DatabaseProvider.MySql)
            {
                EnsureEmployeeIntegrationColumns(connection, transaction);
                EnsureAttendanceTwoSessionColumns(connection, transaction);
                EnsurePayrollDeductionColumns(connection, transaction);
            }
            else
            {
                EnsurePayrollDeductionColumns(connection, transaction);
            }

            EnsureEmployeePayrollInfoColumns(connection, transaction);
            EnsureBrandingCertifyingColumns(connection, transaction);
            EnsurePayrollLineItemsTable(connection, transaction);
            EnsureEmployeeLoansTable(connection, transaction);
            EnsureLeaveRequestAttachmentColumns(connection, transaction);
            EnsureLeaveDocumentsTable(connection, transaction);
            EnsureBrandingSeedRow(connection, transaction);
        }

        public static void VerifyConnection(string rawConnectionString)
        {
            using var connection = new MySqlConnection(NormalizeConnectionString(rawConnectionString));
            connection.Open();
        }

        public static bool CanConnect(DatabaseConnectionTarget target, out string message)
        {
            var rawConnectionString = ResolveRawConnectionString(target);
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                message = "Not configured";
                return false;
            }

            try
            {
                var normalizedConnectionString = NormalizeConnectionString(rawConnectionString);
                using var connection = new MySqlConnection(normalizedConnectionString);
                connection.Open();
                message = GetConnectionSummary(normalizedConnectionString);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static string NormalizeConnectionString(string rawConnectionString)
        {
            return DatabaseProviderResolver.DetectProvider(rawConnectionString) == DatabaseProvider.Sqlite
                ? DatabaseProviderResolver.NormalizeSqliteConnectionString(rawConnectionString)
                : NormalizeMySqlConnectionString(rawConnectionString);
        }

        public static string NormalizeMySqlConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    $"Missing database connection string. Configure {DbConnectionEnvVar} with a Hostinger/MySQL connection string or set AttendanceDb in App.config.");
            }

            var builder = new MySqlConnectionStringBuilder(rawConnectionString);
            ValidateMySqlConnectionString(builder, rawConnectionString);
            return builder.ConnectionString;
        }

        public static string GetConnectionSummary(string? rawConnectionString = null)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                rawConnectionString = ResolveRawConnectionString(DatabaseConnectionTarget.Active);
            }

            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                return "Not configured";
            }

            try
            {
                return DatabaseProviderResolver.DetectProvider(rawConnectionString) == DatabaseProvider.Sqlite
                    ? GetSqliteConnectionSummary(rawConnectionString)
                    : GetMySqlConnectionSummary(rawConnectionString);
            }
            catch
            {
                return "Invalid connection string";
            }
        }

        public static string GetConnectionSummary(DatabaseConnectionTarget target)
        {
            return GetConnectionSummary(ResolveRawConnectionString(target));
        }

        public static string GetActiveConnectionSummary()
        {
            return GetConnectionSummary(GetActiveTarget());
        }

        public static bool IsOnlineConfigured()
        {
            return !string.IsNullOrWhiteSpace(ResolveRawConnectionString(DatabaseConnectionTarget.Online));
        }

        public static bool IsOfflineConfigured()
        {
            return !string.IsNullOrWhiteSpace(ResolveRawConnectionString(DatabaseConnectionTarget.Offline));
        }

        public static DatabaseConnectionTarget GetActiveTarget()
        {
            return DatabaseRuntimeState.UseOfflineDatabase
                ? DatabaseConnectionTarget.Offline
                : DatabaseConnectionTarget.Online;
        }

        public static bool ColumnExists(MySqlConnection connection, string tableName, string columnName, MySqlTransaction? transaction = null)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? $"PRAGMA table_info({tableName})"
                : @"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND LOWER(TABLE_NAME) = LOWER(@TableName)
                      AND LOWER(COLUMN_NAME) = LOWER(@ColumnName)
                    LIMIT 1";

            using var command = new MySqlCommand(sql, connection, transaction);
            if (connection.Provider == DatabaseProvider.MySql)
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                return command.ExecuteScalar() != null;
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var existingColumn = Convert.ToString(reader["name"]);
                if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildConnectionString(DatabaseConnectionTarget target)
        {
            return NormalizeConnectionString(ResolveRawConnectionString(target) ?? string.Empty);
        }

        private static string? ResolveRawConnectionString(DatabaseConnectionTarget target)
        {
            var effectiveTarget = target == DatabaseConnectionTarget.Active
                ? GetActiveTarget()
                : target;

            return effectiveTarget switch
            {
                DatabaseConnectionTarget.Online => ResolveConnectionString(DbConnectionEnvVar, OnlineConnectionStringName),
                DatabaseConnectionTarget.Offline => ResolveConnectionString(OfflineDbConnectionEnvVar, OfflineConnectionStringName),
                _ => ResolveConnectionString(DbConnectionEnvVar, OnlineConnectionStringName)
            };
        }

        private static string? ResolveConnectionString(string envVar, string connectionStringName)
        {
            var fromEnv = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv;
            }

            return ConfigurationManager.ConnectionStrings[connectionStringName]?.ConnectionString;
        }

        private static string GetMySqlConnectionSummary(string rawConnectionString)
        {
            var builder = new MySqlConnectionStringBuilder(rawConnectionString);
            var port = builder.Port == 0 ? 3306 : builder.Port;
            var server = string.IsNullOrWhiteSpace(builder.Server) ? "<missing host>" : builder.Server;
            var database = string.IsNullOrWhiteSpace(builder.Database) ? "<missing database>" : builder.Database;
            return $"{server}:{port} / {database}";
        }

        private static string GetSqliteConnectionSummary(string rawConnectionString)
        {
            var builder = new SqliteConnectionStringBuilder(rawConnectionString);
            var dataSource = string.IsNullOrWhiteSpace(builder.DataSource)
                ? "<missing file>"
                : Environment.ExpandEnvironmentVariables(builder.DataSource);
            return $"SQLite / {dataSource}";
        }

        private static void ValidateMySqlConnectionString(MySqlConnectionStringBuilder builder, string rawConnectionString)
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

            var sslModeWasSpecified =
                rawConnectionString.Contains("SslMode", StringComparison.OrdinalIgnoreCase) ||
                rawConnectionString.Contains("Ssl Mode", StringComparison.OrdinalIgnoreCase);

            if (!sslModeWasSpecified && builder.SslMode == MySqlSslMode.None)
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

        private static void EnsureAttendanceTwoSessionColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (!ColumnExists(connection, "AttendanceRecords", "TimeIn", transaction))
            {
                return;
            }

            const string migrateSql = @"
                ALTER TABLE AttendanceRecords
                    CHANGE COLUMN TimeIn TimeInAM DATETIME NULL,
                    CHANGE COLUMN TimeOut TimeOutAM DATETIME NULL,
                    ADD COLUMN TimeInPM DATETIME NULL AFTER TimeOutAM,
                    ADD COLUMN TimeOutPM DATETIME NULL AFTER TimeInPM";

            using var migrateCommand = new MySqlCommand(migrateSql, connection, transaction);
            migrateCommand.ExecuteNonQuery();
        }

        private static void EnsurePayrollDeductionColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            EnsurePayrollColumnExists(connection, transaction, "SssDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "PhilHealthDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "PagIbigDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "WithholdingTax", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "TardinessDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "AbsentDays", connection.Provider == DatabaseProvider.Sqlite ? "INTEGER NOT NULL DEFAULT 0" : "INT NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "AbsenceDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "ManualDeduction", connection.Provider == DatabaseProvider.Sqlite ? "REAL NOT NULL DEFAULT 0" : "DECIMAL(18, 2) NOT NULL DEFAULT 0");
            EnsurePayrollColumnExists(connection, transaction, "ManualDeductionNote", connection.Provider == DatabaseProvider.Sqlite ? "TEXT NOT NULL DEFAULT ''" : "VARCHAR(255) NOT NULL DEFAULT ''");
        }

        private static void EnsureEmployeePayrollInfoColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            var textDef = connection.Provider == DatabaseProvider.Sqlite ? "TEXT NOT NULL DEFAULT ''" : "VARCHAR(100) NOT NULL DEFAULT ''";
            EnsureEmployeeColumnExists(connection, transaction, "AgencyId", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "SalaryGrade", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "Designation", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "FundSource", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PayrollCycle", connection.Provider == DatabaseProvider.Sqlite
                ? "TEXT NOT NULL DEFAULT 'Monthly'"
                : "VARCHAR(50) NOT NULL DEFAULT 'Monthly'");
            EnsureEmployeeColumnExists(connection, transaction, "TinNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "SssNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "GsisNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PagIbigNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PhilHealthNumber", textDef);

            var nullableDecimalDef = connection.Provider == DatabaseProvider.Sqlite ? "REAL NULL" : "DECIMAL(18,4) NULL";
            EnsureEmployeeColumnExists(connection, transaction, "SssOverride", nullableDecimalDef);
            EnsureEmployeeColumnExists(connection, transaction, "PhilHealthOverride", nullableDecimalDef);
            EnsureEmployeeColumnExists(connection, transaction, "PagIbigOverride", nullableDecimalDef);
            EnsureEmployeeColumnExists(connection, transaction, "WithholdingTaxOverride", nullableDecimalDef);
        }

        private static void EnsureBrandingCertifyingColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            var textDef = connection.Provider == DatabaseProvider.Sqlite ? "TEXT NOT NULL DEFAULT ''" : "VARCHAR(255) NOT NULL DEFAULT ''";
            EnsureBrandingColumnExists(connection, transaction, "AgencyName", textDef);
            EnsureBrandingColumnExists(connection, transaction, "CertifyingOfficerName", textDef);
            EnsureBrandingColumnExists(connection, transaction, "CertifyingOfficerTitle", textDef);
        }

        private static void EnsureBrandingColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            if (ColumnExists(connection, "AppBrandingSettings", columnName, transaction))
            {
                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE AppBrandingSettings ADD COLUMN {columnName} {definition}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }

        private static void EnsureEmployeeLoansTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    CREATE TABLE IF NOT EXISTS EmployeeLoans
                    (
                        LoanId           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        EmployeeId       INTEGER NOT NULL,
                        LoanName         TEXT    NOT NULL DEFAULT '',
                        MonthlyDeduction REAL    NOT NULL DEFAULT 0,
                        RemainingBalance REAL    NOT NULL DEFAULT 0,
                        IsActive         INTEGER NOT NULL DEFAULT 1,
                        StartDate        TEXT    NOT NULL DEFAULT '',
                        FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId) ON DELETE CASCADE
                    );"
                : @"
                    CREATE TABLE IF NOT EXISTS EmployeeLoans
                    (
                        LoanId           INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        EmployeeId       INT NOT NULL,
                        LoanName         VARCHAR(200) NOT NULL DEFAULT '',
                        MonthlyDeduction DECIMAL(18,2) NOT NULL DEFAULT 0,
                        RemainingBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
                        IsActive         BOOLEAN NOT NULL DEFAULT TRUE,
                        StartDate        VARCHAR(20) NOT NULL DEFAULT '',
                        CONSTRAINT FK_EmployeeLoans_Employees FOREIGN KEY (EmployeeId)
                            REFERENCES Employees(EmployeeId) ON DELETE CASCADE
                    ) ENGINE=InnoDB;";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void EnsureLeaveRequestAttachmentColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            var blobDef = connection.Provider == DatabaseProvider.Sqlite ? "BLOB NULL" : "LONGBLOB NULL";
            var nameDef = connection.Provider == DatabaseProvider.Sqlite ? "TEXT NULL" : "VARCHAR(255) NULL";

            if (!ColumnExists(connection, "LeaveRequests", "SupportingDocument", transaction))
            {
                using var cmd = new MySqlCommand(
                    $"ALTER TABLE LeaveRequests ADD COLUMN SupportingDocument {blobDef}",
                    connection, transaction);
                cmd.ExecuteNonQuery();
            }

            if (!ColumnExists(connection, "LeaveRequests", "SupportingDocumentName", transaction))
            {
                using var cmd = new MySqlCommand(
                    $"ALTER TABLE LeaveRequests ADD COLUMN SupportingDocumentName {nameDef}",
                    connection, transaction);
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsureLeaveDocumentsTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (connection.Provider == DatabaseProvider.Sqlite)
            {
                using var createCmd = new MySqlCommand(@"
                    CREATE TABLE IF NOT EXISTS LeaveRequestDocuments
                    (
                        DocumentId     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        LeaveRequestId INTEGER NOT NULL,
                        DocumentName   TEXT NOT NULL,
                        DocumentData   BLOB NOT NULL,
                        FileSizeBytes  INTEGER NOT NULL DEFAULT 0,
                        UploadedAt     TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (LeaveRequestId) REFERENCES LeaveRequests(LeaveRequestId)
                    );", connection, transaction);
                createCmd.ExecuteNonQuery();

                using var idxCmd = new MySqlCommand(
                    "CREATE INDEX IF NOT EXISTS IDX_LeaveRequestDocuments_LeaveRequestId ON LeaveRequestDocuments (LeaveRequestId);",
                    connection, transaction);
                idxCmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new MySqlCommand(@"
                    CREATE TABLE IF NOT EXISTS LeaveRequestDocuments
                    (
                        DocumentId     INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        LeaveRequestId INT NOT NULL,
                        DocumentName   VARCHAR(255) NOT NULL,
                        DocumentData   LONGBLOB NOT NULL,
                        FileSizeBytes  BIGINT NOT NULL DEFAULT 0,
                        UploadedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        INDEX IDX_LeaveRequestDocuments_LeaveRequestId (LeaveRequestId),
                        CONSTRAINT FK_LeaveRequestDocuments_LeaveRequests
                            FOREIGN KEY (LeaveRequestId) REFERENCES LeaveRequests(LeaveRequestId)
                    ) ENGINE=InnoDB;", connection, transaction);
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsurePayrollLineItemsTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    CREATE TABLE IF NOT EXISTS PayrollLineItems
                    (
                        Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        PayrollId INTEGER NOT NULL,
                        Label     TEXT    NOT NULL DEFAULT '',
                        Amount    REAL    NOT NULL DEFAULT 0,
                        ItemType  INTEGER NOT NULL DEFAULT 1,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (PayrollId) REFERENCES PayrollRecords(PayrollId) ON DELETE CASCADE
                    );"
                : @"
                    CREATE TABLE IF NOT EXISTS PayrollLineItems
                    (
                        Id        INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        PayrollId INT NOT NULL,
                        Label     VARCHAR(255) NOT NULL DEFAULT '',
                        Amount    DECIMAL(18, 2) NOT NULL DEFAULT 0,
                        ItemType  INT NOT NULL DEFAULT 1,
                        SortOrder INT NOT NULL DEFAULT 0,
                        CONSTRAINT FK_PayrollLineItems_PayrollRecords FOREIGN KEY (PayrollId)
                            REFERENCES PayrollRecords(PayrollId) ON DELETE CASCADE
                    ) ENGINE=InnoDB;";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void EnsurePayrollColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            if (ColumnExists(connection, "PayrollRecords", columnName, transaction))
            {
                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE PayrollRecords ADD COLUMN {columnName} {definition}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }

        private static void EnsureEmployeeColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            if (ColumnExists(connection, "Employees", columnName, transaction))
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

        private static void EnsureBrandingSeedRow(MySqlConnection connection, MySqlTransaction transaction)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, LogoImage)
                    VALUES (@BrandingSettingsId, NULL)
                    ON CONFLICT(BrandingSettingsId) DO NOTHING"
                : @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, LogoImage)
                    VALUES (@BrandingSettingsId, NULL)
                    ON DUPLICATE KEY UPDATE BrandingSettingsId = VALUES(BrandingSettingsId)";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@BrandingSettingsId", Models.AppBranding.DefaultBrandingSettingsId);
            command.ExecuteNonQuery();
        }
    }
}
