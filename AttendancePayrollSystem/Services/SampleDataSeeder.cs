using System;
using System.Collections.Generic;
using System.Linq;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public sealed class SampleDataSeeder
    {
        private readonly EmployeeRepository _employeeRepository = new();
        private readonly AttendanceRepository _attendanceRepository = new();
        private readonly PayrollRepository _payrollRepository = new();
        private const int DefaultMinimumAttendanceRecords = 30;
        private const int DefaultMinimumPayrollRecords = 30;

        public SampleSeedResult SeedAttendanceAndPayroll()
        {
            return SeedAttendanceAndPayroll(DefaultMinimumAttendanceRecords, DefaultMinimumPayrollRecords);
        }

        public SampleSeedResult SeedAttendanceAndPayroll(int minimumAttendanceRecords, int minimumPayrollRecords)
        {
            var employees = _employeeRepository.GetAllEmployees()
                .Where(employee => employee.IsActive)
                .ToList();

            if (employees.Count == 0)
            {
                return new SampleSeedResult(0, 0, 0, "No active employees found. Nothing was seeded.");
            }

            minimumAttendanceRecords = Math.Max(minimumAttendanceRecords, DefaultMinimumAttendanceRecords);
            minimumPayrollRecords = Math.Max(minimumPayrollRecords, DefaultMinimumPayrollRecords);

            var insertedAttendances = 0;
            var insertedPayrolls = 0;
            var attendanceDates = GetRecentBusinessDates(Math.Max(minimumAttendanceRecords, employees.Count * 10));
            var payrollPeriods = GetRecentPayrollPeriods(Math.Max(2, (int)Math.Ceiling(minimumPayrollRecords / (double)employees.Count)));

            foreach (var employee in employees)
            {
                var existingAttendances = _attendanceRepository
                    .GetAttendanceByEmployee(employee.EmployeeId, attendanceDates.Min(), attendanceDates.Max())
                    .ToDictionary(attendance => attendance.AttendanceDate.Date);

                foreach (var date in attendanceDates)
                {
                    if (insertedAttendances >= minimumAttendanceRecords)
                    {
                        break;
                    }

                    if (existingAttendances.ContainsKey(date))
                    {
                        continue;
                    }

                    _attendanceRepository.AddAttendance(BuildAttendance(employee, date));
                    insertedAttendances++;
                }

                foreach (var period in payrollPeriods)
                {
                    if (insertedPayrolls >= minimumPayrollRecords)
                    {
                        break;
                    }

                    if (_payrollRepository.GetPayrollByEmployeeAndPeriod(employee.EmployeeId, period.Start, period.End) != null)
                    {
                        continue;
                    }

                    _payrollRepository.AddPayroll(BuildPayroll(employee, period.Start, period.End));
                    insertedPayrolls++;
                }

                if (insertedAttendances >= minimumAttendanceRecords &&
                    insertedPayrolls >= minimumPayrollRecords)
                {
                    break;
                }
            }

            var message = $"Seeded {insertedAttendances} attendance record(s) and {insertedPayrolls} payroll record(s) for {employees.Count} active employee(s).";
            return new SampleSeedResult(employees.Count, insertedAttendances, insertedPayrolls, message);
        }

        private static Attendance BuildAttendance(Employee employee, DateTime attendanceDate)
        {
            var lateOffsetMinutes = employee.EmployeeId % 4 == 0 ? 18 : 0;
            var overtimeMinutes = employee.EmployeeId % 3 == 0 ? 50 : 10;
            var timeInAM = attendanceDate.AddHours(8).AddMinutes(lateOffsetMinutes);
            var timeOutAM = attendanceDate.AddHours(12);
            var timeInPM = attendanceDate.AddHours(13).AddMinutes(employee.EmployeeId % 2 == 0 ? 5 : 0);
            var timeOutPM = attendanceDate.AddHours(17).AddMinutes(overtimeMinutes);

            return new Attendance
            {
                EmployeeId = employee.EmployeeId,
                AttendanceDate = attendanceDate,
                TimeInAM = timeInAM,
                TimeOutAM = timeOutAM,
                TimeInPM = timeInPM,
                TimeOutPM = timeOutPM,
                Status = lateOffsetMinutes > 10 ? "Late" : "Present",
                IsBiometricVerified = true
            };
        }

        private static Payroll BuildPayroll(Employee employee, DateTime periodStart, DateTime periodEnd)
        {
            var totalWeekdays = Enumerable.Range(0, (periodEnd - periodStart).Days + 1)
                .Select(offset => periodStart.AddDays(offset))
                .Count(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);

            var regularHours = totalWeekdays * 8m;
            var overtimeHours = employee.EmployeeId % 3 == 0 ? 4m : 1.5m;
            var grossPay = Math.Round((regularHours + overtimeHours) * employee.HourlyRate, 2);
            var deductions = Math.Round(grossPay * 0.10m, 2);

            return new Payroll
            {
                EmployeeId = employee.EmployeeId,
                PayPeriodStart = periodStart,
                PayPeriodEnd = periodEnd,
                RegularHours = regularHours,
                OvertimeHours = overtimeHours,
                GrossPay = grossPay,
                Deductions = deductions,
                NetPay = grossPay - deductions,
                ManualDeduction = 0m,
                ManualDeductionNote = string.Empty,
                Status = periodEnd < DateTime.Today.Date.AddDays(-7) ? "Paid" : "Pending"
            };
        }

        private static List<DateTime> GetRecentBusinessDates(int businessDayCount)
        {
            var dates = new List<DateTime>();
            var cursor = DateTime.Today.Date;

            while (dates.Count < businessDayCount)
            {
                if (cursor.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    dates.Add(cursor);
                }

                cursor = cursor.AddDays(-1);
            }

            dates.Sort();
            return dates;
        }

        private static IReadOnlyList<(DateTime Start, DateTime End)> GetRecentPayrollPeriods(int periodCount)
        {
            var periods = new List<(DateTime Start, DateTime End)>();
            var end = DateTime.Today.Date.AddDays(-1);

            for (var i = 0; i < periodCount; i++)
            {
                var start = end.AddDays(-13);
                periods.Add((start, end));
                end = start.AddDays(-1);
            }

            periods.Reverse();
            return periods;
        }
    }

    public sealed record SampleSeedResult(
        int EmployeesProcessed,
        int AttendancesInserted,
        int PayrollsInserted,
        string Message);
}
