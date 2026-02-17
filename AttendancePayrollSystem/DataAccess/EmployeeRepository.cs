using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.DataAccess
{
    public class EmployeeRepository
    {
        public List<Employee> GetAllEmployees()
        {
            var employees = new List<Employee>();
            const string sql = @"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, ProfileImage, BiometricTemplate
                FROM Employees
                ORDER BY FullName";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
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
            const string sql = @"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, ProfileImage, BiometricTemplate
                FROM Employees
                WHERE EmployeeId = @EmployeeId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            connection.Open();
            using var reader = command.ExecuteReader();

            return reader.Read() ? MapEmployee(reader) : null;
        }

        public int AddEmployee(Employee employee)
        {
            const string sql = @"
                INSERT INTO Employees
                (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, ProfileImage, BiometricTemplate)
                VALUES
                (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @ProfileImage, @BiometricTemplate);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            AddEmployeeParameters(command, employee);
            connection.Open();
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void UpdateEmployee(Employee employee)
        {
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
                    IsActive = @IsActive
                WHERE EmployeeId = @EmployeeId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            AddEmployeeParameters(command, employee);
            command.Parameters.Add("@EmployeeId", SqlDbType.Int).Value = employee.EmployeeId;
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void DeleteEmployee(int employeeId)
        {
            const string sql = @"
                DELETE FROM PayrollRecords WHERE EmployeeId = @EmployeeId;
                DELETE FROM AttendanceRecords WHERE EmployeeId = @EmployeeId;
                DELETE FROM Employees WHERE EmployeeId = @EmployeeId;";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@EmployeeId", SqlDbType.Int).Value = employeeId;
            connection.Open();
            command.ExecuteNonQuery();
        }

        private static void AddEmployeeParameters(SqlCommand command, Employee employee)
        {
            command.Parameters.Add("@EmployeeCode", SqlDbType.NVarChar, 20).Value = employee.EmployeeCode.Trim();
            command.Parameters.Add("@FullName", SqlDbType.NVarChar, 150).Value = employee.FullName.Trim();
            command.Parameters.Add("@Email", SqlDbType.NVarChar, 150).Value = ToDbValue(employee.Email);
            command.Parameters.Add("@Phone", SqlDbType.NVarChar, 50).Value = ToDbValue(employee.Phone);
            command.Parameters.Add("@Position", SqlDbType.NVarChar, 100).Value = ToDbValue(employee.Position);
            command.Parameters.Add("@Department", SqlDbType.NVarChar, 100).Value = ToDbValue(employee.Department);
            command.Parameters.Add("@HourlyRate", SqlDbType.Decimal).Value = employee.HourlyRate;
            command.Parameters["@HourlyRate"].Precision = 18;
            command.Parameters["@HourlyRate"].Scale = 2;
            command.Parameters.Add("@HireDate", SqlDbType.Date).Value = employee.HireDate.Date;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = employee.IsActive;
            command.Parameters.Add("@ProfileImage", SqlDbType.VarBinary).Value = employee.ProfileImage is null ? DBNull.Value : employee.ProfileImage;
            command.Parameters.Add("@BiometricTemplate", SqlDbType.VarBinary).Value = employee.BiometricTemplate is null ? DBNull.Value : employee.BiometricTemplate;
        }

        private static object ToDbValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
        }

        private static Employee MapEmployee(SqlDataReader reader)
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
                ProfileImage = reader["ProfileImage"] is DBNull ? null : (byte[])reader["ProfileImage"],
                BiometricTemplate = reader["BiometricTemplate"] is DBNull ? null : (byte[])reader["BiometricTemplate"]
            };
        }
    }
}
