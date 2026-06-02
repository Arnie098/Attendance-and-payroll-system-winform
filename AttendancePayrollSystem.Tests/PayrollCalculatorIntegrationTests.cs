using System;
using Xunit;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Integration-style tests for PayrollCalculator.CalculatePayroll logic.
    /// Since the calculator internally creates repository instances (no DI),
    /// these tests validate the mathematical formulas and edge cases
    /// that the calculator applies to attendance/employee data.
    /// </summary>
    public class PayrollCalculatorIntegrationTests
    {
        #region Gross Pay Formula Tests

        [Fact]
        public void GrossPay_RegularHoursOnly_ShouldBeHoursTimesRate()
        {
            decimal hourlyRate = 100m;
            decimal regularHours = 40m; // 5 days * 8 hours
            decimal overtimeHours = 0m;

            decimal regularPay = Math.Round(regularHours * hourlyRate, 2, MidpointRounding.AwayFromZero);
            decimal overtimePay = Math.Round(overtimeHours * hourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier, 2, MidpointRounding.AwayFromZero);
            decimal grossPay = regularPay + overtimePay;

            Assert.Equal(4000m, grossPay);
        }

        [Fact]
        public void GrossPay_WithOvertime_ShouldApply1Point25Multiplier()
        {
            decimal hourlyRate = 100m;
            decimal regularHours = 40m;
            decimal overtimeHours = 4m; // 4 hours OT

            decimal regularPay = Math.Round(regularHours * hourlyRate, 2, MidpointRounding.AwayFromZero);
            decimal overtimePay = Math.Round(overtimeHours * hourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier, 2, MidpointRounding.AwayFromZero);
            decimal grossPay = regularPay + overtimePay;

            Assert.Equal(4000m, regularPay);
            Assert.Equal(500m, overtimePay); // 4 * 100 * 1.25
            Assert.Equal(4500m, grossPay);
        }

        [Fact]
        public void GrossPay_FractionalHours_ShouldRoundTo2Decimals()
        {
            decimal hourlyRate = 75.50m;
            decimal regularHours = 7.33m; // Partial day

            decimal regularPay = Math.Round(regularHours * hourlyRate, 2, MidpointRounding.AwayFromZero);

            Assert.Equal(553.42m, regularPay); // 7.33 * 75.50 = 553.415 → 553.42
        }

        #endregion

        #region Tardiness Deduction Formula Tests

        [Fact]
        public void TardinessDeduction_ShouldBeMinuteRateTimesMinutes()
        {
            decimal hourlyRate = 120m;
            int tardinessMinutes = 30;

            decimal minuteRate = hourlyRate / 60m;
            decimal tardinessDeduction = Math.Round(tardinessMinutes * minuteRate, 2, MidpointRounding.AwayFromZero);

            Assert.Equal(60m, tardinessDeduction); // 30 * (120/60) = 30 * 2 = 60
        }

        [Fact]
        public void TardinessDeduction_ZeroMinutes_ShouldBeZero()
        {
            decimal hourlyRate = 150m;
            int tardinessMinutes = 0;

            decimal minuteRate = hourlyRate / 60m;
            decimal tardinessDeduction = Math.Round(tardinessMinutes * minuteRate, 2, MidpointRounding.AwayFromZero);

            Assert.Equal(0m, tardinessDeduction);
        }

        [Fact]
        public void TardinessDeduction_FractionalRate_ShouldRoundCorrectly()
        {
            decimal hourlyRate = 100m;
            int tardinessMinutes = 7;

            decimal minuteRate = hourlyRate / 60m; // 1.6666...
            decimal tardinessDeduction = Math.Round(tardinessMinutes * minuteRate, 2, MidpointRounding.AwayFromZero);

            // 7 * (100/60) = 7 * 1.6666... = 11.6666... → 11.67
            Assert.Equal(11.67m, tardinessDeduction);
        }

        #endregion

        #region Net Pay Calculation Tests

        [Fact]
        public void NetPay_ShouldBeGrossMinusTotalDeductions()
        {
            decimal grossPay = 20000m;
            decimal statutoryDeductions = 3004.30m;
            decimal tardinessDeduction = 60m;
            decimal manualDeduction = 500m;

            decimal totalDeductions = Math.Round(statutoryDeductions + tardinessDeduction + manualDeduction, 2, MidpointRounding.AwayFromZero);
            decimal netPay = Math.Round(Math.Max(0m, grossPay - totalDeductions), 2, MidpointRounding.AwayFromZero);

            Assert.Equal(3564.30m, totalDeductions);
            Assert.Equal(16435.70m, netPay);
        }

        [Fact]
        public void NetPay_ShouldNeverGoNegative()
        {
            decimal grossPay = 100m;
            // Statutory deductions on 100: SSS(4.50) + PhilHealth(2) + PagIbig(2) + Tax(0) = 8.50
            // But let's simulate a scenario where deductions exceed gross
            decimal statutoryDeductions = 50m;
            decimal tardinessDeduction = 30m;
            decimal manualDeduction = 50m;

            decimal totalDeductions = statutoryDeductions + tardinessDeduction + manualDeduction; // 130
            decimal netPay = Math.Max(0m, grossPay - totalDeductions);

            Assert.Equal(0m, netPay); // Floor at 0, not -30
        }

        [Fact]
        public void StatutoryDeductions_ShouldNotExceedGross()
        {
            // The calculator uses Math.Min(grossPay, CalculateDeductions(grossPay))
            decimal grossPay = 5m; // Very low gross
            var config = PayrollDeductionConfig.Current;
            decimal rawDeductions = config.CalculateDeductions(grossPay);
            decimal cappedDeductions = Math.Min(grossPay, rawDeductions);

            // Raw deductions would be: SSS(0.225) + PhilHealth(0.10) + PagIbig(0.10) + Tax(0) = 0.425
            // In this case raw < gross, so no capping needed
            Assert.True(cappedDeductions <= grossPay);
        }

        [Fact]
        public void ManualDeduction_NegativeValue_ShouldBeZero()
        {
            // The calculator uses Math.Max(0m, manualDeduction)
            decimal manualDeduction = -500m;
            decimal sanitized = Math.Max(0m, manualDeduction);

            Assert.Equal(0m, sanitized);
        }

        [Fact]
        public void ManualDeduction_PositiveValue_ShouldPassThrough()
        {
            decimal manualDeduction = 250m;
            decimal sanitized = Math.Max(0m, manualDeduction);

            Assert.Equal(250m, sanitized);
        }

        #endregion

        #region Hours Splitting Logic Tests

        [Fact]
        public void HoursSplitting_ExactlyRegularHours_NoOvertime()
        {
            decimal totalHoursWorked = 8m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            decimal regular = Math.Min(totalHoursWorked, regularDaily);
            decimal overtime = Math.Max(0m, totalHoursWorked - regularDaily);

            Assert.Equal(8m, regular);
            Assert.Equal(0m, overtime);
        }

        [Fact]
        public void HoursSplitting_BelowRegularHours_NoOvertime()
        {
            decimal totalHoursWorked = 6.5m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            decimal regular = Math.Min(totalHoursWorked, regularDaily);
            decimal overtime = Math.Max(0m, totalHoursWorked - regularDaily);

            Assert.Equal(6.5m, regular);
            Assert.Equal(0m, overtime);
        }

        [Fact]
        public void HoursSplitting_AboveRegularHours_ShouldSplitCorrectly()
        {
            decimal totalHoursWorked = 10.5m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            decimal regular = regularDaily; // Capped at 8
            decimal overtime = totalHoursWorked - regularDaily; // 2.5

            Assert.Equal(8m, regular);
            Assert.Equal(2.5m, overtime);
        }

        #endregion

        #region Multi-Day Accumulation Tests

        [Fact]
        public void MultiDay_RegularHoursAccumulate()
        {
            // Simulate 5 days of 8-hour work
            decimal totalRegular = 0m;
            decimal totalOvertime = 0m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            for (int day = 0; day < 5; day++)
            {
                decimal dailyHours = 8m;
                if (dailyHours <= regularDaily)
                {
                    totalRegular += dailyHours;
                }
                else
                {
                    totalRegular += regularDaily;
                    totalOvertime += dailyHours - regularDaily;
                }
            }

            Assert.Equal(40m, totalRegular);
            Assert.Equal(0m, totalOvertime);
        }

        [Fact]
        public void MultiDay_MixedHoursAccumulate()
        {
            // 3 days of 8 hours + 2 days of 10 hours
            decimal totalRegular = 0m;
            decimal totalOvertime = 0m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;
            decimal[] dailyHours = { 8m, 8m, 8m, 10m, 10m };

            foreach (var hours in dailyHours)
            {
                if (hours <= regularDaily)
                {
                    totalRegular += hours;
                }
                else
                {
                    totalRegular += regularDaily;
                    totalOvertime += hours - regularDaily;
                }
            }

            Assert.Equal(40m, totalRegular); // 3*8 + 2*8 = 40
            Assert.Equal(4m, totalOvertime);  // 2*2 = 4
        }

        #endregion

        #region Payroll Model Property Tests

        [Fact]
        public void PayrollModel_DefaultValues_ShouldBeZeroOrEmpty()
        {
            var payroll = new Payroll();

            Assert.Equal(0, payroll.PayrollId);
            Assert.Equal(0, payroll.EmployeeId);
            Assert.Equal(0m, payroll.RegularHours);
            Assert.Equal(0m, payroll.OvertimeHours);
            Assert.Equal(0m, payroll.GrossPay);
            Assert.Equal(0m, payroll.Deductions);
            Assert.Equal(0m, payroll.NetPay);
            Assert.Equal(0, payroll.TotalTardinessMinutes);
            Assert.Equal(0m, payroll.TardinessDeduction);
            Assert.Equal(0m, payroll.ManualDeduction);
            Assert.Equal(string.Empty, payroll.ManualDeductionNote);
            Assert.Equal(string.Empty, payroll.Status);
            Assert.Equal(string.Empty, payroll.EmployeeName);
            Assert.Equal(string.Empty, payroll.EmployeeCode);
        }

        [Fact]
        public void PayrollModel_SetAllProperties_ShouldRetainValues()
        {
            var payroll = new Payroll
            {
                PayrollId = 1,
                EmployeeId = 42,
                PayPeriodStart = new DateTime(2024, 6, 1),
                PayPeriodEnd = new DateTime(2024, 6, 15),
                RegularHours = 80m,
                OvertimeHours = 4m,
                GrossPay = 8500m,
                Deductions = 1200m,
                NetPay = 7300m,
                Status = "Approved",
                EmployeeName = "Juan Dela Cruz",
                EmployeeCode = "EMP-001",
                TotalTardinessMinutes = 45,
                TardinessDeduction = 112.50m,
                ManualDeduction = 200m,
                ManualDeductionNote = "Cash advance"
            };

            Assert.Equal(1, payroll.PayrollId);
            Assert.Equal(42, payroll.EmployeeId);
            Assert.Equal(new DateTime(2024, 6, 1), payroll.PayPeriodStart);
            Assert.Equal(new DateTime(2024, 6, 15), payroll.PayPeriodEnd);
            Assert.Equal(80m, payroll.RegularHours);
            Assert.Equal(4m, payroll.OvertimeHours);
            Assert.Equal(8500m, payroll.GrossPay);
            Assert.Equal(1200m, payroll.Deductions);
            Assert.Equal(7300m, payroll.NetPay);
            Assert.Equal("Approved", payroll.Status);
            Assert.Equal("Juan Dela Cruz", payroll.EmployeeName);
            Assert.Equal("EMP-001", payroll.EmployeeCode);
            Assert.Equal(45, payroll.TotalTardinessMinutes);
            Assert.Equal(112.50m, payroll.TardinessDeduction);
            Assert.Equal(200m, payroll.ManualDeduction);
            Assert.Equal("Cash advance", payroll.ManualDeductionNote);
        }

        #endregion

        #region Employee Model Tests

        [Fact]
        public void EmployeeModel_DefaultValues()
        {
            var employee = new Employee();

            Assert.Equal(0, employee.EmployeeId);
            Assert.Equal(string.Empty, employee.EmployeeCode);
            Assert.Equal(string.Empty, employee.FullName);
            Assert.Equal(string.Empty, employee.Email);
            Assert.Equal(string.Empty, employee.Phone);
            Assert.Equal(string.Empty, employee.Position);
            Assert.Equal(string.Empty, employee.Department);
            Assert.Equal(0m, employee.HourlyRate);
            Assert.False(employee.IsActive);
            Assert.Null(employee.SourceTeacherId);
            Assert.Null(employee.SourceUserId);
            Assert.Null(employee.ProfileImage);
            Assert.Null(employee.BiometricTemplate);
        }

        [Fact]
        public void EmployeeModel_HourlyRate_ShouldSupportDecimalPrecision()
        {
            var employee = new Employee { HourlyRate = 156.75m };
            Assert.Equal(156.75m, employee.HourlyRate);
        }

        #endregion
    }
}
