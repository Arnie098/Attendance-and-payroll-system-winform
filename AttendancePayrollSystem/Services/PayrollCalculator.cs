using System;
using System.Collections.Generic;
using System.Linq;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public class PayrollCalculator
    {
        private readonly AttendanceRepository _attendanceRepo = new();
        private readonly LeaveRequestRepository _leaveRequestRepository = new();

        public Payroll CalculatePayroll(
            Employee employee,
            DateTime periodStart,
            DateTime periodEnd,
            decimal manualDeduction = 0m,
            string manualDeductionNote = "",
            PayrollDeductionOverrides? deductionOverrides = null)
        {
            var attendances = _attendanceRepo.GetAttendanceByEmployee(employee.EmployeeId, periodStart, periodEnd);
            var approvedPaidLeaveDates = _leaveRequestRepository.GetApprovedPaidLeaveDates(employee.EmployeeId, periodStart, periodEnd);
            var approvedAllLeaveDates = _leaveRequestRepository.GetApprovedLeaveDates(employee.EmployeeId, periodStart, periodEnd);

            decimal regularHours = 0;
            decimal overtimeHours = 0;
            int totalTardinessMinutes = 0;
            var attendanceDatesWithWorkedHours = new HashSet<DateTime>();
            decimal missedSessionHours = 0m;

            foreach (var attendance in attendances.Where(a => (a.TimeInAM.HasValue && a.TimeOutAM.HasValue) || (a.TimeInPM.HasValue && a.TimeOutPM.HasValue)))
            {
                var totalHours = (decimal)attendance.TotalHours;
                var regularDaily = DatabaseConfig.RegularHoursPerDay;
                attendanceDatesWithWorkedHours.Add(attendance.AttendanceDate.Date);

                if (totalHours <= regularDaily)
                {
                    regularHours += totalHours;
                }
                else
                {
                    regularHours += regularDaily;
                    overtimeHours += totalHours - regularDaily;
                }

                // Accumulate tardiness minutes
                totalTardinessMinutes += attendance.TardinessMinutes;

                if (attendance.TimeInAM.HasValue && attendance.TimeOutAM.HasValue &&
                    !attendance.TimeInPM.HasValue && !attendance.TimeOutPM.HasValue)
                {
                    missedSessionHours += GetScheduledSessionHours(DatabaseConfig.AfternoonStartTime, DatabaseConfig.AfternoonEndTime);
                }
                else if (!attendance.TimeInAM.HasValue && !attendance.TimeOutAM.HasValue &&
                         attendance.TimeInPM.HasValue && attendance.TimeOutPM.HasValue)
                {
                    missedSessionHours += GetScheduledSessionHours(DatabaseConfig.MorningStartTime, DatabaseConfig.MorningEndTime);
                }
            }

            foreach (var leaveDate in approvedPaidLeaveDates)
            {
                if (attendanceDatesWithWorkedHours.Contains(leaveDate))
                {
                    continue;
                }

                regularHours += DatabaseConfig.RegularHoursPerDay;
            }

            // Calculate absent days: expected working days minus days with work or approved leave
            var expectedWorkingDays = LeavePolicies.GetChargeableDates(periodStart, periodEnd);
            var coveredDays = new HashSet<DateTime>(attendanceDatesWithWorkedHours);
            foreach (var leaveDate in approvedAllLeaveDates)
            {
                coveredDays.Add(leaveDate);
            }

            int absentDays = 0;
            foreach (var workDay in expectedWorkingDays)
            {
                if (!coveredDays.Contains(workDay))
                {
                    absentDays++;
                }
            }

            regularHours = RoundHours(regularHours);
            overtimeHours = RoundHours(overtimeHours);

            var regularPay = RoundCurrency(regularHours * employee.HourlyRate);
            var overtimePay = RoundCurrency(overtimeHours * employee.HourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier);
            var grossPay = RoundCurrency(regularPay + overtimePay);

            // Calculate tardiness deduction: employee's per-minute rate * total tardiness minutes
            var minuteRate = employee.HourlyRate / 60m;
            var tardinessDeduction = RoundCurrency(totalTardinessMinutes * minuteRate);

            // Calculate absence deduction: daily rate * absent days
            var dailyRate = employee.HourlyRate * DatabaseConfig.RegularHoursPerDay;
            var absenceDeduction = RoundCurrency(absentDays * dailyRate);
            var missedSessionDeduction = RoundCurrency(missedSessionHours * employee.HourlyRate);

            var statutoryBreakdown = CalculateStatutoryDeductions(grossPay, deductionOverrides);
            var statutoryDeductions = RoundCurrency(
                statutoryBreakdown.Sss +
                statutoryBreakdown.PhilHealth +
                statutoryBreakdown.PagIbig +
                statutoryBreakdown.WithholdingTax);
            var manualDed = RoundCurrency(Math.Max(0m, manualDeduction));
            var totalDeductions = RoundCurrency(statutoryDeductions + tardinessDeduction + absenceDeduction + missedSessionDeduction + manualDed);
            var netPay = RoundCurrency(Math.Max(0m, grossPay - totalDeductions));

            return new Payroll
            {
                EmployeeId = employee.EmployeeId,
                PayPeriodStart = periodStart.Date,
                PayPeriodEnd = periodEnd.Date,
                RegularHours = regularHours,
                OvertimeHours = overtimeHours,
                GrossPay = grossPay,
                Deductions = totalDeductions,
                NetPay = netPay,
                Status = "Pending",
                SssDeduction = statutoryBreakdown.Sss,
                PhilHealthDeduction = statutoryBreakdown.PhilHealth,
                PagIbigDeduction = statutoryBreakdown.PagIbig,
                WithholdingTax = statutoryBreakdown.WithholdingTax,
                EmployeeName = employee.FullName,
                EmployeeCode = employee.EmployeeCode,
                TotalTardinessMinutes = totalTardinessMinutes,
                TardinessDeduction = tardinessDeduction,
                AbsentDays = absentDays,
                AbsenceDeduction = absenceDeduction,
                MissedSessionHours = RoundHours(missedSessionHours),
                MissedSessionDeduction = missedSessionDeduction,
                ManualDeduction = manualDed,
                ManualDeductionNote = manualDeductionNote ?? string.Empty
            };
        }

        private StatutoryDeductions CalculateStatutoryDeductions(decimal grossPay, PayrollDeductionOverrides? overrides)
        {
            var config = PayrollDeductionConfig.Current;
            return new StatutoryDeductions(
                Sss: RoundCurrency(Math.Max(0m, overrides?.SssDeduction ?? grossPay * config.SssRate)),
                PhilHealth: RoundCurrency(Math.Max(0m, overrides?.PhilHealthDeduction ?? grossPay * config.PhilHealthRate)),
                PagIbig: RoundCurrency(Math.Max(0m, overrides?.PagIbigDeduction ?? Math.Min(grossPay * config.PagIbigRate, config.PagIbigCap))),
                WithholdingTax: RoundCurrency(Math.Max(0m, overrides?.WithholdingTax ?? config.CalculateWithholdingTax(grossPay))));
        }

        private static decimal RoundHours(decimal hours)
        {
            return Math.Round(hours, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal GetScheduledSessionHours(TimeSpan start, TimeSpan end)
        {
            return (decimal)(end - start).TotalHours;
        }

        private static decimal RoundCurrency(decimal amount)
        {
            return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        private sealed record StatutoryDeductions(decimal Sss, decimal PhilHealth, decimal PagIbig, decimal WithholdingTax);
    }

    public sealed record PayrollDeductionOverrides(
        decimal? SssDeduction,
        decimal? PhilHealthDeduction,
        decimal? PagIbigDeduction,
        decimal? WithholdingTax);
}
