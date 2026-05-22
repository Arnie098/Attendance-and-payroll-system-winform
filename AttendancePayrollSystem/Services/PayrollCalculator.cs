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

        public Payroll CalculatePayroll(Employee employee, DateTime periodStart, DateTime periodEnd)
        {
            var attendances = _attendanceRepo.GetAttendanceByEmployee(employee.EmployeeId, periodStart, periodEnd);
            var approvedPaidLeaveDates = _leaveRequestRepository.GetApprovedPaidLeaveDates(employee.EmployeeId, periodStart, periodEnd);

            decimal regularHours = 0;
            decimal overtimeHours = 0;
            int totalTardinessMinutes = 0;
            var attendanceDatesWithWorkedHours = new HashSet<DateTime>();

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
            }

            foreach (var leaveDate in approvedPaidLeaveDates)
            {
                if (attendanceDatesWithWorkedHours.Contains(leaveDate))
                {
                    continue;
                }

                regularHours += DatabaseConfig.RegularHoursPerDay;
            }

            regularHours = RoundHours(regularHours);
            overtimeHours = RoundHours(overtimeHours);

            var regularPay = RoundCurrency(regularHours * employee.HourlyRate);
            var overtimePay = RoundCurrency(overtimeHours * employee.HourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier);
            var grossPay = RoundCurrency(regularPay + overtimePay);

            // Calculate tardiness deduction: employee's per-minute rate * total tardiness minutes
            var minuteRate = employee.HourlyRate / 60m;
            var tardinessDeduction = RoundCurrency(totalTardinessMinutes * minuteRate);

            // Total deductions = statutory + tardiness
            var statutoryDeductions = RoundCurrency(Math.Min(grossPay, CalculateDeductions(grossPay)));
            var totalDeductions = RoundCurrency(statutoryDeductions + tardinessDeduction);
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
                EmployeeName = employee.FullName,
                EmployeeCode = employee.EmployeeCode,
                TotalTardinessMinutes = totalTardinessMinutes,
                TardinessDeduction = tardinessDeduction
            };
        }

        private decimal CalculateDeductions(decimal grossPay)
        {
            return PayrollDeductionConfig.Current.CalculateDeductions(grossPay);
        }

        private decimal CalculateWithholdingTax(decimal grossPay)
        {
            return PayrollDeductionConfig.Current.CalculateWithholdingTax(grossPay);
        }

        private static decimal RoundHours(decimal hours)
        {
            return Math.Round(hours, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal RoundCurrency(decimal amount)
        {
            return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        }
    }
}
