using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using AttendancePayrollSystem.Models;

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
            var attendanceList = new List<Attendance>();
            const string sql = @"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
                FROM AttendanceRecords
                WHERE EmployeeId = @EmployeeId
                  AND AttendanceDate >= @StartDate
                  AND AttendanceDate <= @EndDate
                ORDER BY AttendanceDate DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
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

        public Attendance? GetTodayAttendance(int employeeId)
        {
            const string sql = @"
                SELECT TOP 1 AttendanceId, EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified
                FROM AttendanceRecords
                WHERE EmployeeId = @EmployeeId
                  AND AttendanceDate = @AttendanceDate";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@AttendanceDate", DateTime.Today);
            connection.Open();
            using var reader = command.ExecuteReader();

            return reader.Read() ? MapAttendance(reader) : null;
        }

        public void RecordTimeIn(int employeeId, bool biometricVerified)
        {
            const string sql = @"
                INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeIn, Status, IsBiometricVerified)
                VALUES (@EmployeeId, @AttendanceDate, @TimeIn, @Status, @IsBiometricVerified)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
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
            const string sql = @"
                UPDATE AttendanceRecords
                SET TimeOut = @TimeOut
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TimeOut", DateTime.Now);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void AddAttendance(Attendance attendance)
        {
            const string sql = @"
                INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified)
                VALUES (@EmployeeId, @AttendanceDate, @TimeIn, @TimeOut, @Status, @IsBiometricVerified)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
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
            const string sql = @"
                UPDATE AttendanceRecords
                SET AttendanceDate = @AttendanceDate,
                    TimeIn = @TimeIn,
                    TimeOut = @TimeOut,
                    Status = @Status,
                    IsBiometricVerified = @IsBiometricVerified
                WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
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
            const string sql = "DELETE FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        private static Attendance MapAttendance(SqlDataReader reader)
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
