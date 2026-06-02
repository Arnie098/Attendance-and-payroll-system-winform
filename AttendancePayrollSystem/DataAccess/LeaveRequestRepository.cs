using System;
using System.Collections.Generic;
using System.Linq;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class LeaveRequestRepository
    {
        public List<LeaveRequest> GetLeaveRequestsByEmployee(int employeeId)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetLeaveRequestsByEmployeeViaApi(employeeId);
            }

            const string sql = @"
                SELECT
                    l.LeaveRequestId,
                    l.EmployeeId,
                    l.LeaveType,
                    l.IsPaid,
                    l.StartDate,
                    l.EndDate,
                    l.Reason,
                    l.Status,
                    l.ReviewerNotes,
                    l.ReviewedBy,
                    l.ReviewedAt,
                    l.CreatedAt,
                    l.UpdatedAt,
                    e.EmployeeCode,
                    e.FullName
                FROM LeaveRequests l
                INNER JOIN Employees e ON e.EmployeeId = l.EmployeeId
                WHERE l.EmployeeId = @EmployeeId
                ORDER BY l.CreatedAt DESC, l.StartDate DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            connection.Open();
            using var reader = command.ExecuteReader();

            var requests = new List<LeaveRequest>();
            while (reader.Read())
            {
                requests.Add(MapLeaveRequest(reader));
            }

            return requests;
        }

        public List<LeaveRequest> GetLeaveRequests()
        {
            if (SupabaseConfig.UseApi)
            {
                return GetLeaveRequestsViaApi();
            }

            const string sql = @"
                SELECT
                    l.LeaveRequestId,
                    l.EmployeeId,
                    l.LeaveType,
                    l.IsPaid,
                    l.StartDate,
                    l.EndDate,
                    l.Reason,
                    l.Status,
                    l.ReviewerNotes,
                    l.ReviewedBy,
                    l.ReviewedAt,
                    l.CreatedAt,
                    l.UpdatedAt,
                    e.EmployeeCode,
                    e.FullName
                FROM LeaveRequests l
                INNER JOIN Employees e ON e.EmployeeId = l.EmployeeId
                ORDER BY
                    CASE l.Status
                        WHEN 'Pending' THEN 0
                        WHEN 'Approved' THEN 1
                        WHEN 'Rejected' THEN 2
                        ELSE 3
                    END,
                    l.CreatedAt DESC,
                    l.StartDate DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            connection.Open();
            using var reader = command.ExecuteReader();

            var requests = new List<LeaveRequest>();
            while (reader.Read())
            {
                requests.Add(MapLeaveRequest(reader));
            }

            return requests;
        }

        public List<LeaveRequest> GetApprovedLeaveRequestsByEmployee(int employeeId, DateTime periodStart, DateTime periodEnd, bool paidOnly = false)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetApprovedLeaveRequestsByEmployeeViaApi(employeeId, periodStart, periodEnd, paidOnly);
            }

            var sql = @"
                SELECT
                    l.LeaveRequestId,
                    l.EmployeeId,
                    l.LeaveType,
                    l.IsPaid,
                    l.StartDate,
                    l.EndDate,
                    l.Reason,
                    l.Status,
                    l.ReviewerNotes,
                    l.ReviewedBy,
                    l.ReviewedAt,
                    l.CreatedAt,
                    l.UpdatedAt,
                    e.EmployeeCode,
                    e.FullName
                FROM LeaveRequests l
                INNER JOIN Employees e ON e.EmployeeId = l.EmployeeId
                WHERE l.EmployeeId = @EmployeeId
                  AND l.Status = @Status
                  AND l.StartDate <= @PeriodEnd
                  AND l.EndDate >= @PeriodStart";

            if (paidOnly)
            {
                sql += " AND l.IsPaid = TRUE";
            }

            sql += " ORDER BY l.StartDate ASC, l.LeaveRequestId ASC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@Status", LeavePolicies.StatusApproved);
            command.Parameters.AddWithValue("@PeriodStart", periodStart.Date);
            command.Parameters.AddWithValue("@PeriodEnd", periodEnd.Date);
            connection.Open();
            using var reader = command.ExecuteReader();

            var requests = new List<LeaveRequest>();
            while (reader.Read())
            {
                requests.Add(MapLeaveRequest(reader));
            }

            return requests;
        }

        public HashSet<DateTime> GetApprovedPaidLeaveDates(int employeeId, DateTime periodStart, DateTime periodEnd)
        {
            var dates = new HashSet<DateTime>();
            foreach (var request in GetApprovedLeaveRequestsByEmployee(employeeId, periodStart, periodEnd, paidOnly: true))
            {
                foreach (var date in LeavePolicies.GetChargeableDates(request.StartDate, request.EndDate)
                             .Where(date => date >= periodStart.Date && date <= periodEnd.Date))
                {
                    dates.Add(date);
                }
            }

            return dates;
        }

        public int SubmitLeaveRequest(LeaveRequest leaveRequest)
        {
            ArgumentNullException.ThrowIfNull(leaveRequest);
            ValidateLeaveRequest(leaveRequest);

            leaveRequest.LeaveType = leaveRequest.LeaveType.Trim();
            leaveRequest.Reason = leaveRequest.Reason.Trim();
            leaveRequest.IsPaid = LeavePolicies.IsPaidLeaveType(leaveRequest.LeaveType);
            leaveRequest.Status = LeavePolicies.StatusPending;

            if (SupabaseConfig.UseApi)
            {
                return SubmitLeaveRequestViaApi(leaveRequest);
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                EnsureNoOverlappingLeave(connection, transaction, leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate, null);

                using var command = new MySqlCommand(@"
                    INSERT INTO LeaveRequests
                    (EmployeeId, LeaveType, IsPaid, StartDate, EndDate, Reason, Status, ReviewerNotes, ReviewedBy, ReviewedAt)
                    VALUES
                    (@EmployeeId, @LeaveType, @IsPaid, @StartDate, @EndDate, @Reason, @Status, NULL, NULL, NULL)",
                    connection,
                    transaction);

                AddLeaveRequestParameters(command, leaveRequest);
                command.Parameters.AddWithValue("@Status", LeavePolicies.StatusPending);
                command.ExecuteNonQuery();

                var leaveRequestId = Convert.ToInt32(command.LastInsertedId);
                transaction.Commit();
                MySqlOfflineSyncService.QueueLeaveRequestUpsert(leaveRequestId, leaveRequest.EmployeeId);
                return leaveRequestId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void CancelLeaveRequest(int leaveRequestId)
        {
            if (SupabaseConfig.UseApi)
            {
                CancelLeaveRequestViaApi(leaveRequestId);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var leaveRequest = LoadLeaveRequest(connection, transaction, leaveRequestId)
                    ?? throw new InvalidOperationException("Leave request was not found.");

                if (!leaveRequest.CanEmployeeCancel)
                {
                    throw new InvalidOperationException("Only pending leave requests can be cancelled.");
                }

                using var command = new MySqlCommand(@"
                    UPDATE LeaveRequests
                    SET Status = @Status,
                        ReviewerNotes = NULL,
                        ReviewedBy = NULL,
                        ReviewedAt = NULL
                    WHERE LeaveRequestId = @LeaveRequestId",
                    connection,
                    transaction);

                command.Parameters.AddWithValue("@Status", LeavePolicies.StatusCancelled);
                command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
                command.ExecuteNonQuery();

                transaction.Commit();
                MySqlOfflineSyncService.QueueLeaveRequestUpsert(leaveRequestId, leaveRequest.EmployeeId);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void ApproveLeaveRequest(int leaveRequestId, string reviewer, string reviewerNotes)
        {
            if (SupabaseConfig.UseApi)
            {
                ApproveLeaveRequestViaApi(leaveRequestId, reviewer, reviewerNotes);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var leaveRequest = LoadLeaveRequest(connection, transaction, leaveRequestId)
                    ?? throw new InvalidOperationException("Leave request was not found.");

                EnsurePendingLeaveRequest(leaveRequest);
                EnsureNoOverlappingLeave(connection, transaction, leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate, leaveRequest.LeaveRequestId);
                EnsureNoAttendanceConflict(connection, transaction, leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate);

                var attendanceIds = ApplyLeaveAttendanceRecords(connection, transaction, leaveRequest);

                using var command = new MySqlCommand(@"
                    UPDATE LeaveRequests
                    SET Status = @Status,
                        ReviewerNotes = @ReviewerNotes,
                        ReviewedBy = @ReviewedBy,
                        ReviewedAt = @ReviewedAt
                    WHERE LeaveRequestId = @LeaveRequestId",
                    connection,
                    transaction);

                command.Parameters.AddWithValue("@Status", LeavePolicies.StatusApproved);
                command.Parameters.AddWithValue("@ReviewerNotes", ToDbValue(reviewerNotes));
                command.Parameters.AddWithValue("@ReviewedBy", reviewer.Trim());
                command.Parameters.AddWithValue("@ReviewedAt", DateTime.Now);
                command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
                command.ExecuteNonQuery();

                transaction.Commit();
                MySqlOfflineSyncService.QueueLeaveRequestUpsert(leaveRequestId, leaveRequest.EmployeeId);
                foreach (var attendanceId in attendanceIds)
                {
                    MySqlOfflineSyncService.QueueAttendanceUpsert(attendanceId, leaveRequest.EmployeeId);
                }
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void RejectLeaveRequest(int leaveRequestId, string reviewer, string reviewerNotes)
        {
            if (SupabaseConfig.UseApi)
            {
                RejectLeaveRequestViaApi(leaveRequestId, reviewer, reviewerNotes);
                return;
            }

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var leaveRequest = LoadLeaveRequest(connection, transaction, leaveRequestId)
                    ?? throw new InvalidOperationException("Leave request was not found.");

                EnsurePendingLeaveRequest(leaveRequest);

                using var command = new MySqlCommand(@"
                    UPDATE LeaveRequests
                    SET Status = @Status,
                        ReviewerNotes = @ReviewerNotes,
                        ReviewedBy = @ReviewedBy,
                        ReviewedAt = @ReviewedAt
                    WHERE LeaveRequestId = @LeaveRequestId",
                    connection,
                    transaction);

                command.Parameters.AddWithValue("@Status", LeavePolicies.StatusRejected);
                command.Parameters.AddWithValue("@ReviewerNotes", ToDbValue(reviewerNotes));
                command.Parameters.AddWithValue("@ReviewedBy", reviewer.Trim());
                command.Parameters.AddWithValue("@ReviewedAt", DateTime.Now);
                command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
                command.ExecuteNonQuery();

                transaction.Commit();
                MySqlOfflineSyncService.QueueLeaveRequestUpsert(leaveRequestId, leaveRequest.EmployeeId);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void ValidateLeaveRequest(LeaveRequest leaveRequest)
        {
            if (leaveRequest.EmployeeId <= 0)
            {
                throw new InvalidOperationException("Employee is required for the leave request.");
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.LeaveType))
            {
                throw new InvalidOperationException("Leave type is required.");
            }

            if (leaveRequest.EndDate.Date < leaveRequest.StartDate.Date)
            {
                throw new InvalidOperationException("Leave end date cannot be earlier than the start date.");
            }

            if (LeavePolicies.GetChargeableDayCount(leaveRequest.StartDate, leaveRequest.EndDate) == 0)
            {
                throw new InvalidOperationException("The selected leave range must include at least one weekday.");
            }

            if (string.IsNullOrWhiteSpace(leaveRequest.Reason))
            {
                throw new InvalidOperationException("Reason is required for the leave request.");
            }
        }

        private static void EnsurePendingLeaveRequest(LeaveRequest leaveRequest)
        {
            if (!string.Equals(leaveRequest.Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only pending leave requests can be reviewed.");
            }
        }

        private static void EnsureNoOverlappingLeave(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int employeeId,
            DateTime startDate,
            DateTime endDate,
            int? excludeLeaveRequestId)
        {
            using var command = new MySqlCommand(@"
                SELECT 1
                FROM LeaveRequests
                WHERE EmployeeId = @EmployeeId
                  AND Status IN (@PendingStatus, @ApprovedStatus)
                  AND StartDate <= @EndDate
                  AND EndDate >= @StartDate
                  AND (@ExcludeLeaveRequestId IS NULL OR LeaveRequestId <> @ExcludeLeaveRequestId)
                LIMIT 1",
                connection,
                transaction);

            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@PendingStatus", LeavePolicies.StatusPending);
            command.Parameters.AddWithValue("@ApprovedStatus", LeavePolicies.StatusApproved);
            command.Parameters.AddWithValue("@StartDate", startDate.Date);
            command.Parameters.AddWithValue("@EndDate", endDate.Date);
            command.Parameters.AddWithValue("@ExcludeLeaveRequestId", excludeLeaveRequestId.HasValue ? excludeLeaveRequestId.Value : DBNull.Value);

            if (command.ExecuteScalar() != null)
            {
                throw new InvalidOperationException("This leave request overlaps an existing pending or approved leave request.");
            }
        }

        private static void EnsureNoAttendanceConflict(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int employeeId,
            DateTime startDate,
            DateTime endDate)
        {
            var chargeableDates = LeavePolicies.GetChargeableDates(startDate, endDate).ToHashSet();
            if (chargeableDates.Count == 0)
            {
                return;
            }

            using var command = new MySqlCommand(@"
                SELECT AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status
                FROM AttendanceRecords
                WHERE EmployeeId = @EmployeeId
                  AND AttendanceDate >= @StartDate
                  AND AttendanceDate <= @EndDate",
                connection,
                transaction);

            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@StartDate", startDate.Date);
            command.Parameters.AddWithValue("@EndDate", endDate.Date);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var attendanceDate = Convert.ToDateTime(reader["AttendanceDate"]).Date;
                if (!chargeableDates.Contains(attendanceDate))
                {
                    continue;
                }

                var hasTime = reader["TimeInAM"] is not DBNull || reader["TimeOutAM"] is not DBNull || reader["TimeInPM"] is not DBNull || reader["TimeOutPM"] is not DBNull;
                var status = Convert.ToString(reader["Status"]) ?? string.Empty;
                if (hasTime || !LeavePolicies.IsLeaveAttendanceStatus(status))
                {
                    throw new InvalidOperationException($"Attendance already exists for {attendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                }
            }
        }

        private static List<int> ApplyLeaveAttendanceRecords(MySqlConnection connection, MySqlTransaction transaction, LeaveRequest leaveRequest)
        {
            var affectedAttendanceIds = new List<int>();
            var attendanceStatus = LeavePolicies.GetAttendanceStatus(leaveRequest.IsPaid);

            foreach (var attendanceDate in LeavePolicies.GetChargeableDates(leaveRequest.StartDate, leaveRequest.EndDate))
            {
                using var selectCommand = new MySqlCommand(@"
                    SELECT AttendanceId, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status
                    FROM AttendanceRecords
                    WHERE EmployeeId = @EmployeeId
                      AND AttendanceDate = @AttendanceDate
                    LIMIT 1",
                    connection,
                    transaction);

                selectCommand.Parameters.AddWithValue("@EmployeeId", leaveRequest.EmployeeId);
                selectCommand.Parameters.AddWithValue("@AttendanceDate", attendanceDate);

                using var reader = selectCommand.ExecuteReader();
                int? attendanceId = null;
                bool hasTime = false;
                string existingStatus = string.Empty;

                if (reader.Read())
                {
                    attendanceId = Convert.ToInt32(reader["AttendanceId"]);
                    hasTime = reader["TimeInAM"] is not DBNull || reader["TimeOutAM"] is not DBNull || reader["TimeInPM"] is not DBNull || reader["TimeOutPM"] is not DBNull;
                    existingStatus = Convert.ToString(reader["Status"]) ?? string.Empty;
                }

                reader.Close();

                if (hasTime)
                {
                    throw new InvalidOperationException($"Attendance already exists for {attendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                }

                if (attendanceId.HasValue)
                {
                    if (!string.IsNullOrWhiteSpace(existingStatus) && !LeavePolicies.IsLeaveAttendanceStatus(existingStatus))
                    {
                        throw new InvalidOperationException($"Attendance already exists for {attendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                    }

                    using var updateCommand = new MySqlCommand(@"
                        UPDATE AttendanceRecords
                        SET TimeInAM = NULL,
                            TimeOutAM = NULL,
                            TimeInPM = NULL,
                            TimeOutPM = NULL,
                            Status = @Status,
                            IsBiometricVerified = FALSE
                        WHERE AttendanceId = @AttendanceId",
                        connection,
                        transaction);

                    updateCommand.Parameters.AddWithValue("@Status", attendanceStatus);
                    updateCommand.Parameters.AddWithValue("@AttendanceId", attendanceId.Value);
                    updateCommand.ExecuteNonQuery();
                    affectedAttendanceIds.Add(attendanceId.Value);
                    continue;
                }

                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO AttendanceRecords (EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified)
                    VALUES (@EmployeeId, @AttendanceDate, NULL, NULL, NULL, NULL, @Status, FALSE)",
                    connection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@EmployeeId", leaveRequest.EmployeeId);
                insertCommand.Parameters.AddWithValue("@AttendanceDate", attendanceDate);
                insertCommand.Parameters.AddWithValue("@Status", attendanceStatus);
                insertCommand.ExecuteNonQuery();
                affectedAttendanceIds.Add(Convert.ToInt32(insertCommand.LastInsertedId));
            }

            return affectedAttendanceIds;
        }

        private static LeaveRequest? LoadLeaveRequest(MySqlConnection connection, MySqlTransaction transaction, int leaveRequestId)
        {
            using var command = new MySqlCommand(@"
                SELECT
                    l.LeaveRequestId,
                    l.EmployeeId,
                    l.LeaveType,
                    l.IsPaid,
                    l.StartDate,
                    l.EndDate,
                    l.Reason,
                    l.Status,
                    l.ReviewerNotes,
                    l.ReviewedBy,
                    l.ReviewedAt,
                    l.CreatedAt,
                    l.UpdatedAt,
                    e.EmployeeCode,
                    e.FullName
                FROM LeaveRequests l
                INNER JOIN Employees e ON e.EmployeeId = l.EmployeeId
                WHERE l.LeaveRequestId = @LeaveRequestId
                LIMIT 1",
                connection,
                transaction);

            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapLeaveRequest(reader) : null;
        }

        private static void AddLeaveRequestParameters(MySqlCommand command, LeaveRequest leaveRequest)
        {
            command.Parameters.AddWithValue("@EmployeeId", leaveRequest.EmployeeId);
            command.Parameters.AddWithValue("@LeaveType", leaveRequest.LeaveType.Trim());
            command.Parameters.AddWithValue("@IsPaid", leaveRequest.IsPaid);
            command.Parameters.AddWithValue("@StartDate", leaveRequest.StartDate.Date);
            command.Parameters.AddWithValue("@EndDate", leaveRequest.EndDate.Date);
            command.Parameters.AddWithValue("@Reason", leaveRequest.Reason.Trim());
        }

        private static object ToDbValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

        private static List<LeaveRequest> GetLeaveRequestsByEmployeeViaApi(int employeeId)
        {
            return SupabaseRestClient.GetList<ApiLeaveRequestRecord>(
                    "leaverequests",
                    new Dictionary<string, string>
                    {
                        ["select"] = "leaverequestid,employeeid,leavetype,ispaid,startdate,enddate,reason,status,reviewernotes,reviewedby,reviewedat,createdat,updatedat",
                        ["employeeid"] = $"eq.{employeeId}",
                        ["order"] = "createdat.desc,startdate.desc"
                    })
                .ConvertAll(MapApiLeaveRequest)
                .Pipe(AttachEmployeeDetails);
        }

        private static List<LeaveRequest> GetLeaveRequestsViaApi()
        {
            return SupabaseRestClient.GetList<ApiLeaveRequestRecord>(
                    "leaverequests",
                    new Dictionary<string, string>
                    {
                        ["select"] = "leaverequestid,employeeid,leavetype,ispaid,startdate,enddate,reason,status,reviewernotes,reviewedby,reviewedat,createdat,updatedat",
                        ["order"] = "createdat.desc,startdate.desc"
                    })
                .ConvertAll(MapApiLeaveRequest)
                .Pipe(AttachEmployeeDetails)
                .OrderBy(request => GetStatusSortOrder(request.Status))
                .ThenByDescending(request => request.CreatedAt)
                .ToList();
        }

        private static List<LeaveRequest> GetApprovedLeaveRequestsByEmployeeViaApi(int employeeId, DateTime periodStart, DateTime periodEnd, bool paidOnly)
        {
            var query = new Dictionary<string, string>
            {
                ["select"] = "leaverequestid,employeeid,leavetype,ispaid,startdate,enddate,reason,status,reviewernotes,reviewedby,reviewedat,createdat,updatedat",
                ["employeeid"] = $"eq.{employeeId}",
                ["status"] = $"eq.{LeavePolicies.StatusApproved}",
                ["and"] = $"(startdate.lte.{periodEnd:yyyy-MM-dd},enddate.gte.{periodStart:yyyy-MM-dd})",
                ["order"] = "startdate.asc,leaverequestid.asc"
            };

            if (paidOnly)
            {
                query["ispaid"] = "eq.true";
            }

            return SupabaseRestClient.GetList<ApiLeaveRequestRecord>("leaverequests", query)
                .ConvertAll(MapApiLeaveRequest)
                .Pipe(AttachEmployeeDetails);
        }

        private int SubmitLeaveRequestViaApi(LeaveRequest leaveRequest)
        {
            EnsureNoOverlappingLeaveViaApi(leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate, null);

            var created = SupabaseRestClient.InsertAndReturnSingle<ApiLeaveRequestRecord>(
                "leaverequests",
                new
                {
                    employeeid = leaveRequest.EmployeeId,
                    leavetype = leaveRequest.LeaveType.Trim(),
                    ispaid = leaveRequest.IsPaid,
                    startdate = leaveRequest.StartDate.Date,
                    enddate = leaveRequest.EndDate.Date,
                    reason = leaveRequest.Reason.Trim(),
                    status = LeavePolicies.StatusPending,
                    createdat = DateTime.UtcNow,
                    updatedat = DateTime.UtcNow
                });

            return created.LeaveRequestId;
        }

        private void CancelLeaveRequestViaApi(int leaveRequestId)
        {
            var leaveRequest = GetLeaveRequestByIdViaApi(leaveRequestId)
                ?? throw new InvalidOperationException("Leave request was not found.");

            if (!leaveRequest.CanEmployeeCancel)
            {
                throw new InvalidOperationException("Only pending leave requests can be cancelled.");
            }

            SupabaseRestClient.Update(
                "leaverequests",
                new
                {
                    status = LeavePolicies.StatusCancelled,
                    reviewernotes = (string?)null,
                    reviewedby = (string?)null,
                    reviewedat = (DateTime?)null,
                    updatedat = DateTime.UtcNow
                },
                BuildLeaveRequestIdFilter(leaveRequestId));
        }

        private void ApproveLeaveRequestViaApi(int leaveRequestId, string reviewer, string reviewerNotes)
        {
            var leaveRequest = GetLeaveRequestByIdViaApi(leaveRequestId)
                ?? throw new InvalidOperationException("Leave request was not found.");

            EnsurePendingLeaveRequest(leaveRequest);
            EnsureNoOverlappingLeaveViaApi(leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate, leaveRequest.LeaveRequestId);
            EnsureNoAttendanceConflictViaApi(leaveRequest.EmployeeId, leaveRequest.StartDate, leaveRequest.EndDate);

            foreach (var attendanceDate in LeavePolicies.GetChargeableDates(leaveRequest.StartDate, leaveRequest.EndDate))
            {
                UpsertLeaveAttendanceViaApi(leaveRequest.EmployeeId, attendanceDate, LeavePolicies.GetAttendanceStatus(leaveRequest.IsPaid));
            }

            SupabaseRestClient.Update(
                "leaverequests",
                new
                {
                    status = LeavePolicies.StatusApproved,
                    reviewernotes = string.IsNullOrWhiteSpace(reviewerNotes) ? null : reviewerNotes.Trim(),
                    reviewedby = reviewer.Trim(),
                    reviewedat = DateTime.UtcNow,
                    updatedat = DateTime.UtcNow
                },
                BuildLeaveRequestIdFilter(leaveRequestId));
        }

        private void RejectLeaveRequestViaApi(int leaveRequestId, string reviewer, string reviewerNotes)
        {
            var leaveRequest = GetLeaveRequestByIdViaApi(leaveRequestId)
                ?? throw new InvalidOperationException("Leave request was not found.");

            EnsurePendingLeaveRequest(leaveRequest);

            SupabaseRestClient.Update(
                "leaverequests",
                new
                {
                    status = LeavePolicies.StatusRejected,
                    reviewernotes = string.IsNullOrWhiteSpace(reviewerNotes) ? null : reviewerNotes.Trim(),
                    reviewedby = reviewer.Trim(),
                    reviewedat = DateTime.UtcNow,
                    updatedat = DateTime.UtcNow
                },
                BuildLeaveRequestIdFilter(leaveRequestId));
        }

        private static LeaveRequest? GetLeaveRequestByIdViaApi(int leaveRequestId)
        {
            var leaveRequest = SupabaseRestClient.GetSingleOrDefault<ApiLeaveRequestRecord>(
                "leaverequests",
                new Dictionary<string, string>
                {
                    ["select"] = "leaverequestid,employeeid,leavetype,ispaid,startdate,enddate,reason,status,reviewernotes,reviewedby,reviewedat,createdat,updatedat",
                    ["leaverequestid"] = $"eq.{leaveRequestId}",
                    ["limit"] = "1"
                });

            if (leaveRequest == null)
            {
                return null;
            }

            var mapped = MapApiLeaveRequest(leaveRequest);
            AttachEmployeeDetails(new List<LeaveRequest> { mapped });
            return mapped;
        }

        private static void EnsureNoOverlappingLeaveViaApi(int employeeId, DateTime startDate, DateTime endDate, int? excludeLeaveRequestId)
        {
            var query = new Dictionary<string, string>
            {
                ["select"] = "leaverequestid,employeeid,leavetype,ispaid,startdate,enddate,reason,status,reviewernotes,reviewedby,reviewedat,createdat,updatedat",
                ["employeeid"] = $"eq.{employeeId}",
                ["status"] = "in.(Pending,Approved)",
                ["and"] = $"(startdate.lte.{endDate:yyyy-MM-dd},enddate.gte.{startDate:yyyy-MM-dd})"
            };

            var overlaps = SupabaseRestClient.GetList<ApiLeaveRequestRecord>("leaverequests", query);
            if (excludeLeaveRequestId.HasValue)
            {
                overlaps = overlaps.Where(record => record.LeaveRequestId != excludeLeaveRequestId.Value).ToList();
            }

            if (overlaps.Count > 0)
            {
                throw new InvalidOperationException("This leave request overlaps an existing pending or approved leave request.");
            }
        }

        private static void EnsureNoAttendanceConflictViaApi(int employeeId, DateTime startDate, DateTime endDate)
        {
            var chargeableDates = LeavePolicies.GetChargeableDates(startDate, endDate).ToHashSet();
            if (chargeableDates.Count == 0)
            {
                return;
            }

            var attendanceRecords = new AttendanceRepository().GetAttendanceByEmployee(employeeId, startDate, endDate);
            foreach (var attendance in attendanceRecords)
            {
                if (!chargeableDates.Contains(attendance.AttendanceDate.Date))
                {
                    continue;
                }

                if (attendance.TimeInAM.HasValue || attendance.TimeOutAM.HasValue || attendance.TimeInPM.HasValue || attendance.TimeOutPM.HasValue || !LeavePolicies.IsLeaveAttendanceStatus(attendance.Status))
                {
                    throw new InvalidOperationException($"Attendance already exists for {attendance.AttendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                }
            }
        }

        private static void UpsertLeaveAttendanceViaApi(int employeeId, DateTime attendanceDate, string status)
        {
            var existing = SupabaseRestClient.GetSingleOrDefault<ApiAttendanceRecord>(
                "attendancerecords",
                new Dictionary<string, string>
                {
                    ["select"] = "attendanceid,employeeid,attendancedate,timeinam,timeoutam,timeinpm,timeoutpm,status,isbiometricverified",
                    ["employeeid"] = $"eq.{employeeId}",
                    ["attendancedate"] = $"eq.{attendanceDate:yyyy-MM-dd}",
                    ["limit"] = "1"
                });

            if (existing != null)
            {
                if (existing.TimeInAM.HasValue || existing.TimeOutAM.HasValue || existing.TimeInPM.HasValue || existing.TimeOutPM.HasValue)
                {
                    throw new InvalidOperationException($"Attendance already exists for {attendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                }

                if (!LeavePolicies.IsLeaveAttendanceStatus(existing.Status))
                {
                    throw new InvalidOperationException($"Attendance already exists for {attendanceDate:yyyy-MM-dd}. Resolve the attendance record before approving leave.");
                }

                SupabaseRestClient.Update(
                    "attendancerecords",
                    new
                    {
                        timeinam = (DateTime?)null,
                        timeoutam = (DateTime?)null,
                        timeinpm = (DateTime?)null,
                        timeoutpm = (DateTime?)null,
                        status,
                        isbiometricverified = false
                    },
                    new Dictionary<string, string>
                    {
                        ["attendanceid"] = $"eq.{existing.AttendanceId}"
                    });
                return;
            }

            SupabaseRestClient.InsertAndReturnSingle<ApiAttendanceRecord>(
                "attendancerecords",
                new
                {
                    employeeid = employeeId,
                    attendancedate = attendanceDate.Date,
                    timeinam = (DateTime?)null,
                    timeoutam = (DateTime?)null,
                    timeinpm = (DateTime?)null,
                    timeoutpm = (DateTime?)null,
                    status,
                    isbiometricverified = false
                });
        }

        private static Dictionary<string, string> BuildLeaveRequestIdFilter(int leaveRequestId) =>
            new()
            {
                ["leaverequestid"] = $"eq.{leaveRequestId}"
            };

        private static List<LeaveRequest> AttachEmployeeDetails(List<LeaveRequest> requests)
        {
            if (requests.Count == 0)
            {
                return requests;
            }

            var employeeIds = requests.Select(request => request.EmployeeId).Distinct().ToHashSet();
            var employees = new EmployeeRepository().GetAllEmployees()
                .Where(employee => employeeIds.Contains(employee.EmployeeId))
                .ToDictionary(employee => employee.EmployeeId);

            foreach (var request in requests)
            {
                if (!employees.TryGetValue(request.EmployeeId, out var employee))
                {
                    continue;
                }

                request.EmployeeCode = employee.EmployeeCode;
                request.EmployeeName = employee.FullName;
            }

            return requests;
        }

        private static int GetStatusSortOrder(string status)
        {
            if (string.Equals(status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(status, LeavePolicies.StatusApproved, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(status, LeavePolicies.StatusRejected, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private static LeaveRequest MapLeaveRequest(MySqlDataReader reader)
        {
            return new LeaveRequest
            {
                LeaveRequestId = Convert.ToInt32(reader["LeaveRequestId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                EmployeeName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                LeaveType = Convert.ToString(reader["LeaveType"]) ?? string.Empty,
                IsPaid = Convert.ToBoolean(reader["IsPaid"]),
                StartDate = Convert.ToDateTime(reader["StartDate"]),
                EndDate = Convert.ToDateTime(reader["EndDate"]),
                Reason = Convert.ToString(reader["Reason"]) ?? string.Empty,
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                ReviewerNotes = Convert.ToString(reader["ReviewerNotes"]) ?? string.Empty,
                ReviewedBy = Convert.ToString(reader["ReviewedBy"]) ?? string.Empty,
                ReviewedAt = reader["ReviewedAt"] is DBNull ? null : Convert.ToDateTime(reader["ReviewedAt"]),
                CreatedAt = reader["CreatedAt"] is DBNull ? DateTime.MinValue : Convert.ToDateTime(reader["CreatedAt"]),
                UpdatedAt = reader["UpdatedAt"] is DBNull ? DateTime.MinValue : Convert.ToDateTime(reader["UpdatedAt"])
            };
        }

        private static LeaveRequest MapApiLeaveRequest(ApiLeaveRequestRecord record)
        {
            return new LeaveRequest
            {
                LeaveRequestId = record.LeaveRequestId,
                EmployeeId = record.EmployeeId,
                LeaveType = record.LeaveType,
                IsPaid = record.IsPaid,
                StartDate = record.StartDate,
                EndDate = record.EndDate,
                Reason = record.Reason ?? string.Empty,
                Status = record.Status ?? string.Empty,
                ReviewerNotes = record.ReviewerNotes ?? string.Empty,
                ReviewedBy = record.ReviewedBy ?? string.Empty,
                ReviewedAt = record.ReviewedAt,
                CreatedAt = record.CreatedAt ?? DateTime.MinValue,
                UpdatedAt = record.UpdatedAt ?? DateTime.MinValue
            };
        }

        private sealed class ApiLeaveRequestRecord
        {
            public int LeaveRequestId { get; set; }
            public int EmployeeId { get; set; }
            public string LeaveType { get; set; } = string.Empty;
            public bool IsPaid { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public string? Reason { get; set; }
            public string? Status { get; set; }
            public string? ReviewerNotes { get; set; }
            public string? ReviewedBy { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private sealed class ApiAttendanceRecord
        {
            public int AttendanceId { get; set; }
            public int EmployeeId { get; set; }
            public DateTime AttendanceDate { get; set; }
            public DateTime? TimeInAM { get; set; }
            public DateTime? TimeOutAM { get; set; }
            public DateTime? TimeInPM { get; set; }
            public DateTime? TimeOutPM { get; set; }
            public string Status { get; set; } = string.Empty;
            public bool IsBiometricVerified { get; set; }
        }
    }

    internal static class LeaveRequestRepositoryExtensions
    {
        public static T Pipe<T>(this T value, Func<T, T> transform) => transform(value);
    }
}
