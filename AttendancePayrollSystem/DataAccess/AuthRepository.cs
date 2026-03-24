using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class AuthRepository
    {
        private const string BootstrapAdminPasswordEnvVar = "ATTENDANCE_BOOTSTRAP_ADMIN_PASSWORD";
        private const string DemoModeEnvVar = "ATTENDANCE_ENABLE_DEMO_ACCOUNTS";
        private const string DemoEmployeePasswordEnvVar = "ATTENDANCE_DEMO_EMPLOYEE_PASSWORD";
        private const string BootstrapAdminPasswordSetting = "BootstrapAdminPassword";
        private const string DemoModeSetting = "EnableDemoAccounts";
        private const string DemoEmployeePasswordSetting = "DemoEmployeePassword";
        private const int PasswordSaltSize = 16;
        private const int PasswordHashSize = 32;
        private const int PasswordHashIterations = 210000;
        private const string DefaultAdminUsername = "admin";
        private const string DevPolicyScriptPath = "AttendancePayrollSystem/SUPABASE_DEV_POLICIES.sql";
        private const string KnownLegacyAdminDefaultHash = "240BE518FABD2724DDB6F04EEB1DA5967448D7E831C08C8FA822809F74C720A9";
        private const string KnownLegacyEmployeeDefaultHash = "5B2F8E27E2E5B4081C03CE70B288C87BD1263140CBD1BD9AE078123509B7CAFF";

        public void EnsureAuthSchemaAndSeedDefaults()
        {
            if (SupabaseConfig.UseApi)
            {
                EnsureAuthSchemaAndSeedDefaultsViaApi();
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);
                EnsureUserAccountsTable(connection, transaction);
                EnsurePasswordColumns(connection, transaction);
                DisableAccountsUsingKnownLegacyDefaultPasswords(connection, transaction);
                EnsureDefaultAdmin(connection, transaction);
                EnsureEmployeeAccounts(connection, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void EnsureLocalAuthSchema()
        {
            if (SupabaseConfig.UseApi)
            {
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);
                EnsureUserAccountsTable(connection, transaction);
                EnsurePasswordColumns(connection, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void EnsureEmployeeAccounts()
        {
            if (SupabaseConfig.UseApi)
            {
                EnsureEmployeeAccountsViaApi();
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);
                EnsureUserAccountsTable(connection, transaction);
                EnsurePasswordColumns(connection, transaction);
                DisableAccountsUsingKnownLegacyDefaultPasswords(connection, transaction);
                EnsureEmployeeAccounts(connection, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public Employee RegisterEmployee(EmployeeRegistrationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            EmployeeSourcePolicy.EnsureEmployeeRegistrationAllowed();

            var normalizedUsername = NormalizeUsername(request.Username);
            ValidateRegistrationRequest(request, normalizedUsername);

            return SupabaseConfig.UseApi
                ? RegisterEmployeeViaApi(request, normalizedUsername)
                : RegisterEmployeeViaDatabase(request, normalizedUsername);
        }

        public UserAccount? Authenticate(string username, string password)
        {
            if (SupabaseConfig.UseApi)
            {
                return AuthenticateViaApi(username, password);
            }

            const string sql = @"
                SELECT UserAccountId, Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive
                FROM UserAccounts
                WHERE Username = @Username
                LIMIT 1";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Username", username.Trim().ToLowerInvariant());
            connection.Open();
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            var storedHash = Convert.ToString(reader["PasswordHash"]) ?? string.Empty;
            var salt = reader["PasswordSalt"] is DBNull ? null : (byte[])reader["PasswordSalt"];
            var hashIterations = reader["HashIterations"] is DBNull
                ? PasswordHashIterations
                : Convert.ToInt32(reader["HashIterations"]);

            var usesExternalPbkdf2Hash = IsExternalPbkdf2PasswordHash(storedHash);
            var isLegacyHash = !usesExternalPbkdf2Hash && (salt == null || salt.Length == 0 || hashIterations <= 0);
            var passwordIsValid = usesExternalPbkdf2Hash
                ? VerifyExternalPbkdf2Password(password, storedHash)
                : isLegacyHash
                ? string.Equals(storedHash, HashLegacyPassword(password), StringComparison.OrdinalIgnoreCase)
                : VerifyPassword(password, storedHash, salt, hashIterations);

            if (!passwordIsValid)
            {
                return null;
            }

            var account = new UserAccount
            {
                UserAccountId = Convert.ToInt32(reader["UserAccountId"]),
                Username = Convert.ToString(reader["Username"]) ?? string.Empty,
                PasswordHash = storedHash,
                Role = Convert.ToString(reader["Role"]) ?? string.Empty,
                EmployeeId = reader["EmployeeId"] is DBNull ? null : Convert.ToInt32(reader["EmployeeId"]),
                IsActive = Convert.ToBoolean(reader["IsActive"])
            };

            reader.Close();
            UpgradeLegacyHashIfNeeded(account.UserAccountId, isLegacyHash, password);

            return account;
        }

        public static bool IsDemoModeEnabled =>
            GetOptionalBool(DemoModeSetting, DemoModeEnvVar);

        private static Employee RegisterEmployeeViaDatabase(EmployeeRegistrationRequest request, string normalizedUsername)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);
                EnsureUserAccountsTable(connection, transaction);
                EnsurePasswordColumns(connection, transaction);

                if (UsernameExists(connection, transaction, normalizedUsername))
                {
                    throw new InvalidOperationException("Username is already in use.");
                }

                var employee = CreateRegisteredEmployee(request, GenerateEmployeeCode(connection, transaction));
                employee.EmployeeId = InsertEmployee(connection, transaction, employee);

                var secret = CreatePasswordSecret(request.Password);
                using var command = new MySqlCommand(@"
                    INSERT INTO UserAccounts (Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive)
                    VALUES (@Username, @PasswordHash, @PasswordSalt, @HashIterations, @Role, @EmployeeId, TRUE)", connection, transaction);

                command.Parameters.AddWithValue("@Username", normalizedUsername);
                command.Parameters.AddWithValue("@PasswordHash", secret.Hash);
                command.Parameters.AddWithValue("@PasswordSalt", secret.Salt);
                command.Parameters.AddWithValue("@HashIterations", secret.Iterations);
                command.Parameters.AddWithValue("@Role", UserRoles.Employee);
                command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
                command.ExecuteNonQuery();

                transaction.Commit();
                return employee;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static Employee RegisterEmployeeViaApi(EmployeeRegistrationRequest request, string normalizedUsername)
        {
            if (UsernameExistsViaApi(normalizedUsername))
            {
                throw new InvalidOperationException("Username is already in use.");
            }

            var employee = CreateRegisteredEmployee(request, GenerateEmployeeCodeViaApi());

            try
            {
                var createdEmployee = SupabaseRestClient.InsertAndReturnSingle<ApiEmployeeRecord>(
                    "employees",
                    BuildApiEmployeePayload(employee));

                employee.EmployeeId = createdEmployee.EmployeeId;

                var secret = CreatePasswordSecret(request.Password);
                SupabaseRestClient.InsertAndReturnSingle<ApiUserAccount>(
                    "useraccounts",
                    new
                    {
                        username = normalizedUsername,
                        passwordhash = secret.Hash,
                        passwordsalt = ToByteaLiteral(secret.Salt),
                        hashiterations = secret.Iterations,
                        role = UserRoles.Employee,
                        employeeid = employee.EmployeeId,
                        isactive = true
                    });

                return employee;
            }
            catch (Exception ex)
            {
                if (employee.EmployeeId > 0)
                {
                    try
                    {
                        SupabaseRestClient.Delete(
                            "employees",
                            new Dictionary<string, string>
                            {
                                ["employeeid"] = $"eq.{employee.EmployeeId}"
                            });
                    }
                    catch
                    {
                        // Best effort cleanup after a partial API registration failure.
                    }
                }

                throw BuildApiModeException("Failed to register employee account through the Supabase API", ex);
            }
        }

        private static void EnsureAuthSchemaAndSeedDefaultsViaApi()
        {
            try
            {
                EnsureApiTablesAccessible();
                DisableAccountsUsingKnownLegacyDefaultPasswordsViaApi();
                EnsureDefaultAdminViaApi();
                EnsureEmployeeAccountsViaApi();
            }
            catch (Exception ex)
            {
                throw BuildApiModeException("Failed to initialize Supabase API login data", ex);
            }
        }

        private static void EnsureEmployeeAccountsViaApi()
        {
            try
            {
                var employees = LoadEmployeesForSync();
                var accounts = LoadApiAccounts();
                var employeeAccounts = accounts
                    .Where(account => account.EmployeeId.HasValue)
                    .ToDictionary(account => account.EmployeeId!.Value);

                if (IsDemoModeEnabled)
                {
                    var demoPassword = Environment.GetEnvironmentVariable(DemoEmployeePasswordEnvVar);
                    if (string.IsNullOrWhiteSpace(demoPassword))
                    {
                        throw new InvalidOperationException(
                            $"Missing {DemoEmployeePasswordEnvVar}. Set a strong demo password to enable auto-generated employee accounts.");
                    }

                    ValidateStrongPassword(demoPassword, DemoEmployeePasswordEnvVar);

                    foreach (var employee in employees.Where(employee => !employeeAccounts.ContainsKey(employee.EmployeeId)))
                    {
                        var username = GetAvailableUsernameForApi(accounts, employee.EmployeeCode, employee.EmployeeId);
                        var secret = CreatePasswordSecret(demoPassword);
                        var created = SupabaseRestClient.InsertAndReturnSingle<ApiUserAccount>(
                            "useraccounts",
                            new
                            {
                                username,
                                passwordhash = secret.Hash,
                                passwordsalt = ToByteaLiteral(secret.Salt),
                                hashiterations = secret.Iterations,
                                role = UserRoles.Employee,
                                employeeid = employee.EmployeeId,
                                isactive = employee.IsActive
                            });

                        accounts.Add(created);
                        employeeAccounts[employee.EmployeeId] = created;
                    }
                }

                foreach (var employee in employees)
                {
                    if (!employeeAccounts.TryGetValue(employee.EmployeeId, out var account))
                    {
                        continue;
                    }

                    if (account.IsActive == employee.IsActive)
                    {
                        continue;
                    }

                    SupabaseRestClient.Update(
                        "useraccounts",
                        new { isactive = employee.IsActive },
                        new Dictionary<string, string>
                        {
                            ["useraccountid"] = $"eq.{account.UserAccountId}"
                        });
                }
            }
            catch (Exception ex)
            {
                throw BuildApiModeException("Failed to synchronize employee accounts through the Supabase API", ex);
            }
        }

        private static UserAccount? AuthenticateViaApi(string username, string password)
        {
            try
            {
                var accountRecord = SupabaseRestClient.GetSingleOrDefault<ApiUserAccount>(
                    "useraccounts",
                    new Dictionary<string, string>
                    {
                        ["select"] = "useraccountid,username,passwordhash,passwordsalt,hashiterations,role,employeeid,isactive",
                        ["username"] = $"eq.{username.Trim().ToLowerInvariant()}",
                        ["limit"] = "1"
                    });

                if (accountRecord == null)
                {
                    return null;
                }

                var salt = ParseBytea(accountRecord.PasswordSalt);
                var hashIterations = accountRecord.HashIterations <= 0 ? PasswordHashIterations : accountRecord.HashIterations;
                var usesExternalPbkdf2Hash = IsExternalPbkdf2PasswordHash(accountRecord.PasswordHash);
                var isLegacyHash = !usesExternalPbkdf2Hash && (salt == null || salt.Length == 0 || hashIterations <= 0);
                var passwordIsValid = usesExternalPbkdf2Hash
                    ? VerifyExternalPbkdf2Password(password, accountRecord.PasswordHash)
                    : isLegacyHash
                    ? string.Equals(accountRecord.PasswordHash, HashLegacyPassword(password), StringComparison.OrdinalIgnoreCase)
                    : VerifyPassword(password, accountRecord.PasswordHash, salt, hashIterations);

                if (!passwordIsValid)
                {
                    return null;
                }

                UpgradeLegacyHashIfNeededViaApi(accountRecord.UserAccountId, isLegacyHash, password);

                return new UserAccount
                {
                    UserAccountId = accountRecord.UserAccountId,
                    Username = accountRecord.Username,
                    PasswordHash = accountRecord.PasswordHash,
                    Role = accountRecord.Role,
                    EmployeeId = accountRecord.EmployeeId,
                    IsActive = accountRecord.IsActive
                };
            }
            catch (Exception ex)
            {
                throw BuildApiModeException("Failed to query login accounts through the Supabase API", ex);
            }
        }

        private static void EnsureApiTablesAccessible()
        {
            try
            {
                SupabaseRestClient.GetList<ApiUserAccount>(
                    "useraccounts",
                    new Dictionary<string, string>
                    {
                        ["select"] = "useraccountid",
                        ["limit"] = "1"
                    });

                SupabaseRestClient.GetList<ApiEmployeeSyncRow>(
                    "employees",
                    new Dictionary<string, string>
                    {
                        ["select"] = "employeeid",
                        ["limit"] = "1"
                    });
            }
            catch (Exception ex)
            {
                throw BuildApiModeException("Supabase API access to required tables failed", ex);
            }
        }

        private static void DisableAccountsUsingKnownLegacyDefaultPasswordsViaApi()
        {
            var accounts = SupabaseRestClient.GetList<ApiUserAccount>(
                "useraccounts",
                new Dictionary<string, string>
                {
                    ["select"] = "useraccountid,passwordhash,passwordsalt,isactive"
                });

            foreach (var account in accounts)
            {
                var hasLegacyDefaultPassword =
                    string.IsNullOrWhiteSpace(account.PasswordSalt) &&
                    (string.Equals(account.PasswordHash, KnownLegacyAdminDefaultHash, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(account.PasswordHash, KnownLegacyEmployeeDefaultHash, StringComparison.OrdinalIgnoreCase));

                if (!hasLegacyDefaultPassword || !account.IsActive)
                {
                    continue;
                }

                SupabaseRestClient.Update(
                    "useraccounts",
                    new { isactive = false },
                    new Dictionary<string, string>
                    {
                        ["useraccountid"] = $"eq.{account.UserAccountId}"
                    });
            }
        }

        private static void EnsureDefaultAdminViaApi()
        {
            var existingAdmin = SupabaseRestClient.GetSingleOrDefault<ApiUserAccount>(
                "useraccounts",
                new Dictionary<string, string>
                {
                    ["select"] = "useraccountid,username,passwordhash,passwordsalt,hashiterations,role,employeeid,isactive",
                    ["username"] = $"eq.{DefaultAdminUsername}",
                    ["limit"] = "1"
                });

            if (existingAdmin != null)
            {
                return;
            }

            var bootstrapPassword = GetOptionalValue(BootstrapAdminPasswordSetting, BootstrapAdminPasswordEnvVar);
            if (string.IsNullOrWhiteSpace(bootstrapPassword))
            {
                throw new InvalidOperationException(
                    $"Missing {BootstrapAdminPasswordEnvVar}. Set a strong admin password before first startup.");
            }

            ValidateStrongPassword(bootstrapPassword, BootstrapAdminPasswordEnvVar);
            var secret = CreatePasswordSecret(bootstrapPassword);

            SupabaseRestClient.InsertAndReturnSingle<ApiUserAccount>(
                "useraccounts",
                new
                {
                    username = DefaultAdminUsername,
                    passwordhash = secret.Hash,
                    passwordsalt = ToByteaLiteral(secret.Salt),
                    hashiterations = secret.Iterations,
                    role = UserRoles.Admin,
                    employeeid = (int?)null,
                    isactive = true
                });
        }

        private static List<ApiEmployeeSyncRow> LoadEmployeesForSync()
        {
            return SupabaseRestClient.GetList<ApiEmployeeSyncRow>(
                "employees",
                new Dictionary<string, string>
                {
                    ["select"] = "employeeid,employeecode,isactive",
                    ["order"] = "employeeid.asc"
                });
        }

        private static List<ApiUserAccount> LoadApiAccounts()
        {
            return SupabaseRestClient.GetList<ApiUserAccount>(
                "useraccounts",
                new Dictionary<string, string>
                {
                    ["select"] = "useraccountid,username,passwordhash,passwordsalt,hashiterations,role,employeeid,isactive",
                    ["order"] = "useraccountid.asc"
                });
        }

        private static bool UsernameExistsViaApi(string normalizedUsername)
        {
            return SupabaseRestClient.GetSingleOrDefault<ApiUserAccount>(
                "useraccounts",
                new Dictionary<string, string>
                {
                    ["select"] = "useraccountid",
                    ["username"] = $"eq.{normalizedUsername}",
                    ["limit"] = "1"
                }) != null;
        }

        private static string GenerateEmployeeCodeViaApi()
        {
            var existingCodes = LoadEmployeesForSync()
                .Select(employee => employee.EmployeeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var nextSequence = existingCodes
                .Select(ParseGeneratedEmployeeCode)
                .Where(sequence => sequence.HasValue)
                .DefaultIfEmpty(0)
                .Max()
                .GetValueOrDefault() + 1;

            var candidate = $"EMP-{nextSequence:D4}";
            while (existingCodes.Contains(candidate))
            {
                nextSequence++;
                candidate = $"EMP-{nextSequence:D4}";
            }

            return candidate;
        }

        private static string GetAvailableUsernameForApi(IEnumerable<ApiUserAccount> accounts, string employeeCode, int employeeId)
        {
            var baseUsername = string.IsNullOrWhiteSpace(employeeCode)
                ? $"employee-{employeeId}"
                : employeeCode.Trim().ToLowerInvariant();

            var candidate = baseUsername;
            var suffix = 0;
            while (accounts.Any(account => string.Equals(account.Username, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                suffix++;
                candidate = $"{baseUsername}-{suffix}";
            }

            return candidate;
        }

        private static Employee CreateRegisteredEmployee(EmployeeRegistrationRequest request, string employeeCode)
        {
            return new Employee
            {
                EmployeeCode = employeeCode,
                FullName = request.FullName.Trim(),
                Email = request.Email.Trim(),
                Phone = request.Phone.Trim(),
                Position = request.Position.Trim(),
                Department = request.Department.Trim(),
                HourlyRate = 0m,
                HireDate = request.HireDate == default ? DateTime.Today : request.HireDate.Date,
                IsActive = true,
                ProfileImage = request.ProfileImage
            };
        }

        private static string NormalizeUsername(string username)
        {
            var normalized = username.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException("Username is required.");
            }

            if (normalized.Length > 80)
            {
                throw new InvalidOperationException("Username must be 80 characters or fewer.");
            }

            if (normalized.Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException("Username cannot contain spaces.");
            }

            return normalized;
        }

        private static void ValidateRegistrationRequest(EmployeeRegistrationRequest request, string normalizedUsername)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                throw new InvalidOperationException("Full name is required.");
            }

            if (normalizedUsername.Length < 3)
            {
                throw new InvalidOperationException("Username must be at least 3 characters.");
            }

            if (request.HireDate == default)
            {
                throw new InvalidOperationException("Hire date is required.");
            }

            ValidateStrongPassword(request.Password, nameof(request.Password));
        }

        private static int InsertEmployee(MySqlConnection connection, MySqlTransaction transaction, Employee employee)
        {
            using var command = new MySqlCommand(@"
                INSERT INTO Employees
                (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, ProfileImage, BiometricTemplate)
                VALUES
                (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @ProfileImage, NULL);", connection, transaction);

            command.Parameters.AddWithValue("@EmployeeCode", employee.EmployeeCode);
            command.Parameters.AddWithValue("@FullName", employee.FullName);
            command.Parameters.AddWithValue("@Email", ToDbValue(employee.Email));
            command.Parameters.AddWithValue("@Phone", ToDbValue(employee.Phone));
            command.Parameters.AddWithValue("@Position", ToDbValue(employee.Position));
            command.Parameters.AddWithValue("@Department", ToDbValue(employee.Department));
            command.Parameters.AddWithValue("@HourlyRate", employee.HourlyRate);
            command.Parameters.AddWithValue("@HireDate", employee.HireDate.Date);
            command.Parameters.AddWithValue("@IsActive", employee.IsActive);
            command.Parameters.AddWithValue("@ProfileImage", employee.ProfileImage is null ? DBNull.Value : employee.ProfileImage);
            command.ExecuteNonQuery();
            return Convert.ToInt32(command.LastInsertedId);
        }

        private static string GenerateEmployeeCode(MySqlConnection connection, MySqlTransaction transaction)
        {
            const string sql = @"
                SELECT COALESCE(MAX(CAST(SUBSTRING(EmployeeCode, 5) AS UNSIGNED)), 0)
                FROM Employees
                WHERE EmployeeCode REGEXP '^EMP-[0-9]+$'";

            using var command = new MySqlCommand(sql, connection, transaction);
            var nextSequence = Convert.ToInt32(command.ExecuteScalar()) + 1;
            var candidate = $"EMP-{nextSequence:D4}";

            while (EmployeeCodeExists(connection, transaction, candidate))
            {
                nextSequence++;
                candidate = $"EMP-{nextSequence:D4}";
            }

            return candidate;
        }

        private static bool EmployeeCodeExists(MySqlConnection connection, MySqlTransaction transaction, string employeeCode)
        {
            using var command = new MySqlCommand(@"
                SELECT 1
                FROM Employees
                WHERE EmployeeCode = @EmployeeCode
                LIMIT 1", connection, transaction);

            command.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            return command.ExecuteScalar() != null;
        }

        private static int? ParseGeneratedEmployeeCode(string employeeCode)
        {
            if (!employeeCode.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return int.TryParse(employeeCode[4..], out var parsed)
                ? parsed
                : null;
        }

        private static object BuildApiEmployeePayload(Employee employee)
        {
            return new
            {
                employeecode = employee.EmployeeCode,
                fullname = employee.FullName,
                email = ToApiValue(employee.Email),
                phone = ToApiValue(employee.Phone),
                position = ToApiValue(employee.Position),
                department = ToApiValue(employee.Department),
                hourlyrate = employee.HourlyRate,
                hiredate = employee.HireDate.Date,
                isactive = employee.IsActive,
                profileimage = employee.ProfileImage == null ? null : ToByteaLiteral(employee.ProfileImage)
            };
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

        private static object ToDbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private static string? ToApiValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ToByteaLiteral(byte[] value)
        {
            return $@"\x{Convert.ToHexString(value).ToLowerInvariant()}";
        }

        private static void EnsureUserAccountsTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS UserAccounts
                (
                    UserAccountId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Username VARCHAR(80) NOT NULL UNIQUE,
                    PasswordHash VARCHAR(256) NOT NULL,
                    PasswordSalt VARBINARY(64) NULL,
                    HashIterations INT NOT NULL DEFAULT 210000,
                    Role VARCHAR(20) NOT NULL,
                    EmployeeId INT NULL,
                    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
                    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX IDX_UserAccounts_Role (Role),
                    CONSTRAINT FK_UserAccounts_Employee FOREIGN KEY (EmployeeId)
                        REFERENCES Employees(EmployeeId)
                        ON DELETE CASCADE,
                    CONSTRAINT UQ_UserAccounts_EmployeeId UNIQUE (EmployeeId)
                ) ENGINE=InnoDB;";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void EnsurePasswordColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            EnsureColumnExists(connection, transaction, "PasswordSalt", "VARBINARY(64) NULL");
            EnsureColumnExists(connection, transaction, "HashIterations", $"INT NOT NULL DEFAULT {PasswordHashIterations}");
        }

        private static void DisableAccountsUsingKnownLegacyDefaultPasswords(
            MySqlConnection connection,
            MySqlTransaction transaction)
        {
            const string sql = @"
                UPDATE UserAccounts
                SET IsActive = FALSE
                WHERE (PasswordSalt IS NULL OR LENGTH(PasswordSalt) = 0)
                  AND UPPER(PasswordHash) IN (@AdminHash, @EmployeeHash)";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@AdminHash", KnownLegacyAdminDefaultHash);
            command.Parameters.AddWithValue("@EmployeeHash", KnownLegacyEmployeeDefaultHash);
            command.ExecuteNonQuery();
        }

        private static void EnsureColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            const string checkSql = @"
                SELECT 1
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND LOWER(TABLE_NAME) = LOWER('UserAccounts')
                  AND LOWER(COLUMN_NAME) = LOWER(@ColumnName)
                LIMIT 1";

            using var checkCommand = new MySqlCommand(checkSql, connection, transaction);
            checkCommand.Parameters.AddWithValue("@ColumnName", columnName);
            var columnExists = checkCommand.ExecuteScalar() != null;
            if (columnExists)
            {
                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE UserAccounts ADD COLUMN {columnName} {definition}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }

        private static void EnsureDefaultAdmin(MySqlConnection connection, MySqlTransaction transaction)
        {
            const string existsSql = @"
                SELECT 1
                FROM UserAccounts
                WHERE Username = @Username
                LIMIT 1";

            using var existsCommand = new MySqlCommand(existsSql, connection, transaction);
            existsCommand.Parameters.AddWithValue("@Username", DefaultAdminUsername);
            if (existsCommand.ExecuteScalar() != null)
            {
                return;
            }

            var bootstrapPassword = GetOptionalValue(BootstrapAdminPasswordSetting, BootstrapAdminPasswordEnvVar);
            if (string.IsNullOrWhiteSpace(bootstrapPassword))
            {
                throw new InvalidOperationException(
                    $"Missing {BootstrapAdminPasswordEnvVar}. Set a strong admin password before first startup.");
            }

            ValidateStrongPassword(bootstrapPassword, BootstrapAdminPasswordEnvVar);
            var passwordSecret = CreatePasswordSecret(bootstrapPassword);

            const string sql = @"
                INSERT INTO UserAccounts (Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive)
                VALUES (@Username, @PasswordHash, @PasswordSalt, @HashIterations, @Role, NULL, TRUE)";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Username", DefaultAdminUsername);
            command.Parameters.AddWithValue("@PasswordHash", passwordSecret.Hash);
            command.Parameters.AddWithValue("@PasswordSalt", passwordSecret.Salt);
            command.Parameters.AddWithValue("@HashIterations", passwordSecret.Iterations);
            command.Parameters.AddWithValue("@Role", UserRoles.Admin);
            command.ExecuteNonQuery();
        }

        private static void EnsureEmployeeAccounts(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (IsDemoModeEnabled)
            {
                EnsureDemoEmployeeAccounts(connection, transaction);
            }
            const string updateStatusSql = @"
                UPDATE UserAccounts u
                INNER JOIN Employees e ON e.EmployeeId = u.EmployeeId
                SET u.IsActive = e.IsActive
                WHERE u.Role = @Role";

            using var updateCommand = new MySqlCommand(updateStatusSql, connection, transaction);
            updateCommand.Parameters.AddWithValue("@Role", UserRoles.Employee);
            updateCommand.ExecuteNonQuery();
        }

        private static void EnsureDemoEmployeeAccounts(MySqlConnection connection, MySqlTransaction transaction)
        {
            var demoPassword = GetOptionalValue(DemoEmployeePasswordSetting, DemoEmployeePasswordEnvVar);
            if (string.IsNullOrWhiteSpace(demoPassword))
            {
                throw new InvalidOperationException(
                    $"Missing {DemoEmployeePasswordEnvVar}. Set a strong demo password to enable auto-generated employee accounts.");
            }

            ValidateStrongPassword(demoPassword, DemoEmployeePasswordEnvVar);

            const string selectMissingSql = @"
                SELECT e.EmployeeId, LOWER(TRIM(e.EmployeeCode)) AS EmployeeUsername, e.IsActive
                FROM Employees e
                LEFT JOIN UserAccounts u ON u.EmployeeId = e.EmployeeId
                WHERE u.UserAccountId IS NULL";

            using var selectCommand = new MySqlCommand(selectMissingSql, connection, transaction);
            using var reader = selectCommand.ExecuteReader();

            var pendingAccounts = new List<(int EmployeeId, string PreferredUsername, bool IsActive)>();
            while (reader.Read())
            {
                pendingAccounts.Add(
                    (
                        Convert.ToInt32(reader["EmployeeId"]),
                        Convert.ToString(reader["EmployeeUsername"]) ?? string.Empty,
                        Convert.ToBoolean(reader["IsActive"])
                    ));
            }

            reader.Close();

            foreach (var pendingAccount in pendingAccounts)
            {
                var username = GetAvailableUsername(connection, transaction, pendingAccount.PreferredUsername, pendingAccount.EmployeeId);
                var secret = CreatePasswordSecret(demoPassword);

                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO UserAccounts (Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive)
                    VALUES (@Username, @PasswordHash, @PasswordSalt, @HashIterations, @Role, @EmployeeId, @IsActive)", connection, transaction);

                insertCommand.Parameters.AddWithValue("@Username", username);
                insertCommand.Parameters.AddWithValue("@PasswordHash", secret.Hash);
                insertCommand.Parameters.AddWithValue("@PasswordSalt", secret.Salt);
                insertCommand.Parameters.AddWithValue("@HashIterations", secret.Iterations);
                insertCommand.Parameters.AddWithValue("@Role", UserRoles.Employee);
                insertCommand.Parameters.AddWithValue("@EmployeeId", pendingAccount.EmployeeId);
                insertCommand.Parameters.AddWithValue("@IsActive", pendingAccount.IsActive);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static string GetAvailableUsername(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string preferredUsername,
            int employeeId)
        {
            var baseUsername = string.IsNullOrWhiteSpace(preferredUsername)
                ? $"employee-{employeeId}"
                : preferredUsername;

            var candidate = baseUsername;

            var suffix = 0;
            while (UsernameExists(connection, transaction, candidate))
            {
                suffix++;
                candidate = $"{baseUsername}-{suffix}";
            }

            return candidate;
        }

        private static bool UsernameExists(MySqlConnection connection, MySqlTransaction transaction, string username)
        {
            const string sql = @"
                SELECT 1
                FROM UserAccounts
                WHERE Username = @Username
                LIMIT 1";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Username", username);
            return command.ExecuteScalar() != null;
        }

        private static PasswordSecret CreatePasswordSecret(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                PasswordHashIterations,
                HashAlgorithmName.SHA256,
                PasswordHashSize);

            return new PasswordSecret(Convert.ToHexString(hashBytes), salt, PasswordHashIterations);
        }

        private static bool VerifyPassword(string password, string storedHash, byte[]? salt, int iterations)
        {
            if (salt == null || salt.Length == 0 || string.IsNullOrWhiteSpace(storedHash))
            {
                return false;
            }

            byte[] expectedBytes;
            try
            {
                expectedBytes = Convert.FromHexString(storedHash);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedBytes.Length);

            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        private static bool IsExternalPbkdf2PasswordHash(string storedHash)
        {
            return storedHash.StartsWith("PBKDF2$", StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyExternalPbkdf2Password(string password, string storedHash)
        {
            if (!IsExternalPbkdf2PasswordHash(storedHash))
            {
                return false;
            }

            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expectedBytes;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expectedBytes = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedBytes.Length);

            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        private static string HashLegacyPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static void ValidateStrongPassword(string password, string sourceName)
        {
            if (password.Length < 6)
            {
                throw new InvalidOperationException(
                    $"{sourceName} must be at least 6 characters for testing.");
            }
        }

        private static string? GetOptionalValue(string appSettingKey, string envVar)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = ConfigurationManager.AppSettings[appSettingKey];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool GetOptionalBool(string appSettingKey, string envVar)
        {
            var value = GetOptionalValue(appSettingKey, envVar);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct PasswordSecret(string Hash, byte[] Salt, int Iterations);

        private static void UpgradeLegacyHashIfNeeded(int userAccountId, bool isLegacyHash, string password)
        {
            if (!isLegacyHash)
            {
                return;
            }

            var secret = CreatePasswordSecret(password);
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var command = new MySqlCommand(@"
                UPDATE UserAccounts
                SET PasswordHash = @PasswordHash,
                    PasswordSalt = @PasswordSalt,
                    HashIterations = @HashIterations
                WHERE UserAccountId = @UserAccountId", connection);

            command.Parameters.AddWithValue("@PasswordHash", secret.Hash);
            command.Parameters.AddWithValue("@PasswordSalt", secret.Salt);
            command.Parameters.AddWithValue("@HashIterations", secret.Iterations);
            command.Parameters.AddWithValue("@UserAccountId", userAccountId);
            command.ExecuteNonQuery();
        }

        private static void UpgradeLegacyHashIfNeededViaApi(int userAccountId, bool isLegacyHash, string password)
        {
            if (!isLegacyHash)
            {
                return;
            }

            var secret = CreatePasswordSecret(password);
            SupabaseRestClient.Update(
                "useraccounts",
                new
                {
                    passwordhash = secret.Hash,
                    passwordsalt = ToByteaLiteral(secret.Salt),
                    hashiterations = secret.Iterations
                },
                new Dictionary<string, string>
                {
                    ["useraccountid"] = $"eq.{userAccountId}"
                });
        }

        private static InvalidOperationException BuildApiModeException(string action, Exception ex)
        {
            return new InvalidOperationException(
                $"{action}. The app is running in publishable-key API mode and needs anon RLS access for the required REST operations. " +
                $"For development, run {DevPolicyScriptPath} after the schema SQL, or create equivalent select/insert/update/delete policies. {ex.Message}",
                ex);
        }

        private sealed class ApiEmployeeRecord
        {
            public int EmployeeId { get; set; }
        }

        private sealed class ApiUserAccount
        {
            public int UserAccountId { get; set; }
            public string Username { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
            public string? PasswordSalt { get; set; }
            public int HashIterations { get; set; }
            public string Role { get; set; } = string.Empty;
            public int? EmployeeId { get; set; }
            public bool IsActive { get; set; }
        }

        private sealed class ApiEmployeeSyncRow
        {
            public int EmployeeId { get; set; }
            public string EmployeeCode { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }
    }
}
