using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
        private string _birthdaySummaryText = string.Empty;
        private string _databaseStatusTitle = string.Empty;
        private string _databaseStatusDetail = string.Empty;
        private string _databaseStatusBadgeText = string.Empty;
        private bool _isOnlineMode;
        private bool _isOfflineMode;
        private bool _hasDatabaseIssue;

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

        public string BirthdaySummaryText
        {
            get => _birthdaySummaryText;
            set => SetProperty(ref _birthdaySummaryText, value);
        }

        public string DatabaseStatusTitle
        {
            get => _databaseStatusTitle;
            set => SetProperty(ref _databaseStatusTitle, value);
        }

        public string DatabaseStatusDetail
        {
            get => _databaseStatusDetail;
            set => SetProperty(ref _databaseStatusDetail, value);
        }

        public string DatabaseStatusBadgeText
        {
            get => _databaseStatusBadgeText;
            set => SetProperty(ref _databaseStatusBadgeText, value);
        }

        public bool IsOnlineMode
        {
            get => _isOnlineMode;
            set => SetProperty(ref _isOnlineMode, value);
        }

        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            set => SetProperty(ref _isOfflineMode, value);
        }

        public bool HasDatabaseIssue
        {
            get => _hasDatabaseIssue;
            set => SetProperty(ref _hasDatabaseIssue, value);
        }

        public void RefreshDashboard()
        {
            var now = DateTime.Now;
            var snapshot = LoadDashboardSnapshot(now, now.Date);
            ApplyDashboardSnapshot(snapshot);
        }

        public async Task RefreshDashboardAsync()
        {
            var now = DateTime.Now;
            var today = now.Date;
            var snapshot = await Task.Run(() => LoadDashboardSnapshot(now, today));
            ApplyDashboardSnapshot(snapshot);
        }

        private DashboardSnapshot LoadDashboardSnapshot(DateTime now, DateTime today)
        {
            return new DashboardSnapshot
            {
                DashboardDateText = now.ToString("dddd, dd MMMM yyyy"),
                RuntimeStatus = LoadRuntimeStatusSnapshot(),
                Statistics = LoadStatisticsSnapshot(today),
                BirthdayCelebrants = LoadBirthdayCelebrantsSnapshot(today),
                LatestAttendances = LoadLatestAttendancesSnapshot()
            };
        }

        private RuntimeStatusSnapshot LoadRuntimeStatusSnapshot()
        {
            var pendingSyncOperations = DatabaseRuntimeState.IsOfflineDatabaseAvailable
                ? MySqlOfflineSyncService.GetPendingOperationCount()
                : 0;

            if (DatabaseRuntimeState.UseOfflineDatabase)
            {
                return new RuntimeStatusSnapshot
                {
                    IsOfflineMode = true,
                    DatabaseStatusTitle = "OFFLINE MIRROR ACTIVE",
                    DatabaseStatusBadgeText = "LOCAL MODE",
                    DatabaseStatusDetail = $"{DatabaseHelper.GetActiveConnectionSummary()}\nPending sync operations: {pendingSyncOperations}"
                };
            }

            if (DatabaseRuntimeState.IsOnlineAvailable)
            {
                var detail = DatabaseHelper.GetActiveConnectionSummary();
                if (DatabaseRuntimeState.IsOfflineDatabaseAvailable)
                {
                    detail = $"{detail}\nPending sync operations: {pendingSyncOperations}";
                }

                return new RuntimeStatusSnapshot
                {
                    IsOnlineMode = true,
                    DatabaseStatusTitle = "DATABASE CONNECTED",
                    DatabaseStatusBadgeText = "ONLINE MODE",
                    DatabaseStatusDetail = detail
                };
            }

            return new RuntimeStatusSnapshot
            {
                HasDatabaseIssue = true,
                DatabaseStatusTitle = "DATABASE ATTENTION",
                DatabaseStatusBadgeText = "CHECK SETTINGS",
                DatabaseStatusDetail = BuildCompactStatusMessage(DatabaseRuntimeState.StatusMessage)
            };
        }

        private StatisticsSnapshot LoadStatisticsSnapshot(DateTime today)
        {
            if (SupabaseConfig.UseApi)
            {
                var employees = _employeeRepository.GetAllEmployees();
                var activeEmployees = employees.Where(employee => employee.IsActive).ToList();
                var todaysAttendances = _attendanceRepository.GetAttendancesByDate(today);
                var presentToday = todaysAttendances
                    .Where(attendance => attendance.TimeInAM.HasValue || attendance.TimeInPM.HasValue)
                    .Select(attendance => attendance.EmployeeId)
                    .Distinct()
                    .Count();
                var lateToday = todaysAttendances.Count(attendance => string.Equals(attendance.Status, "Late", StringComparison.OrdinalIgnoreCase));

                return new StatisticsSnapshot
                {
                    TotalEmployees = activeEmployees.Count,
                    PresentToday = presentToday,
                    LateToday = lateToday,
                    AbsentToday = Math.Max(0, activeEmployees.Count - presentToday)
                };
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND SourceTeacherId IS NOT NULL"
                : string.Empty;
            var joinedEmployeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? " AND e.SourceTeacherId IS NOT NULL"
                : string.Empty;

            var totalEmployees = ExecuteScalarInt(connection, $"SELECT COUNT(*) FROM Employees WHERE IsActive = TRUE{employeeFilter}");
            var dbPresentToday = ExecuteScalarInt(connection, $@"
                SELECT COUNT(DISTINCT a.EmployeeId)
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                WHERE a.AttendanceDate = @Today
                  AND a.TimeInAM IS NOT NULL{joinedEmployeeFilter}", today);
            var dbLateToday = ExecuteScalarInt(connection, $@"
                SELECT COUNT(*)
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                WHERE a.AttendanceDate = @Today
                  AND a.Status = 'Late'{joinedEmployeeFilter}", today);

            return new StatisticsSnapshot
            {
                TotalEmployees = totalEmployees,
                PresentToday = dbPresentToday,
                LateToday = dbLateToday,
                AbsentToday = Math.Max(0, totalEmployees - dbPresentToday)
            };
        }

        private List<BirthdayEmployeeItem> LoadBirthdayCelebrantsSnapshot(DateTime today)
        {
            if (SupabaseConfig.UseApi)
            {
                return _employeeRepository.GetAllEmployees()
                    .Where(employee => employee.IsActive)
                    .Where(employee => employee.HireDate.Month == today.Month && employee.HireDate.Day == today.Day)
                    .OrderBy(employee => employee.FullName)
                    .Select(employee => new BirthdayEmployeeItem
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeCode = employee.EmployeeCode,
                        FullName = employee.FullName,
                        ProfileImage = employee.ProfileImage,
                        Label = "Anniversary (Hire Date)"
                    })
                    .ToList();
            }

            var birthdayColumnExists = ColumnExists("Employees", "BirthDate");
            var dateColumn = birthdayColumnExists ? "BirthDate" : "HireDate";
            var label = birthdayColumnExists ? "Birthday Today" : "Anniversary (Hire Date)";
            var celebrants = new List<BirthdayEmployeeItem>();

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

            command.Parameters.AddWithValue("@Today", today);
            connection.Open();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                celebrants.Add(new BirthdayEmployeeItem
                {
                    EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    ProfileImage = reader["ProfileImage"] is DBNull ? null : (byte[])reader["ProfileImage"],
                    Label = label
                });
            }

            return celebrants;
        }

        private List<LatestAttendanceItem> LoadLatestAttendancesSnapshot()
        {
            if (SupabaseConfig.UseApi)
            {
                var employees = _employeeRepository.GetAllEmployees().ToDictionary(employee => employee.EmployeeId);
                return _attendanceRepository.GetRecentAttendances(20)
                    .Select(attendance =>
                    {
                        employees.TryGetValue(attendance.EmployeeId, out var employee);
                        return new LatestAttendanceItem
                        {
                            EmployeeCode = employee?.EmployeeCode ?? string.Empty,
                            FullName = employee?.FullName ?? string.Empty,
                            AttendanceDate = attendance.AttendanceDate,
                            TimeIn = attendance.TimeInAM,
                            TimeOut = attendance.TimeOutPM,
                            Status = attendance.Status
                        };
                    })
                    .ToList();
            }

            var latestAttendances = new List<LatestAttendanceItem>();
            using var connection = DatabaseHelper.GetConnection();
            var employeeFilter = EmployeeSourcePolicy.UseSchoolAsExclusiveSource
                ? "WHERE e.SourceTeacherId IS NOT NULL"
                : string.Empty;
            using var command = new MySqlCommand($@"
                SELECT
                    a.AttendanceDate,
                    a.TimeInAM,
                    a.TimeOutPM,
                    a.Status,
                    e.EmployeeCode,
                    e.FullName
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                {employeeFilter}
                ORDER BY a.AttendanceDate DESC, a.TimeInAM DESC
                LIMIT 20", connection);

            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                latestAttendances.Add(new LatestAttendanceItem
                {
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    AttendanceDate = Convert.ToDateTime(reader["AttendanceDate"]),
                    TimeIn = reader["TimeInAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeInAM"]),
                    TimeOut = reader["TimeOutPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutPM"]),
                    Status = Convert.ToString(reader["Status"]) ?? string.Empty
                });
            }

            return latestAttendances;
        }

        private static int ExecuteScalarInt(MySqlConnection connection, string sql, DateTime? today = null)
        {
            using var command = new MySqlCommand(sql, connection);
            if (today.HasValue)
            {
                command.Parameters.AddWithValue("@Today", today.Value);
            }

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private void ApplyDashboardSnapshot(DashboardSnapshot snapshot)
        {
            DashboardDateText = snapshot.DashboardDateText;

            IsOnlineMode = snapshot.RuntimeStatus.IsOnlineMode;
            IsOfflineMode = snapshot.RuntimeStatus.IsOfflineMode;
            HasDatabaseIssue = snapshot.RuntimeStatus.HasDatabaseIssue;
            DatabaseStatusTitle = snapshot.RuntimeStatus.DatabaseStatusTitle;
            DatabaseStatusDetail = snapshot.RuntimeStatus.DatabaseStatusDetail;
            DatabaseStatusBadgeText = snapshot.RuntimeStatus.DatabaseStatusBadgeText;

            TotalEmployees = snapshot.Statistics.TotalEmployees;
            PresentToday = snapshot.Statistics.PresentToday;
            LateToday = snapshot.Statistics.LateToday;
            AbsentToday = snapshot.Statistics.AbsentToday;

            BirthdayCelebrants.Clear();
            foreach (var celebrant in snapshot.BirthdayCelebrants)
            {
                BirthdayCelebrants.Add(celebrant);
            }

            LatestAttendances.Clear();
            foreach (var attendance in snapshot.LatestAttendances)
            {
                LatestAttendances.Add(attendance);
            }

            UpdateBirthdaySummary();
        }

        private void UpdateBirthdaySummary()
        {
            BirthdaySummaryText = BirthdayCelebrants.Count switch
            {
                0 => "No celebrants today",
                1 => "1 celebrant today",
                _ => $"{BirthdayCelebrants.Count} celebrants today"
            };
        }

        private static string BuildCompactStatusMessage(string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(statusMessage))
            {
                return "Database status is unavailable.";
            }

            var lines = statusMessage
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(3)
                .ToArray();

            return lines.Length == 0
                ? "Database status is unavailable."
                : string.Join("\n", lines);
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

        private sealed class DashboardSnapshot
        {
            public string DashboardDateText { get; init; } = string.Empty;
            public RuntimeStatusSnapshot RuntimeStatus { get; init; } = new();
            public StatisticsSnapshot Statistics { get; init; } = new();
            public List<BirthdayEmployeeItem> BirthdayCelebrants { get; init; } = new();
            public List<LatestAttendanceItem> LatestAttendances { get; init; } = new();
        }

        private sealed class RuntimeStatusSnapshot
        {
            public bool IsOnlineMode { get; init; }
            public bool IsOfflineMode { get; init; }
            public bool HasDatabaseIssue { get; init; }
            public string DatabaseStatusTitle { get; init; } = string.Empty;
            public string DatabaseStatusDetail { get; init; } = string.Empty;
            public string DatabaseStatusBadgeText { get; init; } = string.Empty;
        }

        private sealed class StatisticsSnapshot
        {
            public int TotalEmployees { get; init; }
            public int PresentToday { get; init; }
            public int LateToday { get; init; }
            public int AbsentToday { get; init; }
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
