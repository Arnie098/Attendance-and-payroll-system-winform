using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class AttendanceRepository
    {
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
            const string sql = @"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
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
                        ["select"] = "attendanceid,employeeid,attendancedate,timein,timeout,status,isbiometricverified",
                        ["attendancedate"] = $"eq.{attendanceDate:yyyy-MM-dd}",
                        ["order"] = "timein.desc"
                    });
            }

            var attendanceList = new List<Attendance>();
            const string sql = @"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
                FROM AttendanceRecords
                WHERE AttendanceDate = @AttendanceDate
                ORDER BY TimeIn DESC";

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

        public List<Attendance> GetRecentAttendances(int limit)
        {
            if (SupabaseConfig.UseApi)
            {
                return SupabaseRestClient.GetList<Attendance>(
                    "attendancerecords",
                    new Dictionary<string, string>
                    {
                        ["select"] = "attendanceid,employeeid,attendancedate,timein,timeout,status,isbiometricverified",
                        ["order"] = "attendancedate.desc,timein.desc",
                        ["limit"] = limit.ToString()
                    });
            }

            var attendanceList = new List<Attendance>();
            const string sql = @"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
                FROM AttendanceRecords
                ORDER BY AttendanceDate DESC, TimeIn DESC
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
                        ["select"] = "attendanceid,employeeid,attendancedate,timein,timeout,status,isbiometricverified",
                        ["employeeid"] = $"eq.{employeeId}",
                        ["attendancedate"] = $"eq.{DateTime.Today:yyyy-MM-dd}",
                        ["limit"] = "1"
                    });
            }

            const string sql = @"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
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

        public void RecordTimeIn(int employeeId, bool biometricVerified)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.InsertAndReturnSingle<Attendance>(
                    "attendancerecords",
                    new
                    {
                        employeeid = employeeId,
                        attendancedate = DateTime.Today,
                        timein = DateTime.Now,
                        status = "Present",
                        isbiometricverified = biometricVerified
                    });
                return;
            }

            const string sql = @"
                INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeIn, Status, IsBiometricVerified)
                VALUES (@EmployeeId, @AttendanceDate, @TimeIn, @Status, @IsBiometricVerified)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@AttendanceDate", DateTime.Today);
            command.Parameters.AddWithValue("@TimeIn", DateTime.Now);
            command.Parameters.AddWithValue("@Status", "Present");
            command.Parameters.AddWithValue("@IsBiometricVerified", biometricVerified);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void RecordTimeOut(int attendanceId)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.Update(
                    "attendancerecords",
                    new { timeout = DateTime.Now },
                    new Dictionary<string, string>
                    {
                        ["attendanceid"] = $"eq.{attendanceId}"
                    });
                return;
            }

            const string sql = @"
                UPDATE AttendanceRecords
                SET TimeOut = @TimeOut
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TimeOut", DateTime.Now);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void AddAttendance(Attendance attendance)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.InsertAndReturnSingle<Attendance>("attendancerecords", BuildAttendancePayload(attendance));
                return;
            }

            const string sql = @"
                INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified)
                VALUES (@EmployeeId, @AttendanceDate, @TimeIn, @TimeOut, @Status, @IsBiometricVerified)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", attendance.EmployeeId);
            command.Parameters.AddWithValue("@AttendanceDate", attendance.AttendanceDate.Date);
            command.Parameters.AddWithValue("@TimeIn", (object?)attendance.TimeIn ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOut", (object?)attendance.TimeOut ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", attendance.Status);
            command.Parameters.AddWithValue("@IsBiometricVerified", attendance.IsBiometricVerified);
            connection.Open();
            command.ExecuteNonQuery();
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
                    TimeIn = @TimeIn,
                    TimeOut = @TimeOut,
                    Status = @Status,
                    IsBiometricVerified = @IsBiometricVerified
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceId", attendance.AttendanceId);
            command.Parameters.AddWithValue("@AttendanceDate", attendance.AttendanceDate.Date);
            command.Parameters.AddWithValue("@TimeIn", (object?)attendance.TimeIn ?? DBNull.Value);
            command.Parameters.AddWithValue("@TimeOut", (object?)attendance.TimeOut ?? DBNull.Value);
            command.Parameters.AddWithValue("@Status", attendance.Status);
            command.Parameters.AddWithValue("@IsBiometricVerified", attendance.IsBiometricVerified);
            connection.Open();
            command.ExecuteNonQuery();
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
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        private static List<Attendance> GetAttendanceByEmployeeViaApi(int employeeId, DateTime startDate, DateTime endDate)
        {
            return SupabaseRestClient.GetList<Attendance>(
                "attendancerecords",
                new Dictionary<string, string>
                {
                    ["select"] = "attendanceid,employeeid,attendancedate,timein,timeout,status,isbiometricverified",
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
                timein = attendance.TimeIn,
                timeout = attendance.TimeOut,
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
                TimeIn = reader["TimeIn"] is DBNull ? null : Convert.ToDateTime(reader["TimeIn"]),
                TimeOut = reader["TimeOut"] is DBNull ? null : Convert.ToDateTime(reader["TimeOut"]),
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                IsBiometricVerified = Convert.ToBoolean(reader["IsBiometricVerified"])
            };
        }
    }
}
