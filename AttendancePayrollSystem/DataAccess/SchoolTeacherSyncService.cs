using System;
using System.Collections.Generic;
using System.Linq;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class SchoolTeacherSyncService
    {
        private readonly AuthRepository _authRepository = new();
        private readonly SchoolTeacherRepository _schoolTeacherRepository = new();

        public SchoolTeacherSyncResult SyncTeachers()
        {
            if (SupabaseConfig.UseApi)
            {
                return SchoolTeacherSyncResult.Skipped("School teacher sync is not supported while the app is running in Supabase API mode.");
            }

            if (!SchoolDatabaseHelper.IsConfigured())
            {
                return SchoolTeacherSyncResult.Skipped("School teacher sync skipped because the school DB connection is not configured.");
            }

            _authRepository.EnsureLocalAuthSchema();
            var schoolTeachers = _schoolTeacherRepository.GetTeachers();

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);

                var employees = LoadLegacyEmployees(connection, transaction);
                var employeesByTeacherId = employees
                    .Where(employee => employee.SourceTeacherId.HasValue)
                    .ToDictionary(employee => employee.SourceTeacherId!.Value);
                var employeesByUserId = employees
                    .Where(employee => employee.SourceUserId.HasValue)
                    .ToDictionary(employee => employee.SourceUserId!.Value);
                var employeesByCode = employees
                    .Where(employee => !string.IsNullOrWhiteSpace(employee.EmployeeCode))
                    .ToDictionary(employee => employee.EmployeeCode, StringComparer.OrdinalIgnoreCase);

                var accounts = LoadEmployeeAccounts(connection, transaction);
                var accountsByEmployeeId = accounts
                    .Where(account => account.EmployeeId.HasValue)
                    .ToDictionary(account => account.EmployeeId!.Value);
                var accountsByUsername = accounts
                    .Where(account => !string.IsNullOrWhiteSpace(account.Username))
                    .ToDictionary(account => account.Username, StringComparer.OrdinalIgnoreCase);

                var result = new SchoolTeacherSyncResult
                {
                    TeachersRead = schoolTeachers.Count
                };

                foreach (var teacher in schoolTeachers)
                {
                    var employee = ResolveEmployee(teacher, employeesByTeacherId, employeesByUserId, employeesByCode);
                    var previousEmployeeCode = employee?.EmployeeCode;
                    var desiredEmployeeCode = BuildEmployeeCode(teacher);
                    EnsureEmployeeCodeAvailable(desiredEmployeeCode, employee?.EmployeeId, employeesByCode);

                    if (employee == null)
                    {
                        employee = new LegacyEmployeeSyncRow
                        {
                            EmployeeId = InsertEmployee(connection, transaction, teacher, desiredEmployeeCode)
                        };
                        result.EmployeesInserted++;
                    }
                    else
                    {
                        UpdateEmployee(connection, transaction, employee, teacher, desiredEmployeeCode);
                        result.EmployeesUpdated++;
                    }

                    employee.EmployeeCode = desiredEmployeeCode;
                    employee.FullName = ComposeFullName(teacher);
                    employee.Email = teacher.Email.Trim();
                    employee.Phone = teacher.ContactNo.Trim();
                    employee.HireDate = teacher.HireDate?.Date ?? employee.HireDate.Date;
                    employee.IsActive = IsTeacherActive(teacher);
                    employee.SourceTeacherId = teacher.TeacherId;
                    employee.SourceUserId = teacher.UserId;

                    if (!string.IsNullOrWhiteSpace(previousEmployeeCode) &&
                        !string.Equals(previousEmployeeCode, employee.EmployeeCode, StringComparison.OrdinalIgnoreCase))
                    {
                        employeesByCode.Remove(previousEmployeeCode);
                    }

                    employeesByTeacherId[teacher.TeacherId] = employee;
                    employeesByUserId[teacher.UserId] = employee;
                    employeesByCode[employee.EmployeeCode] = employee;

                    if (TrySyncUserAccount(connection, transaction, teacher, employee, accountsByEmployeeId, accountsByUsername, out var accountInserted))
                    {
                        if (accountInserted)
                        {
                            result.AccountsInserted++;
                        }
                        else
                        {
                            result.AccountsUpdated++;
                        }
                    }
                }

                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static LegacyEmployeeSyncRow? ResolveEmployee(
            SchoolTeacherRecord teacher,
            IReadOnlyDictionary<long, LegacyEmployeeSyncRow> employeesByTeacherId,
            IReadOnlyDictionary<long, LegacyEmployeeSyncRow> employeesByUserId,
            IReadOnlyDictionary<string, LegacyEmployeeSyncRow> employeesByCode)
        {
            if (employeesByTeacherId.TryGetValue(teacher.TeacherId, out var employeeByTeacher))
            {
                return employeeByTeacher;
            }

            if (employeesByUserId.TryGetValue(teacher.UserId, out var employeeByUser))
            {
                return employeeByUser;
            }

            var employeeNo = teacher.EmployeeNo.Trim();
            if (!string.IsNullOrWhiteSpace(employeeNo) &&
                employeesByCode.TryGetValue(employeeNo, out var employeeByCode) &&
                !employeeByCode.SourceTeacherId.HasValue &&
                !employeeByCode.SourceUserId.HasValue)
            {
                return employeeByCode;
            }

            return null;
        }

        private static void EnsureEmployeeCodeAvailable(
            string employeeCode,
            int? currentEmployeeId,
            IReadOnlyDictionary<string, LegacyEmployeeSyncRow> employeesByCode)
        {
            if (!employeesByCode.TryGetValue(employeeCode, out var existing))
            {
                return;
            }

            if (!currentEmployeeId.HasValue || existing.EmployeeId != currentEmployeeId.Value)
            {
                throw new InvalidOperationException(
                    $"School teacher sync could not assign employee code '{employeeCode}' because it is already used by a different legacy employee.");
            }
        }

        private static int InsertEmployee(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SchoolTeacherRecord teacher,
            string employeeCode)
        {
            using var command = new MySqlCommand(@"
                INSERT INTO Employees
                (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate)
                VALUES
                (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @SourceTeacherId, @SourceUserId, NULL, NULL)", connection, transaction);

            command.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            command.Parameters.AddWithValue("@FullName", ComposeFullName(teacher));
            command.Parameters.AddWithValue("@Email", ToDbValue(teacher.Email));
            command.Parameters.AddWithValue("@Phone", ToDbValue(teacher.ContactNo));
            command.Parameters.AddWithValue("@Position", "Teacher");
            command.Parameters.AddWithValue("@Department", "Faculty");
            command.Parameters.AddWithValue("@HourlyRate", 0m);
            command.Parameters.AddWithValue("@HireDate", teacher.HireDate?.Date ?? DateTime.Today);
            command.Parameters.AddWithValue("@IsActive", IsTeacherActive(teacher));
            command.Parameters.AddWithValue("@SourceTeacherId", teacher.TeacherId);
            command.Parameters.AddWithValue("@SourceUserId", teacher.UserId);
            command.ExecuteNonQuery();

            return Convert.ToInt32(command.LastInsertedId);
        }

        private static void UpdateEmployee(
            MySqlConnection connection,
            MySqlTransaction transaction,
            LegacyEmployeeSyncRow employee,
            SchoolTeacherRecord teacher,
            string employeeCode)
        {
            using var command = new MySqlCommand(@"
                UPDATE Employees
                SET EmployeeCode = @EmployeeCode,
                    FullName = @FullName,
                    Email = @Email,
                    Phone = @Phone,
                    Position = @Position,
                    Department = @Department,
                    HireDate = @HireDate,
                    IsActive = @IsActive,
                    SourceTeacherId = @SourceTeacherId,
                    SourceUserId = @SourceUserId
                WHERE EmployeeId = @EmployeeId", connection, transaction);

            command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
            command.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            command.Parameters.AddWithValue("@FullName", ComposeFullName(teacher));
            command.Parameters.AddWithValue("@Email", ToDbValue(teacher.Email));
            command.Parameters.AddWithValue("@Phone", ToDbValue(teacher.ContactNo));
            command.Parameters.AddWithValue("@Position", string.IsNullOrWhiteSpace(employee.Position) ? "Teacher" : employee.Position);
            command.Parameters.AddWithValue("@Department", string.IsNullOrWhiteSpace(employee.Department) ? "Faculty" : employee.Department);
            command.Parameters.AddWithValue("@HireDate", teacher.HireDate?.Date ?? employee.HireDate.Date);
            command.Parameters.AddWithValue("@IsActive", IsTeacherActive(teacher));
            command.Parameters.AddWithValue("@SourceTeacherId", teacher.TeacherId);
            command.Parameters.AddWithValue("@SourceUserId", teacher.UserId);
            command.ExecuteNonQuery();
        }

        private static bool TrySyncUserAccount(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SchoolTeacherRecord teacher,
            LegacyEmployeeSyncRow employee,
            IDictionary<int, LegacyUserAccountSyncRow> accountsByEmployeeId,
            IDictionary<string, LegacyUserAccountSyncRow> accountsByUsername,
            out bool accountInserted)
        {
            accountInserted = false;

            var username = teacher.Username.Trim().ToLowerInvariant();
            var passwordHash = teacher.PasswordHash.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(passwordHash))
            {
                return false;
            }

            var desiredIsActive = employee.IsActive && IsUserActive(teacher);
            accountsByEmployeeId.TryGetValue(employee.EmployeeId, out var existingAccount);

            if (accountsByUsername.TryGetValue(username, out var conflictingAccount) &&
                (existingAccount == null || conflictingAccount.UserAccountId != existingAccount.UserAccountId))
            {
                throw new InvalidOperationException(
                    $"School teacher sync could not assign username '{username}' because it is already used by another local account.");
            }

            if (existingAccount == null)
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO UserAccounts (Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive)
                    VALUES (@Username, @PasswordHash, NULL, 0, @Role, @EmployeeId, @IsActive)", connection, transaction);

                insertCommand.Parameters.AddWithValue("@Username", username);
                insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
                insertCommand.Parameters.AddWithValue("@Role", UserRoles.Employee);
                insertCommand.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
                insertCommand.Parameters.AddWithValue("@IsActive", desiredIsActive);
                insertCommand.ExecuteNonQuery();

                var createdAccount = new LegacyUserAccountSyncRow
                {
                    UserAccountId = Convert.ToInt32(insertCommand.LastInsertedId),
                    EmployeeId = employee.EmployeeId,
                    Username = username,
                    PasswordHash = passwordHash,
                    IsActive = desiredIsActive
                };

                accountsByEmployeeId[employee.EmployeeId] = createdAccount;
                accountsByUsername[username] = createdAccount;
                accountInserted = true;
                return true;
            }

            var usernameChanged = !string.Equals(existingAccount.Username, username, StringComparison.OrdinalIgnoreCase);
            var passwordChanged = !string.Equals(existingAccount.PasswordHash, passwordHash, StringComparison.Ordinal);
            var statusChanged = existingAccount.IsActive != desiredIsActive;

            if (!usernameChanged && !passwordChanged && !statusChanged)
            {
                return false;
            }

            using var updateCommand = new MySqlCommand(@"
                UPDATE UserAccounts
                SET Username = @Username,
                    PasswordHash = @PasswordHash,
                    PasswordSalt = NULL,
                    HashIterations = 0,
                    Role = @Role,
                    IsActive = @IsActive
                WHERE UserAccountId = @UserAccountId", connection, transaction);

            updateCommand.Parameters.AddWithValue("@UserAccountId", existingAccount.UserAccountId);
            updateCommand.Parameters.AddWithValue("@Username", username);
            updateCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
            updateCommand.Parameters.AddWithValue("@Role", UserRoles.Employee);
            updateCommand.Parameters.AddWithValue("@IsActive", desiredIsActive);
            updateCommand.ExecuteNonQuery();

            if (usernameChanged && !string.IsNullOrWhiteSpace(existingAccount.Username))
            {
                accountsByUsername.Remove(existingAccount.Username);
            }

            existingAccount.Username = username;
            existingAccount.PasswordHash = passwordHash;
            existingAccount.IsActive = desiredIsActive;
            accountsByUsername[username] = existingAccount;
            return true;
        }

        private static List<LegacyEmployeeSyncRow> LoadLegacyEmployees(MySqlConnection connection, MySqlTransaction transaction)
        {
            using var command = new MySqlCommand(@"
                SELECT EmployeeId, EmployeeCode, Position, Department, HireDate, IsActive, SourceTeacherId, SourceUserId
                FROM Employees
                ORDER BY EmployeeId", connection, transaction);

            using var reader = command.ExecuteReader();
            var employees = new List<LegacyEmployeeSyncRow>();

            while (reader.Read())
            {
                employees.Add(new LegacyEmployeeSyncRow
                {
                    EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    Position = Convert.ToString(reader["Position"]) ?? string.Empty,
                    Department = Convert.ToString(reader["Department"]) ?? string.Empty,
                    HireDate = Convert.ToDateTime(reader["HireDate"]),
                    IsActive = Convert.ToBoolean(reader["IsActive"]),
                    SourceTeacherId = reader["SourceTeacherId"] is DBNull ? null : Convert.ToInt64(reader["SourceTeacherId"]),
                    SourceUserId = reader["SourceUserId"] is DBNull ? null : Convert.ToInt64(reader["SourceUserId"])
                });
            }

            return employees;
        }

        private static List<LegacyUserAccountSyncRow> LoadEmployeeAccounts(MySqlConnection connection, MySqlTransaction transaction)
        {
            using var command = new MySqlCommand(@"
                SELECT UserAccountId, Username, PasswordHash, EmployeeId, IsActive
                FROM UserAccounts
                WHERE EmployeeId IS NOT NULL
                ORDER BY UserAccountId", connection, transaction);

            using var reader = command.ExecuteReader();
            var accounts = new List<LegacyUserAccountSyncRow>();

            while (reader.Read())
            {
                accounts.Add(new LegacyUserAccountSyncRow
                {
                    UserAccountId = Convert.ToInt32(reader["UserAccountId"]),
                    Username = Convert.ToString(reader["Username"]) ?? string.Empty,
                    PasswordHash = Convert.ToString(reader["PasswordHash"]) ?? string.Empty,
                    EmployeeId = reader["EmployeeId"] is DBNull ? null : Convert.ToInt32(reader["EmployeeId"]),
                    IsActive = Convert.ToBoolean(reader["IsActive"])
                });
            }

            return accounts;
        }

        private static bool IsTeacherActive(SchoolTeacherRecord teacher)
        {
            return string.Equals(teacher.TeacherStatus.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUserActive(SchoolTeacherRecord teacher)
        {
            return string.Equals(teacher.UserStatus.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildEmployeeCode(SchoolTeacherRecord teacher)
        {
            var employeeCode = teacher.EmployeeNo.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                employeeCode = $"TCH-{teacher.TeacherId}";
            }

            if (employeeCode.Length > 20)
            {
                throw new InvalidOperationException(
                    $"School teacher sync could not use employee code '{employeeCode}' because it exceeds the 20-character legacy limit.");
            }

            return employeeCode;
        }

        private static string ComposeFullName(SchoolTeacherRecord teacher)
        {
            var parts = new[]
            {
                teacher.FirstName.Trim(),
                teacher.MiddleName.Trim(),
                teacher.LastName.Trim()
            };

            return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static object ToDbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private sealed class LegacyEmployeeSyncRow
        {
            public int EmployeeId { get; set; }
            public string EmployeeCode { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Position { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public DateTime HireDate { get; set; }
            public bool IsActive { get; set; }
            public long? SourceTeacherId { get; set; }
            public long? SourceUserId { get; set; }
        }

        private sealed class LegacyUserAccountSyncRow
        {
            public int UserAccountId { get; set; }
            public int? EmployeeId { get; set; }
            public string Username { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }
    }

    public sealed class SchoolTeacherSyncResult
    {
        public int TeachersRead { get; init; }
        public int EmployeesInserted { get; set; }
        public int EmployeesUpdated { get; set; }
        public int AccountsInserted { get; set; }
        public int AccountsUpdated { get; set; }
        public bool WasSkipped { get; init; }
        public string Message { get; init; } = string.Empty;

        public static SchoolTeacherSyncResult Skipped(string message)
        {
            return new SchoolTeacherSyncResult
            {
                WasSkipped = true,
                Message = message
            };
        }

        public string ToSummary()
        {
            return WasSkipped
                ? Message
                : $"School sync: {TeachersRead} teachers read, {EmployeesInserted} employees added, {EmployeesUpdated} employees updated, {AccountsInserted} accounts added, {AccountsUpdated} accounts updated.";
        }
    }
}
