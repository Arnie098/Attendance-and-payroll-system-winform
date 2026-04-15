using System;
using System.Collections.Generic;
using System.Linq;

namespace AttendancePayrollSystem.Services
{
    public static class LeavePolicies
    {
        public const string StatusPending = "Pending";
        public const string StatusApproved = "Approved";
        public const string StatusRejected = "Rejected";
        public const string StatusCancelled = "Cancelled";

        public const string AttendanceStatusOnLeave = "On Leave";
        public const string AttendanceStatusLeaveWithoutPay = "Leave Without Pay";

        public static readonly string[] DefaultLeaveTypes =
        [
            "Vacation Leave",
            "Sick Leave",
            "Emergency Leave",
            "Maternity Leave",
            "Paternity Leave",
            "Unpaid Leave"
        ];

        public static bool IsPaidLeaveType(string? leaveType)
        {
            if (string.IsNullOrWhiteSpace(leaveType))
            {
                return false;
            }

            return !string.Equals(leaveType.Trim(), "Unpaid Leave", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetAttendanceStatus(bool isPaidLeave) =>
            isPaidLeave ? AttendanceStatusOnLeave : AttendanceStatusLeaveWithoutPay;

        public static bool IsLeaveAttendanceStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return string.Equals(status, AttendanceStatusOnLeave, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, AttendanceStatusLeaveWithoutPay, StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<DateTime> GetChargeableDates(DateTime startDate, DateTime endDate)
        {
            if (endDate.Date < startDate.Date)
            {
                return Array.Empty<DateTime>();
            }

            var dates = new List<DateTime>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                dates.Add(date);
            }

            return dates;
        }

        public static int GetChargeableDayCount(DateTime startDate, DateTime endDate) =>
            GetChargeableDates(startDate, endDate).Count;

        public static bool IsTerminalStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return string.Equals(status, StatusApproved, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, StatusRejected, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, StatusCancelled, StringComparison.OrdinalIgnoreCase);
        }
    }
}
