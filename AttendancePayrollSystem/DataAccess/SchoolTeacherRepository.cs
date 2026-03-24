using System;
using System.Collections.Generic;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class SchoolTeacherRepository
    {
        public IReadOnlyList<SchoolTeacherRecord> GetTeachers()
        {
            if (!SchoolDatabaseHelper.IsConfigured())
            {
                return [];
            }

            using var connection = SchoolDatabaseHelper.GetConnection();
            using var command = new MySqlCommand(@"
                SELECT
                    t.Id AS TeacherId,
                    t.user_id AS UserId,
                    t.employee_no AS EmployeeNo,
                    t.first_name AS FirstName,
                    t.last_name AS LastName,
                    t.middle_name AS MiddleName,
                    t.email AS Email,
                    t.contact_no AS ContactNo,
                    t.hire_date AS HireDate,
                    t.status AS TeacherStatus,
                    u.username AS Username,
                    u.password_hash AS PasswordHash,
                    u.role AS UserRole,
                    u.status AS UserStatus
                FROM teachers t
                LEFT JOIN users u ON u.Id = t.user_id
                ORDER BY t.Id", connection);

            connection.Open();
            using var reader = command.ExecuteReader();

            var teachers = new List<SchoolTeacherRecord>();
            while (reader.Read())
            {
                teachers.Add(new SchoolTeacherRecord
                {
                    TeacherId = Convert.ToInt64(reader["TeacherId"]),
                    UserId = Convert.ToInt64(reader["UserId"]),
                    EmployeeNo = Convert.ToString(reader["EmployeeNo"]) ?? string.Empty,
                    FirstName = Convert.ToString(reader["FirstName"]) ?? string.Empty,
                    LastName = Convert.ToString(reader["LastName"]) ?? string.Empty,
                    MiddleName = Convert.ToString(reader["MiddleName"]) ?? string.Empty,
                    Email = Convert.ToString(reader["Email"]) ?? string.Empty,
                    ContactNo = Convert.ToString(reader["ContactNo"]) ?? string.Empty,
                    HireDate = reader["HireDate"] is DBNull ? null : Convert.ToDateTime(reader["HireDate"]),
                    TeacherStatus = Convert.ToString(reader["TeacherStatus"]) ?? string.Empty,
                    Username = Convert.ToString(reader["Username"]) ?? string.Empty,
                    PasswordHash = Convert.ToString(reader["PasswordHash"]) ?? string.Empty,
                    UserRole = Convert.ToString(reader["UserRole"]) ?? string.Empty,
                    UserStatus = Convert.ToString(reader["UserStatus"]) ?? string.Empty
                });
            }

            return teachers;
        }
    }

    public sealed class SchoolTeacherRecord
    {
        public long TeacherId { get; set; }
        public long UserId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactNo { get; set; } = string.Empty;
        public DateTime? HireDate { get; set; }
        public string TeacherStatus { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public string UserStatus { get; set; } = string.Empty;
    }
}
