using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class AttendanceRepository
    {
        private const string SelectColumns =
            "AttendanceId, EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified";

        private const string ApiSelectColumns =
            "attendanceid,employeeid,attendancedate,timeinam,timeoutam,timeinpm,timeoutpm,status,isbiometricverified";

        public List<Attendance> GetAttendanceByEmployee(int employeeId)
        {
            return GetAttendanceByEmployee(employeeId, DateTime.Today.AddMonths(-1), DateTime.Today);
        }

        public List<Attendance> GetAttendanceByEmployee(int employeeId, DateTime startDate, DateTime endDate)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetAttendanceByEmployeeViaApi(employeeId, startDate, endDate);
            }

            var attendanceList = new List<Attendance>();
            string sql = $@"
                SELECT {SelectColumns}
                FROM AttendanceRecords
                WHERE EmployeeId = @EmployeeId
                  AND AttendanceDate >= @StartDate
                  AND AttendanceDate <= @EndDate
                ORDER BY AttendanceDate DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@StartDate", startDate.Date);
            command.Parameters.AddWithValue("@EndDate", endDate.Date);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                attendanceList.Add(MapAttendance(reader));
            }

            return attendanceList;
        }

        public List<Attendance> GetAttendancesByDate(DateTime attendanceDate)
        {
            if (SupabaseConfig.UseApi)
            {
                return SupabaseRestClient.GetList<Attendance>(
                    "attendancerecords",
                    new Dictionary<string, string>
                    {
                        ["select"] = ApiSelectColumns,
                        ["attendancedate"] = $"eq.{attendanceDate:yyyy-MM-dd}",
                        ["order"] = "timeinam.desc"
                    });
            }

            var attendanceList = new List<Attendance>();
            string sql = $@"
                SELECT {SelectColumns}
                FROM AttendanceRecords
                WHERE AttendanceDate = @AttendanceDate
                ORDER BY TimeInAM DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceDate", attendanceDate.Date);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                attendanceList.Add(MapAttendance(reader));
            }

            return attendanceList;
        }

        public List<Models.DtrLedgerEntry> GetDtrLedger(Models.DtrLedgerQuery query)
        {
            var entries = new List<Models.DtrLedgerEntry>();

            var sql = new System.Text.StringBuilder(@"
                SELECT a.AttendanceId, a.EmployeeId,
                       e.EmployeeCode, e.FullName, COALESCE(e.Department, '') AS Department,
                       a.AttendanceDate, a.TimeInAM, a.TimeOutAM, a.TimeInPM, a.TimeOutPM,
                       a.Status, a.IsBiometricVerified
                FROM AttendanceRecords a
                INNER JOIN Employees e ON e.EmployeeId = a.EmployeeId
                WHERE 1=1");

            if (query.StartDate.HasValue)
                sql.Append(" AND a.AttendanceDate >= @StartDate");
            if (query.EndDate.HasValue)
                sql.Append(" AND a.AttendanceDate <= @EndDate");
            if (!string.IsNullOrWhiteSpace(query.SearchText))
                sql.Append(" AND (e.EmployeeCode LIKE @Search OR e.FullName LIKE @Search)");
            if (!string.Equals(query.Department, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(query.Department))
                sql.Append(" AND e.Department = @Department");
            if (!string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(query.Status))
                sql.Append(" AND a.Status = @Status");
            if (query.MissingPunchOnly)
                sql.Append(@" AND (
                    (a.TimeInAM IS NOT NULL AND a.TimeOutAM IS NULL) OR
                    (a.TimeInAM IS NULL  AND a.TimeOutAM IS NOT NULL) OR
                    (a.TimeInPM IS NOT NULL AND a.TimeOutPM IS NULL) OR
                    (a.TimeInPM IS NULL  AND a.TimeOutPM IS NOT NULL))");

            sql.Append(" ORDER BY a.AttendanceDate DESC, e.FullName ASC");
            sql.Append(" LIMIT @Limit");

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql.ToString(), connection);

            if (query.StartDate.HasValue)
                command.Parameters.AddWithValue("@StartDate", query.StartDate.Value.Date);
            if (query.EndDate.HasValue)
                command.Parameters.AddWithValue("@EndDate", query.EndDate.Value.Date);
            if (!string.IsNullOrWhiteSpace(query.SearchText))
                command.Parameters.AddWithValue("@Search", $"%{query.SearchText.Trim()}%");
            if (!string.Equals(query.Department, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(query.Department))
                command.Parameters.AddWithValue("@Department", query.Department);
            if (!string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(query.Status))
                command.Parameters.AddWithValue("@Status", query.Status);
            command.Parameters.AddWithValue("@Limit", query.Limit);

            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new Models.DtrLedgerEntry
                {
                    AttendanceId      = Convert.ToInt32(reader["AttendanceId"]),
                    EmployeeId        = Convert.ToInt32(reader["EmployeeId"]),
                    EmployeeCode      = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                    FullName          = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    Department        = Convert.ToString(reader["Department"]) ?? string.Empty,
                    AttendanceDate    = Convert.ToDateTime(reader["AttendanceDate"]),
                    TimeInAM          = reader["TimeInAM"]  is DBNull ? null : Convert.ToDateTime(reader["TimeInAM"]),
                    TimeOutAM         = reader["TimeOutAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutAM"]),
                    TimeInPM          = reader["TimeInPM"]  is DBNull ? null : Convert.ToDateTime(reader["TimeInPM"]),
                    TimeOutPM         = reader["TimeOutPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutPM"]),
                    Status            = Convert.ToString(reader["Status"]) ?? string.Empty,
                    IsBiometricVerified = Convert.ToBoolean(reader["IsBiometricVerified"])
                });
            }

            return entries;
        }

        public List<string> GetDistinctDepartments()
        {
            var departments = new List<string> { "All" };
            const string sql = "SELECT DISTINCT Department FROM Employees WHERE Department IS NOT NULL AND Department <> '' ORDER BY Department";
            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            connection.Open();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                departments.Add(Convert.ToString(reader["Department"]) ?? string.Empty);
            return departments;
        }

        public List<Attendance> GetRecentAttendances(int limit)
        {
            if (SupabaseConfig.UseApi)
            {
                return SupabaseRestClient.GetList<Attendance>(
                    "attendancerecords",
                    new Dictionary<string, string>
                    {
                        ["select"] = ApiSelectColumns,
                        ["order"] = "attendancedate.desc,timeinam.desc",
                        ["limit"] = limit.ToString()
                    });
            }

            var attendanceList = new List<Attendance>();
            string sql = $@"
                SELECT {SelectColumns}
                FROM AttendanceRecords
                ORDER BY AttendanceDate DESC, TimeInAM DESC
                LIMIT @Limit";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Limit", limit);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                attendanceList.Add(MapAttendance(reader));
            }

            return attendanceList;
        }

        public Attendance? GetTodayAttendance(int employeeId)
        {
            if (SupabaseConfig.UseApi)
            {
                return SupabaseRestClient.GetSingleOrDefault<Attendance>(
                    "attendancerecords",
                    new Dictionary<string, string>
                    {
                        ["select"] = ApiSelectColumns,
                        ["employeeid"] = $"eq.{employeeId}",
                        ["attendancedate"] = $"eq.{DateTime.Today:yyyy-MM-dd}",
                        ["limit"] = "1"
                    });
            }

            string sql = $@"
                SELECT {SelectColumns}
                FROM AttendanceRecords
                WHERE EmployeeId = @EmployeeId
                  AND AttendanceDate = @AttendanceDate
                LIMIT 1";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@AttendanceDate", DateTime.Today);
            connection.Open();
            using var reader = command.ExecuteReader();

            return reader.Read() ? MapAttendance(reader) : null;
        }

        /// <summary>
        /// Records time-in for the current session based on time of day.
        /// Morning session (before 12:00): sets TimeInAM.
        /// Afternoon session (12:00 or later): sets TimeInPM.
        /// </summary>
        public void RecordTimeIn(int employeeId, bool biometricVerified)
        {
            var now = TruncateToMinute(DateTime.Now);
            bool isMorning = now.Hour < 12;
            var timeColumn = isMorning ? "TimeInAM" : "TimeInPM";

            if (SupabaseConfig.UseApi)
            {
                // Check if record exists for today
                var existing = GetTodayAttendance(employeeId);
                if (existing != null)
                {
                    var updatedStatus = DetermineAttendanceStatus(
                        existing.AttendanceDate,
                        isMorning ? now : existing.TimeInAM,
                        isMorning ? existing.TimeInPM : now,
                        existing.Status);

                    // Update the appropriate session column
                    var updatePayload = isMorning
                        ? (object)new { timeinam = now, status = updatedStatus, isbiometricverified = biometricVerified || existing.IsBiometricVerified }
                        : new { timeinpm = now, status = updatedStatus, isbiometricverified = biometricVerified || existing.IsBiometricVerified };
                    SupabaseRestClient.Update(
                        "attendancerecords",
                        updatePayload,
                        new Dictionary<string, string>
                        {
                            ["attendanceid"] = $"eq.{existing.AttendanceId}"
                        });
                }
                else
                {
                    var status = DetermineAttendanceStatus(
                        DateTime.Today,
                        isMorning ? now : null,
                        isMorning ? null : now);

                    var insertPayload = isMorning
                        ? (object)new
                        {
                            employeeid = employeeId,
                            attendancedate = DateTime.Today,
                            timeinam = now,
                            status,
                            isbiometricverified = biometricVerified
                        }
                        : new
                        {
                            employeeid = employeeId,
                            attendancedate = DateTime.Today,
                            timeinpm = now,
                            status,
                            isbiometricverified = biometricVerified
                        };
                    SupabaseRestClient.InsertAndReturnSingle<Attendance>("attendancerecords", insertPayload);
                }
                return;
            }

            // Check if a record already exists for today
            var todayRecord = GetTodayAttendance(employeeId);
            if (todayRecord != null)
            {
                var updatedStatus = DetermineAttendanceStatus(
                    todayRecord.AttendanceDate,
                    isMorning ? now : todayRecord.TimeInAM,
                    isMorning ? todayRecord.TimeInPM : now,
                    todayRecord.Status);

                // Update the appropriate session time-in
                string sql = $@"
                    UPDATE AttendanceRecords
                    SET {timeColumn} = @TimeIn,
                        Status = @Status,
                        IsBiometricVerified = @IsBiometricVerified
                    WHERE AttendanceId = @AttendanceId";

                using var connection = DatabaseHelper.GetConnection();
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@TimeIn", now);
                command.Parameters.AddWithValue("@Status", updatedStatus);
                command.Parameters.AddWithValue("@IsBiometricVerified", biometricVerified || todayRecord.IsBiometricVerified);
                command.Parameters.AddWithValue("@AttendanceId", todayRecord.AttendanceId);
                connection.Open();
                command.ExecuteNonQuery();
                MySqlOfflineSyncService.QueueAttendanceUpsert(todayRecord.AttendanceId, employeeId);
            }
            else
            {
                var status = DetermineAttendanceStatus(
                    DateTime.Today,
                    isMorning ? now : null,
                    isMorning ? null : now);

                // Insert new record with the appropriate session time-in
                string sql = $@"
                    INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, {timeColumn}, Status, IsBiometricVerified)
                    VALUES (@EmployeeId, @AttendanceDate, @TimeIn, @Status, @IsBiometricVerified)";

                using var connection = DatabaseHelper.GetConnection();
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@EmployeeId", employeeId);
                command.Parameters.AddWithValue("@AttendanceDate", DateTime.Today);
                command.Parameters.AddWithValue("@TimeIn", now);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@IsBiometricVerified", biometricVerified);
                connection.Open();
                command.ExecuteNonQuery();
                MySqlOfflineSyncService.QueueAttendanceUpsert(Convert.ToInt32(command.LastInsertedId), employeeId);
            }
        }

        /// <summary>
        /// Records time-out for the current session based on time of day.
        /// Morning session (before 12:30): sets TimeOutAM.
        /// Afternoon session (12:30 or later): sets TimeOutPM.
        /// </summary>
        public void RecordTimeOut(int attendanceId)
        {
            var now = TruncateToMinute(DateTime.Now);
            bool isMorning = now.Hour < 13 || (now.Hour == 12 && now.Minute < 30);
            var timeColumn = isMorning ? "TimeOutAM" : "TimeOutPM";

            if (SupabaseConfig.UseApi)
            {
                var updatePayload = isMorning
                    ? (object)new { timeoutam = now }
                    : new { timeoutpm = now };
                SupabaseRestClient.Update(
                    "attendancerecords",
                    updatePayload,
                    new Dictionary<string, string>
                    {
                        ["attendanceid"] = $"eq.{attendanceId}"
                    });
                return;
            }

            string sql = $@"
                UPDATE AttendanceRecords
                SET {timeColumn} = @TimeOut
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            var employeeId = GetEmployeeId(connection, attendanceId);

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TimeOut", now);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            command.ExecuteNonQuery();

            if (employeeId.HasValue)
            {
                MySqlOfflineSyncService.QueueAttendanceUpsert(attendanceId, employeeId.Value);
            }
        }

        public void RecordSpecificSlot(int employeeId, string slotColumn, bool biometricVerified)
        {
            var allowed = new HashSet<string> { "TimeInAM", "TimeOutAM", "TimeInPM", "TimeOutPM" };
            if (!allowed.Contains(slotColumn))
                throw new ArgumentException($"Unknown slot column: {slotColumn}");

            bool isTimeIn = slotColumn == "TimeInAM" || slotColumn == "TimeInPM";
            var now = TruncateToMinute(DateTime.Now);

            if (SupabaseConfig.UseApi)
            {
                var existing = GetTodayAttendance(employeeId);
                if (existing != null)
                {
                    string updatedStatus = existing.Status;
                    if (isTimeIn)
                    {
                        var amIn = slotColumn == "TimeInAM" ? now : existing.TimeInAM;
                        var pmIn = slotColumn == "TimeInPM" ? now : existing.TimeInPM;
                        updatedStatus = DetermineAttendanceStatus(existing.AttendanceDate, amIn, pmIn, existing.Status);
                    }
                    var newBio = biometricVerified || existing.IsBiometricVerified;
                    var updatePayload = slotColumn switch
                    {
                        "TimeInAM"  => (object)new { timeinam  = now, status = updatedStatus, isbiometricverified = newBio },
                        "TimeOutAM" =>         new { timeoutam = now, status = updatedStatus, isbiometricverified = newBio },
                        "TimeInPM"  =>         new { timeinpm  = now, status = updatedStatus, isbiometricverified = newBio },
                        _           =>         new { timeoutpm = now, status = updatedStatus, isbiometricverified = newBio },
                    };
                    SupabaseRestClient.Update(
                        "attendancerecords",
                        updatePayload,
                        new Dictionary<string, string> { ["attendanceid"] = $"eq.{existing.AttendanceId}" });
                }
                else
                {
                    if (!isTimeIn)
                        throw new InvalidOperationException("Cannot record Time Out — no attendance record exists for today.");

                    var status = DetermineAttendanceStatus(
                        DateTime.Today,
                        slotColumn == "TimeInAM" ? now : (DateTime?)null,
                        slotColumn == "TimeInPM" ? now : (DateTime?)null);

                    var insertPayload = slotColumn == "TimeInAM"
                        ? (object)new { employeeid = employeeId, attendancedate = DateTime.Today, timeinam = now, status, isbiometricverified = biometricVerified }
                        :          new { employeeid = employeeId, attendancedate = DateTime.Today, timeinpm = now, status, isbiometricverified = biometricVerified };
                    SupabaseRestClient.InsertAndReturnSingle<Attendance>("attendancerecords", insertPayload);
                }
                return;
            }

            var todayRecord = GetTodayAttendance(employeeId);
            if (todayRecord != null)
            {
                string updatedStatus = todayRecord.Status;
                if (isTimeIn)
                {
                    var amIn = slotColumn == "TimeInAM" ? now : todayRecord.TimeInAM;
                    var pmIn = slotColumn == "TimeInPM" ? now : todayRecord.TimeInPM;
                    updatedStatus = DetermineAttendanceStatus(todayRecord.AttendanceDate, amIn, pmIn, todayRecord.Status);
                }
                string sql = $@"
                    UPDATE AttendanceRecords
                    SET {slotColumn} = @Time,
                        Status = @Status,
                        IsBiometricVerified = @Bio
                    WHERE AttendanceId = @AttendanceId";
                using var connection = DatabaseHelper.GetConnection();
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Time", now);
                command.Parameters.AddWithValue("@Status", updatedStatus);
                command.Parameters.AddWithValue("@Bio", biometricVerified || todayRecord.IsBiometricVerified);
                command.Parameters.AddWithValue("@AttendanceId", todayRecord.AttendanceId);
                connection.Open();
                command.ExecuteNonQuery();
                MySqlOfflineSyncService.QueueAttendanceUpsert(todayRecord.AttendanceId, employeeId);
            }
            else
            {
                if (!isTimeIn)
                    throw new InvalidOperationException("Cannot record Time Out — no attendance record exists for today.");

                var status = DetermineAttendanceStatus(
                    DateTime.Today,
                    slotColumn == "TimeInAM" ? now : (DateTime?)null,
                    slotColumn == "TimeInPM" ? now : (DateTime?)null);

                string sql = $@"
                    INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, {slotColumn}, Status, IsBiometricVerified)
                    VALUES (@EmployeeId, @AttendanceDate, @Time, @Status, @Bio)";
                using var connection = DatabaseHelper.GetConnection();
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@EmployeeId", employeeId);
                command.Parameters.AddWithValue("@AttendanceDate", DateTime.Today);
                command.Parameters.AddWithValue("@Time", now);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@Bio", biometricVerified);
                connection.Open();
                command.ExecuteNonQuery();
                MySqlOfflineSyncService.QueueAttendanceUpsert(Convert.ToInt32(command.LastInsertedId), employeeId);
            }
        }

        public void AddAttendance(Attendance attendance)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.InsertAndReturnSingle<Attendance>("attendancerecords", BuildAttendancePayload(attendance));
                return;
            }

            const string sql = @"
                INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified)
                VALUES (@EmployeeId, @AttendanceDate, @TimeInAM, @TimeOutAM, @TimeInPM, @TimeOutPM, @Status, @IsBiometricVerified)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", attendance.EmployeeId);
            command.Parameters.AddWithValue("@AttendanceDate", attendance.AttendanceDate.Date);
            command.Parameters.AddWithValue("@TimeInAM", (object?)attendance.TimeInAM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutAM", (object?)attendance.TimeOutAM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeInPM", (object?)attendance.TimeInPM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutPM", (object?)attendance.TimeOutPM ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", attendance.Status);
            command.Parameters.AddWithValue("@IsBiometricVerified", attendance.IsBiometricVerified);
            connection.Open();
            command.ExecuteNonQuery();
            MySqlOfflineSyncService.QueueAttendanceUpsert(Convert.ToInt32(command.LastInsertedId), attendance.EmployeeId);
        }

        public void UpdateAttendance(Attendance attendance)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.Update(
                    "attendancerecords",
                    BuildAttendancePayload(attendance),
                    new Dictionary<string, string>
                    {
                        ["attendanceid"] = $"eq.{attendance.AttendanceId}"
                    });
                return;
            }

            const string sql = @"
                UPDATE AttendanceRecords
                SET AttendanceDate = @AttendanceDate,
                    TimeInAM = @TimeInAM,
                    TimeOutAM = @TimeOutAM,
                    TimeInPM = @TimeInPM,
                    TimeOutPM = @TimeOutPM,
                    Status = @Status,
                    IsBiometricVerified = @IsBiometricVerified
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceId", attendance.AttendanceId);
            command.Parameters.AddWithValue("@AttendanceDate", attendance.AttendanceDate.Date);
            command.Parameters.AddWithValue("@TimeInAM", (object?)attendance.TimeInAM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutAM", (object?)attendance.TimeOutAM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeInPM", (object?)attendance.TimeInPM ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutPM", (object?)attendance.TimeOutPM ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", attendance.Status);
            command.Parameters.AddWithValue("@IsBiometricVerified", attendance.IsBiometricVerified);
            connection.Open();
            command.ExecuteNonQuery();
            MySqlOfflineSyncService.QueueAttendanceUpsert(attendance.AttendanceId, attendance.EmployeeId);
        }

        public void DeleteAttendance(int attendanceId)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.Delete(
                    "attendancerecords",
                    new Dictionary<string, string>
                    {
                        ["attendanceid"] = $"eq.{attendanceId}"
                    });
                return;
            }

            const string sql = "DELETE FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            var employeeId = GetEmployeeId(connection, attendanceId);

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            command.ExecuteNonQuery();

            if (employeeId.HasValue)
            {
                MySqlOfflineSyncService.QueueAttendanceDelete(attendanceId, employeeId.Value);
            }
        }

        /// <summary>
        /// Determines the current session state for biometric scanning logic.
        /// Returns which action should be taken next.
        /// </summary>
        public AttendanceSessionState GetSessionState(Attendance? todayAttendance)
        {
            var now = DateTime.Now;
            bool isMorningTime = now.Hour < 12;

            if (todayAttendance == null)
            {
                return isMorningTime
                    ? AttendanceSessionState.NeedsMorningTimeIn
                    : AttendanceSessionState.NeedsAfternoonTimeIn;
            }

            // Morning period (before 12:00)
            if (isMorningTime)
            {
                if (!todayAttendance.TimeInAM.HasValue)
                    return AttendanceSessionState.NeedsMorningTimeIn;
                if (!todayAttendance.TimeOutAM.HasValue)
                    return AttendanceSessionState.NeedsMorningTimeOut;
                // Morning complete, afternoon not yet
                return AttendanceSessionState.MorningComplete;
            }

            // Afternoon period (12:00 or later)
            if (!todayAttendance.TimeInAM.HasValue && !todayAttendance.TimeInPM.HasValue)
                return AttendanceSessionState.NeedsAfternoonTimeIn;

            if (todayAttendance.TimeInAM.HasValue && !todayAttendance.TimeOutAM.HasValue)
                return AttendanceSessionState.NeedsMorningTimeOut;

            if (!todayAttendance.TimeInPM.HasValue)
                return AttendanceSessionState.NeedsAfternoonTimeIn;

            if (!todayAttendance.TimeOutPM.HasValue)
                return AttendanceSessionState.NeedsAfternoonTimeOut;

            return AttendanceSessionState.AllComplete;
        }

        private static List<Attendance> GetAttendanceByEmployeeViaApi(int employeeId, DateTime startDate, DateTime endDate)
        {
            return SupabaseRestClient.GetList<Attendance>(
                "attendancerecords",
                new Dictionary<string, string>
                {
                    ["select"] = ApiSelectColumns,
                    ["employeeid"] = $"eq.{employeeId}",
                    ["and"] = $"(attendancedate.gte.{startDate:yyyy-MM-dd},attendancedate.lte.{endDate:yyyy-MM-dd})",
                    ["order"] = "attendancedate.desc"
                });
        }

        private static object BuildAttendancePayload(Attendance attendance)
        {
            return new
            {
                employeeid = attendance.EmployeeId,
                attendancedate = attendance.AttendanceDate.Date,
                timeinam = attendance.TimeInAM,
                timeoutam = attendance.TimeOutAM,
                timeinpm = attendance.TimeInPM,
                timeoutpm = attendance.TimeOutPM,
                status = attendance.Status,
                isbiometricverified = attendance.IsBiometricVerified
            };
        }

        private static Attendance MapAttendance(MySqlDataReader reader)
        {
            return new Attendance
            {
                AttendanceId = Convert.ToInt32(reader["AttendanceId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                AttendanceDate = Convert.ToDateTime(reader["AttendanceDate"]),
                TimeInAM = reader["TimeInAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeInAM"]),
                TimeOutAM = reader["TimeOutAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutAM"]),
                TimeInPM = reader["TimeInPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeInPM"]),
                TimeOutPM = reader["TimeOutPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutPM"]),
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                IsBiometricVerified = Convert.ToBoolean(reader["IsBiometricVerified"])
            };
        }

        private static string DetermineAttendanceStatus(
            DateTime attendanceDate,
            DateTime? timeInAM,
            DateTime? timeInPM,
            string? existingStatus = null)
        {
            if (!string.IsNullOrWhiteSpace(existingStatus) && LeavePolicies.IsLeaveAttendanceStatus(existingStatus))
            {
                return existingStatus;
            }

            var isLateMorning = IsLateForSession(attendanceDate, timeInAM, DatabaseConfig.MorningStartTime);
            var isLateAfternoon = IsLateForSession(attendanceDate, timeInPM, DatabaseConfig.AfternoonStartTime);
            return isLateMorning || isLateAfternoon ? "Late" : "Present";
        }

        private static bool IsLateForSession(DateTime attendanceDate, DateTime? actualTimeIn, TimeSpan scheduledStart)
        {
            if (!actualTimeIn.HasValue)
            {
                return false;
            }

            var graceEnd = attendanceDate.Date.Add(scheduledStart).AddMinutes(DatabaseConfig.GracePeriodMinutes);
            return actualTimeIn.Value > graceEnd;
        }

        private static int? GetEmployeeId(MySqlConnection connection, int attendanceId)
        {
            using var command = new MySqlCommand(
                "SELECT EmployeeId FROM AttendanceRecords WHERE AttendanceId = @AttendanceId LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            var result = command.ExecuteScalar();
            return result == null || result is DBNull ? null : Convert.ToInt32(result);
        }

        /// <summary>
        /// Strips seconds and sub-second precision so clock-in/out times are stored
        /// at minute granularity, matching manual CRUD entry and the minute-based
        /// tardiness calculation.
        /// </summary>
        private static DateTime TruncateToMinute(DateTime value)
        {
            return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);
        }
    }

    public enum AttendanceSessionState
    {
        NeedsMorningTimeIn,
        NeedsMorningTimeOut,
        MorningComplete,
        NeedsAfternoonTimeIn,
        NeedsAfternoonTimeOut,
        AllComplete
    }
}
