using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.ViewModels
{
    public class AdminDashboardViewModel : BaseViewModel
    {
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
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            TotalEmployees = ExecuteScalarInt(connection, "SELECT COUNT(*) FROM Employees WHERE IsActive = 1");
            PresentToday = ExecuteScalarInt(connection, @"
                SELECT COUNT(DISTINCT EmployeeId)
                FROM AttendanceRecords
                WHERE AttendanceDate = @Today
                  AND TimeIn IS NOT NULL");
            LateToday = ExecuteScalarInt(connection, @"
                SELECT COUNT(*)
                FROM AttendanceRecords
                WHERE AttendanceDate = @Today
                  AND Status = 'Late'");

            AbsentToday = Math.Max(0, TotalEmployees - PresentToday);
        }

        private void LoadBirthdayCelebrants()
        {
            BirthdayCelebrants.Clear();

            var birthdayColumnExists = ColumnExists("Employees", "BirthDate");
            var dateColumn = birthdayColumnExists ? "BirthDate" : "HireDate";
            var label = birthdayColumnExists ? "Birthday Today" : "Anniversary (Hire Date)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand($@"
                SELECT EmployeeId, EmployeeCode, FullName, ProfileImage
                FROM Employees
                WHERE IsActive = 1
                  AND MONTH({dateColumn}) = MONTH(@Today)
                  AND DAY({dateColumn}) = DAY(@Today)
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

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(@"
                SELECT TOP 20
                    a.AttendanceDate,
                    a.TimeIn,
                    a.TimeOut,
                    a.Status,
                    e.EmployeeCode,
                    e.FullName
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                ORDER BY a.AttendanceDate DESC, a.TimeIn DESC", connection);

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

        private static int ExecuteScalarInt(SqlConnection connection, string sql)
        {
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Today", DateTime.Today);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static bool ColumnExists(string tableName, string columnName)
        {
            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(@"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @TableName
                  AND COLUMN_NAME = @ColumnName", connection);

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
