using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class EmployeeRepository
    {
        public List<Employee> GetAllEmployees()
        {
            if (SupabaseConfig.UseApi)
            {
                return GetAllEmployeesViaApi();
            }

            var employees = new List<Employee>();
            var sourceFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? "WHERE SourceTeacherId IS NOT NULL"
                : string.Empty;
            var sql = $@"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate
                FROM Employees
                {sourceFilter}
                ORDER BY FullName";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                employees.Add(MapEmployee(reader));
            }

            return employees;
        }

        public Employee? GetEmployeeById(int employeeId)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetEmployeeByIdViaApi(employeeId);
            }

            const string sql = @"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate
                FROM Employees
                WHERE EmployeeId = @EmployeeId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            connection.Open();
            using var reader = command.ExecuteReader();

            return reader.Read() ? MapEmployee(reader) : null;
        }

        public int AddEmployee(Employee employee)
        {
            EmployeeSourcePolicy.EnsureLocalEmployeeManagementAllowed("Adding employees");

            if (SupabaseConfig.UseApi)
            {
                return AddEmployeeViaApi(employee);
            }

            const string sql = @"
                INSERT INTO Employees
                (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate)
                VALUES
                (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @SourceTeacherId, @SourceUserId, @ProfileImage, @BiometricTemplate);";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            AddEmployeeParameters(command, employee);
            connection.Open();
            command.ExecuteNonQuery();
            return Convert.ToInt32(command.LastInsertedId);
        }

        public void UpdateEmployee(Employee employee)
        {
            EmployeeSourcePolicy.EnsureLocalEmployeeManagementAllowed("Updating employees");

            if (SupabaseConfig.UseApi)
            {
                UpdateEmployeeViaApi(employee);
                return;
            }

            const string sql = @"
                UPDATE Employees
                SET EmployeeCode = @EmployeeCode,
                    FullName = @FullName,
                    Email = @Email,
                    Phone = @Phone,
                    Position = @Position,
                    Department = @Department,
                    HourlyRate = @HourlyRate,
                    HireDate = @HireDate,
                    IsActive = @IsActive,
                    SourceTeacherId = @SourceTeacherId,
                    SourceUserId = @SourceUserId,
                    ProfileImage = @ProfileImage,
                    BiometricTemplate = @BiometricTemplate
                WHERE EmployeeId = @EmployeeId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            AddEmployeeParameters(command, employee);
            command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void UpdateProfileImage(int employeeId, byte[]? profileImage)
        {
            if (SupabaseConfig.UseApi)
            {
                UpdateProfileImageViaApi(employeeId, profileImage);
                return;
            }

            const string sql = @"
                UPDATE Employees
                SET ProfileImage = @ProfileImage
                WHERE EmployeeId = @EmployeeId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@ProfileImage", profileImage is null ? DBNull.Value : profileImage);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void DeleteEmployee(int employeeId)
        {
            EmployeeSourcePolicy.EnsureLocalEmployeeManagementAllowed("Deleting employees");

            if (SupabaseConfig.UseApi)
            {
                DeleteEmployeeViaApi(employeeId);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var deletePayroll = new MySqlCommand("DELETE FROM PayrollRecords WHERE EmployeeId = @EmployeeId", connection, transaction);
                deletePayroll.Parameters.AddWithValue("@EmployeeId", employeeId);
                deletePayroll.ExecuteNonQuery();

                using var deleteAttendance = new MySqlCommand("DELETE FROM AttendanceRecords WHERE EmployeeId = @EmployeeId", connection, transaction);
                deleteAttendance.Parameters.AddWithValue("@EmployeeId", employeeId);
                deleteAttendance.ExecuteNonQuery();

                using var deleteEmployee = new MySqlCommand("DELETE FROM Employees WHERE EmployeeId = @EmployeeId", connection, transaction);
                deleteEmployee.Parameters.AddWithValue("@EmployeeId", employeeId);
                deleteEmployee.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static List<Employee> GetAllEmployeesViaApi()
        {
            return SupabaseRestClient.GetList<ApiEmployeeRecord>(
                "employees",
                new Dictionary<string, string>
                {
                    ["select"] = "employeeid,employeecode,fullname,email,phone,position,department,hourlyrate,hiredate,isactive,profileimage",
                    ["order"] = "fullname.asc"
                }).ConvertAll(MapApiEmployee);
        }

        private static Employee? GetEmployeeByIdViaApi(int employeeId)
        {
            var employee = SupabaseRestClient.GetSingleOrDefault<ApiEmployeeRecord>(
                "employees",
                new Dictionary<string, string>
                {
                    ["select"] = "employeeid,employeecode,fullname,email,phone,position,department,hourlyrate,hiredate,isactive,profileimage",
                    ["employeeid"] = $"eq.{employeeId}",
                    ["limit"] = "1"
                });

            return employee == null ? null : MapApiEmployee(employee);
        }

        private static int AddEmployeeViaApi(Employee employee)
        {
            var created = SupabaseRestClient.InsertAndReturnSingle<ApiEmployeeRecord>("employees", BuildEmployeePayload(employee));
            return created.EmployeeId;
        }

        private static void UpdateEmployeeViaApi(Employee employee)
        {
            SupabaseRestClient.Update(
                "employees",
                BuildEmployeePayload(employee),
                new Dictionary<string, string>
                {
                    ["employeeid"] = $"eq.{employee.EmployeeId}"
                });
        }

        private static void UpdateProfileImageViaApi(int employeeId, byte[]? profileImage)
        {
            SupabaseRestClient.Update(
                "employees",
                new
                {
                    profileimage = ToApiByteaValue(profileImage)
                },
                new Dictionary<string, string>
                {
                    ["employeeid"] = $"eq.{employeeId}"
                });
        }

        private static void DeleteEmployeeViaApi(int employeeId)
        {
            var filter = new Dictionary<string, string>
            {
                ["employeeid"] = $"eq.{employeeId}"
            };

            SupabaseRestClient.Delete("payrollrecords", filter);
            SupabaseRestClient.Delete("attendancerecords", filter);
            SupabaseRestClient.Delete("useraccounts", filter);
            SupabaseRestClient.Delete("employees", filter);
        }

        private static object BuildEmployeePayload(Employee employee)
        {
            return new
            {
                employeecode = employee.EmployeeCode.Trim(),
                fullname = employee.FullName.Trim(),
                email = ToApiValue(employee.Email),
                phone = ToApiValue(employee.Phone),
                position = ToApiValue(employee.Position),
                department = ToApiValue(employee.Department),
                hourlyrate = employee.HourlyRate,
                hiredate = employee.HireDate.Date,
                isactive = employee.IsActive,
                profileimage = ToApiByteaValue(employee.ProfileImage)
            };
        }

        private static void AddEmployeeParameters(MySqlCommand command, Employee employee)
        {
            command.Parameters.AddWithValue("@EmployeeCode", employee.EmployeeCode.Trim());
            command.Parameters.AddWithValue("@FullName", employee.FullName.Trim());
            command.Parameters.AddWithValue("@Email", ToDbValue(employee.Email));
            command.Parameters.AddWithValue("@Phone", ToDbValue(employee.Phone));
            command.Parameters.AddWithValue("@Position", ToDbValue(employee.Position));
            command.Parameters.AddWithValue("@Department", ToDbValue(employee.Department));
            command.Parameters.AddWithValue("@HourlyRate", employee.HourlyRate);
            command.Parameters.AddWithValue("@HireDate", employee.HireDate.Date);
            command.Parameters.AddWithValue("@IsActive", employee.IsActive);
            command.Parameters.AddWithValue("@SourceTeacherId", employee.SourceTeacherId.HasValue ? employee.SourceTeacherId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@SourceUserId", employee.SourceUserId.HasValue ? employee.SourceUserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@ProfileImage", employee.ProfileImage is null ? DBNull.Value : employee.ProfileImage);
            command.Parameters.AddWithValue("@BiometricTemplate", employee.BiometricTemplate is null ? DBNull.Value : employee.BiometricTemplate);
        }

        private static object ToDbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private static string? ToApiValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? ToApiByteaValue(byte[]? value)
        {
            return value == null || value.Length == 0
                ? null
                : $@"\x{Convert.ToHexString(value).ToLowerInvariant()}";
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

        private static Employee MapApiEmployee(ApiEmployeeRecord record)
        {
            return new Employee
            {
                EmployeeId = record.EmployeeId,
                EmployeeCode = record.EmployeeCode,
                FullName = record.FullName,
                Email = record.Email ?? string.Empty,
                Phone = record.Phone ?? string.Empty,
                Position = record.Position ?? string.Empty,
                Department = record.Department ?? string.Empty,
                HourlyRate = record.HourlyRate,
                HireDate = record.HireDate,
                IsActive = record.IsActive,
                ProfileImage = ParseBytea(record.ProfileImage)
            };
        }

        private static Employee MapEmployee(MySqlDataReader reader)
        {
            return new Employee
            {
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                Email = Convert.ToString(reader["Email"]) ?? string.Empty,
                Phone = Convert.ToString(reader["Phone"]) ?? string.Empty,
                Position = Convert.ToString(reader["Position"]) ?? string.Empty,
                Department = Convert.ToString(reader["Department"]) ?? string.Empty,
                HourlyRate = Convert.ToDecimal(reader["HourlyRate"]),
                HireDate = Convert.ToDateTime(reader["HireDate"]),
                IsActive = Convert.ToBoolean(reader["IsActive"]),
                SourceTeacherId = reader["SourceTeacherId"] is DBNull ? null : Convert.ToInt64(reader["SourceTeacherId"]),
                SourceUserId = reader["SourceUserId"] is DBNull ? null : Convert.ToInt64(reader["SourceUserId"]),
                ProfileImage = reader["ProfileImage"] is DBNull ? null : (byte[])reader["ProfileImage"],
                BiometricTemplate = reader["BiometricTemplate"] is DBNull ? null : (byte[])reader["BiometricTemplate"]
            };
        }

        private sealed class ApiEmployeeRecord
        {
            public int EmployeeId { get; set; }
            public string EmployeeCode { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? Position { get; set; }
            public string? Department { get; set; }
            public decimal HourlyRate { get; set; }
            public DateTime HireDate { get; set; }
            public bool IsActive { get; set; }
            public string? ProfileImage { get; set; }
        }
    }
}
