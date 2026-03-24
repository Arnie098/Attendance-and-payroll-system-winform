using System;
using System.Collections.ObjectModel;
using System.Linq;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.ViewModels
{
    public class AdminDashboardViewModel : BaseViewModel
    {
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly AttendanceRepository _attendanceRepository = new();
        private int _totalEmployees;
        private int _presentToday;
        private int _lateToday;
        private int _absentToday;
        private string _dashboardDateText = string.Empty;

        public ObservableCollection<BirthdayEmployeeItem> BirthdayCelebrants { get; } = new();
        public ObservableCollection<LatestAttendanceItem> LatestAttendances { get; } = new();

        public int TotalEmployees
        {
            get => _totalEmployees;
            set => SetProperty(ref _totalEmployees, value);
        }

        public int PresentToday
        {
            get => _presentToday;
            set => SetProperty(ref _presentToday, value);
        }

        public int LateToday
        {
            get => _lateToday;
            set => SetProperty(ref _lateToday, value);
        }

        public int AbsentToday
        {
            get => _absentToday;
            set => SetProperty(ref _absentToday, value);
        }

        public string DashboardDateText
        {
            get => _dashboardDateText;
            set => SetProperty(ref _dashboardDateText, value);
        }

        public void RefreshDashboard()
        {
            DashboardDateText = DateTime.Now.ToString("MMMM dd, yyyy");
            LoadStatistics();
            LoadBirthdayCelebrants();
            LoadLatestAttendances();
        }

        private void LoadStatistics()
        {
            if (SupabaseConfig.UseApi)
            {
                var employees = _employeeRepository.GetAllEmployees();
                var activeEmployees = employees.Where(employee => employee.IsActive).ToList();
                var todaysAttendances = _attendanceRepository.GetAttendancesByDate(DateTime.Today);

                TotalEmployees = activeEmployees.Count;
                PresentToday = todaysAttendances
                    .Where(attendance => attendance.TimeIn.HasValue)
                    .Select(attendance => attendance.EmployeeId)
                    .Distinct()
                    .Count();
                LateToday = todaysAttendances.Count(attendance => string.Equals(attendance.Status, "Late", StringComparison.OrdinalIgnoreCase));
                AbsentToday = Math.Max(0, TotalEmployees - PresentToday);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND SourceTeacherId IS NOT NULL"
                : string.Empty;
            var joinedEmployeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND e.SourceTeacherId IS NOT NULL"
                : string.Empty;

            TotalEmployees = ExecuteScalarInt(connection, $"SELECT COUNT(*) FROM Employees WHERE IsActive = TRUE{employeeFilter}");
            PresentToday = ExecuteScalarInt(connection, $@"
                SELECT COUNT(DISTINCT a.EmployeeId)
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                WHERE a.AttendanceDate = @Today
                  AND a.TimeIn IS NOT NULL{joinedEmployeeFilter}", includeToday: true);
            LateToday = ExecuteScalarInt(connection, $@"
                SELECT COUNT(*)
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                WHERE a.AttendanceDate = @Today
                  AND a.Status = 'Late'{joinedEmployeeFilter}", includeToday: true);

            AbsentToday = Math.Max(0, TotalEmployees - PresentToday);
        }

        private void LoadBirthdayCelebrants()
        {
            BirthdayCelebrants.Clear();

            if (SupabaseConfig.UseApi)
            {
                foreach (var employee in _employeeRepository.GetAllEmployees()
                             .Where(employee => employee.IsActive)
                             .Where(employee => employee.HireDate.Month == DateTime.Today.Month && employee.HireDate.Day == DateTime.Today.Day)
                             .OrderBy(employee => employee.FullName))
                {
                    BirthdayCelebrants.Add(new BirthdayEmployeeItem
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeCode = employee.EmployeeCode,
                        FullName = employee.FullName,
                        ProfileImage = employee.ProfileImage,
                        Label = "Anniversary (Hire Date)"
                    });
                }

                return;
            }

            var birthdayColumnExists = ColumnExists("Employees", "BirthDate");
            var dateColumn = birthdayColumnExists ? "BirthDate" : "HireDate";
            var label = birthdayColumnExists ? "Birthday Today" : "Anniversary (Hire Date)";

            using var connection = DatabaseHelper.GetConnection();
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND SourceTeacherId IS NOT NULL"
                : string.Empty;
            using var command = new MySqlCommand($@"
                SELECT EmployeeId, EmployeeCode, FullName, ProfileImage
                FROM Employees
                WHERE IsActive = TRUE
                  AND MONTH({dateColumn}) = MONTH(@Today)
                  AND DAY({dateColumn}) = DAY(@Today){employeeFilter}
                ORDER BY FullName", connection);

            command.Parameters.AddWithValue("@Today", DateTime.Today);
            connection.Open();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                BirthdayCelebrants.Add(new BirthdayEmployeeItem
                {
                    EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    ProfileImage = reader["ProfileImage"] is DBNull ? null : (byte[])reader["ProfileImage"],
                    Label = label
                });
            }
        }

        private void LoadLatestAttendances()
        {
            LatestAttendances.Clear();

            if (SupabaseConfig.UseApi)
            {
                var employees = _employeeRepository.GetAllEmployees().ToDictionary(employee => employee.EmployeeId);
                foreach (var attendance in _attendanceRepository.GetRecentAttendances(20))
                {
                    employees.TryGetValue(attendance.EmployeeId, out var employee);
                    LatestAttendances.Add(new LatestAttendanceItem
                    {
                        EmployeeCode = employee?.EmployeeCode ?? string.Empty,
                        FullName = employee?.FullName ?? string.Empty,
                        AttendanceDate = attendance.AttendanceDate,
                        TimeIn = attendance.TimeIn,
                        TimeOut = attendance.TimeOut,
                        Status = attendance.Status
                    });
                }

                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? "WHERE e.SourceTeacherId IS NOT NULL"
                : string.Empty;
            using var command = new MySqlCommand($@"
                SELECT
                    a.AttendanceDate,
                    a.TimeIn,
                    a.TimeOut,
                    a.Status,
                    e.EmployeeCode,
                    e.FullName
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                {employeeFilter}
                ORDER BY a.AttendanceDate DESC, a.TimeIn DESC
                LIMIT 20", connection);

            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                LatestAttendances.Add(new LatestAttendanceItem
                {
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    AttendanceDate = Convert.ToDateTime(reader["AttendanceDate"]),
                    TimeIn = reader["TimeIn"] is DBNull ? null : Convert.ToDateTime(reader["TimeIn"]),
                    TimeOut = reader["TimeOut"] is DBNull ? null : Convert.ToDateTime(reader["TimeOut"]),
                    Status = Convert.ToString(reader["Status"]) ?? string.Empty
                });
            }
        }

        private static int ExecuteScalarInt(MySqlConnection connection, string sql, bool includeToday = false)
        {
            using var command = new MySqlCommand(sql, connection);
            if (includeToday)
            {
                command.Parameters.AddWithValue("@Today", DateTime.Today);
            }

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static bool ColumnExists(string tableName, string columnName)
        {
            if (SupabaseConfig.UseApi)
            {
                return false;
            }

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND LOWER(TABLE_NAME) = LOWER(@TableName)
                  AND LOWER(COLUMN_NAME) = LOWER(@ColumnName)", connection);

            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);
            connection.Open();

            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }

    public class BirthdayEmployeeItem
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public byte[]? ProfileImage { get; set; }
    }

    public class LatestAttendanceItem
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public DateTime AttendanceDate { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
