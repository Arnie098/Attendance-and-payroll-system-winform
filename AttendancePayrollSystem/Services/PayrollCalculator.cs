using System;
using System.Linq;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public class PayrollCalculator
    {
        private readonly AttendanceRepository _attendanceRepo = new();

        public Payroll CalculatePayroll(Employee employee, DateTime periodStart, DateTime periodEnd)
        {
            var attendances = _attendanceRepo.GetAttendanceByEmployee(employee.EmployeeId, periodStart, periodEnd);

            decimal regularHours = 0;
            decimal overtimeHours = 0;

            foreach (var attendance in attendances.Where(a => a.TimeIn.HasValue && a.TimeOut.HasValue))
            {
                var totalHours = (decimal)attendance.TotalHours;
                var regularDaily = DatabaseConfig.RegularHoursPerDay;

                if (totalHours <= regularDaily)
                {
                    regularHours += totalHours;
                }
                else
                {
                    regularHours += regularDaily;
                    overtimeHours += totalHours - regularDaily;
                }
            }

            var regularPay = regularHours * employee.HourlyRate;
            var overtimePay = overtimeHours * employee.HourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier;
            var grossPay = regularPay + overtimePay;

            var deductions = CalculateDeductions(grossPay);
            var netPay = grossPay - deductions;

            return new Payroll
            {
                EmployeeId = employee.EmployeeId,
                PayPeriodStart = periodStart.Date,
                PayPeriodEnd = periodEnd.Date,
                RegularHours = regularHours,
                OvertimeHours = overtimeHours,
                GrossPay = grossPay,
                Deductions = deductions,
                NetPay = netPay,
                Status = "Pending",
                EmployeeName = employee.FullName,
                EmployeeCode = employee.EmployeeCode
            };
        }

        private decimal CalculateDeductions(decimal grossPay)
        {
            var sssContribution = grossPay * 0.045m;
            var philHealthContribution = grossPay * 0.02m;
            var pagIbigContribution = Math.Min(grossPay * 0.02m, 100m);
            var withholdingTax = CalculateWithholdingTax(grossPay);

            return sssContribution + philHealthContribution + pagIbigContribution + withholdingTax;
        }

        private decimal CalculateWithholdingTax(decimal grossPay)
        {
            if (grossPay <= 10417) return 0;
            if (grossPay <= 16666) return (grossPay - 10417) * 0.15m;
            if (grossPay <= 33332) return 937.50m + (grossPay - 16666) * 0.20m;
            if (grossPay <= 83332) return 4270.70m + (grossPay - 33332) * 0.25m;
            if (grossPay <= 333332) return 16770.70m + (grossPay - 83332) * 0.30m;
            return 91770.70m + (grossPay - 333332) * 0.35m;
        }
    }
}
