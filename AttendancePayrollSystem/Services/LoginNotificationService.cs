using System;
using System.Collections.Generic;
using System.Linq;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.Services
{
    public class LoginNotificationService
    {
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly AttendanceRepository _attendanceRepository = new();

        public LoginNotificationSnapshot BuildSnapshot(string audienceName, bool isEmployeeAudience)
        {
            var today = DateTime.Today;
            var birthdays = LoadBirthdayNotifications(today);
            var employees = _employeeRepository.GetAllEmployees().ToDictionary(employee => employee.EmployeeId);
            var recentAttendances = _attendanceRepository.GetRecentAttendances(5)
                .Select(attendance =>
                {
                    employees.TryGetValue(attendance.EmployeeId, out var employee);
                    return new LoginNotificationAttendanceItem
                    {
                        EmployeeName = employee?.FullName ?? $"Employee #{attendance.EmployeeId}",
                        Status = attendance.Status,
                        AttendanceDate = attendance.AttendanceDate,
                        TimeIn = attendance.TimeInAM ?? attendance.TimeInPM
                    };
                })
                .ToList();

            var headline = isEmployeeAudience
                ? $"Welcome back, {audienceName}."
                : $"Welcome back, {audienceName}.";
            var summary = birthdays.Count == 0 && recentAttendances.Count == 0
                ? "No new birthday or attendance notifications right now."
                : "Here is a quick summary of today's notable updates.";

            return new LoginNotificationSnapshot
            {
                Headline = headline,
                Summary = summary,
                Birthdays = birthdays,
                RecentAttendances = recentAttendances
            };
        }

        private List<LoginNotificationBirthdayItem> LoadBirthdayNotifications(DateTime today)
        {
            if (SupabaseConfig.UseApi)
            {
                return _employeeRepository.GetAllEmployees()
                    .Where(employee => employee.IsActive)
                    .Where(employee => employee.HireDate.Month == today.Month && employee.HireDate.Day == today.Day)
                    .OrderBy(employee => employee.FullName)
                    .Take(6)
                    .Select(employee => new LoginNotificationBirthdayItem
                    {
                        EmployeeName = employee.FullName,
                        Label = "Anniversary today"
                    })
                    .ToList();
            }

            var birthdayColumnExists = ColumnExists("Employees", "BirthDate");
            var dateColumn = birthdayColumnExists ? "BirthDate" : "HireDate";
            var label = birthdayColumnExists ? "Birthday today" : "Anniversary today";
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND SourceTeacherId IS NOT NULL"
                : string.Empty;

            using var connection = DatabaseHelper.GetConnection();
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? $@"
                    SELECT FullName
                    FROM Employees
                    WHERE IsActive = 1
                      AND strftime('%m', {dateColumn}) = strftime('%m', @Today)
                      AND strftime('%d', {dateColumn}) = strftime('%d', @Today){employeeFilter}
                    ORDER BY FullName
                    LIMIT 6"
                : $@"
                    SELECT FullName
                    FROM Employees
                    WHERE IsActive = TRUE
                      AND MONTH({dateColumn}) = MONTH(@Today)
                      AND DAY({dateColumn}) = DAY(@Today){employeeFilter}
                    ORDER BY FullName
                    LIMIT 6";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Today", today);
            connection.Open();

            var results = new List<LoginNotificationBirthdayItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new LoginNotificationBirthdayItem
                {
                    EmployeeName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    Label = label
                });
            }

            return results;
        }

        private static bool ColumnExists(string tableName, string columnName)
        {
            if (SupabaseConfig.UseApi)
            {
                return false;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            return DatabaseHelper.ColumnExists(connection, tableName, columnName);
        }
    }

    public sealed class LoginNotificationSnapshot
    {
        public string Headline { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public List<LoginNotificationBirthdayItem> Birthdays { get; init; } = new();
        public List<LoginNotificationAttendanceItem> RecentAttendances { get; init; } = new();
    }

    public sealed class LoginNotificationBirthdayItem
    {
        public string EmployeeName { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }

    public sealed class LoginNotificationAttendanceItem
    {
        public string EmployeeName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime AttendanceDate { get; init; }
        public DateTime? TimeIn { get; init; }
    }
}
