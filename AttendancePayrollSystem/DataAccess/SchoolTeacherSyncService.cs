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

            if (DatabaseRuntimeState.UseOfflineDatabase)
            {
                return SchoolTeacherSyncResult.Skipped("School teacher sync skipped while the app is using the local offline database.");
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
                var matchedEmployeeIds = new HashSet<int>();

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
                    employee.IsActive = IsTeacherAvailableForPayroll(teacher);
                    employee.SourceTeacherId = teacher.TeacherId;
                    employee.SourceUserId = teacher.UserId;
                    matchedEmployeeIds.Add(employee.EmployeeId);

                    if (!string.IsNullOrWhiteSpace(previousEmployeeCode) &&
                        !string.Equals(previousEmployeeCode, employee.EmployeeCode, StringComparison.OrdinalIgnoreCase))
                    {
                        employeesByCode.Remove(previousEmployeeCode);
                    }

                    employeesByTeacherId[teacher.TeacherId] = employee;
                    if (teacher.UserId.HasValue)
                    {
                        employeesByUserId[teacher.UserId.Value] = employee;
                    }
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

                ReconcileMissingSchoolEmployees(
                    connection,
                    transaction,
                    employees,
                    accountsByEmployeeId,
                    matchedEmployeeIds,
                    result);

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

            if (teacher.UserId.HasValue &&
                employeesByUserId.TryGetValue(teacher.UserId.Value, out var employeeByUser))
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
            command.Parameters.AddWithValue("@IsActive", IsTeacherAvailableForPayroll(teacher));
            command.Parameters.AddWithValue("@SourceTeacherId", teacher.TeacherId);
            command.Parameters.AddWithValue("@SourceUserId", teacher.UserId.HasValue ? teacher.UserId.Value : DBNull.Value);
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
            command.Parameters.AddWithValue("@IsActive", IsTeacherAvailableForPayroll(teacher));
            command.Parameters.AddWithValue("@SourceTeacherId", teacher.TeacherId);
            command.Parameters.AddWithValue("@SourceUserId", teacher.UserId.HasValue ? teacher.UserId.Value : DBNull.Value);
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
            var desiredIsActive = employee.IsActive;
            if (!accountsByEmployeeId.TryGetValue(employee.EmployeeId, out var existingAccount) ||
                existingAccount.IsActive == desiredIsActive)
            {
                return false;
            }

            using var updateCommand = new MySqlCommand(@"
                UPDATE UserAccounts
                SET Role = @Role,
                    IsActive = @IsActive
                WHERE UserAccountId = @UserAccountId", connection, transaction);

            updateCommand.Parameters.AddWithValue("@UserAccountId", existingAccount.UserAccountId);
            updateCommand.Parameters.AddWithValue("@Role", UserRoles.Employee);
            updateCommand.Parameters.AddWithValue("@IsActive", desiredIsActive);
            updateCommand.ExecuteNonQuery();

            existingAccount.IsActive = desiredIsActive;
            return true;
        }

        private static bool DeactivateExistingAccount(
            MySqlConnection connection,
            MySqlTransaction transaction,
            IDictionary<int, LegacyUserAccountSyncRow> accountsByEmployeeId,
            int employeeId)
        {
            if (!accountsByEmployeeId.TryGetValue(employeeId, out var existingAccount) || !existingAccount.IsActive)
            {
                return false;
            }

            using var command = new MySqlCommand(@"
                UPDATE UserAccounts
                SET IsActive = FALSE
                WHERE UserAccountId = @UserAccountId", connection, transaction);
            command.Parameters.AddWithValue("@UserAccountId", existingAccount.UserAccountId);
            command.ExecuteNonQuery();
            existingAccount.IsActive = false;
            return true;
        }

        private static void ReconcileMissingSchoolEmployees(
            MySqlConnection connection,
            MySqlTransaction transaction,
            IEnumerable<LegacyEmployeeSyncRow> employees,
            IDictionary<int, LegacyUserAccountSyncRow> accountsByEmployeeId,
            ISet<int> matchedEmployeeIds,
            SchoolTeacherSyncResult result)
        {
            foreach (var employee in employees.Where(employee =>
                         employee.SourceTeacherId.HasValue &&
                         !matchedEmployeeIds.Contains(employee.EmployeeId)))
            {
                if (employee.IsActive)
                {
                    using var deactivateEmployee = new MySqlCommand(@"
                        UPDATE Employees
                        SET IsActive = FALSE
                        WHERE EmployeeId = @EmployeeId", connection, transaction);
                    deactivateEmployee.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
                    deactivateEmployee.ExecuteNonQuery();
                    employee.IsActive = false;
                    result.EmployeesInactivated++;
                }

                if (accountsByEmployeeId.TryGetValue(employee.EmployeeId, out var account) && account.IsActive)
                {
                    using var deactivateAccount = new MySqlCommand(@"
                        UPDATE UserAccounts
                        SET IsActive = FALSE
                        WHERE UserAccountId = @UserAccountId", connection, transaction);
                    deactivateAccount.Parameters.AddWithValue("@UserAccountId", account.UserAccountId);
                    deactivateAccount.ExecuteNonQuery();
                    account.IsActive = false;
                    result.AccountsInactivated++;
                }
            }
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

        private static bool IsTeacherAvailableForPayroll(SchoolTeacherRecord teacher)
        {
            return IsTeacherActive(teacher) && (!teacher.UserId.HasValue || IsUserActive(teacher));
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
        public int EmployeesInactivated { get; set; }
        public int AccountsInserted { get; set; }
        public int AccountsUpdated { get; set; }
        public int AccountsInactivated { get; set; }
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
                : $"School sync: {TeachersRead} teachers read, {EmployeesInserted} employees added, {EmployeesUpdated} employees updated, {EmployeesInactivated} employees inactivated, {AccountsInserted} accounts added, {AccountsUpdated} accounts updated, {AccountsInactivated} accounts inactivated.";
        }
    }
}
