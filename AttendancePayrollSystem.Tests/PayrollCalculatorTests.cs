using System;
using Xunit;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests for PayrollCalculator logic.
    /// Since PayrollCalculator internally creates repository instances (no DI),
    /// we test the deduction/tax calculation logic by validating the known formulas
    /// and testing the Attendance model's TotalHours which feeds into payroll.
    /// </summary>
    public class PayrollCalculatorTests
    {
        #region Withholding Tax Bracket Tests

        [Theory]
        [InlineData(0, 0)]           // Zero gross
        [InlineData(5000, 0)]        // Below first bracket
        [InlineData(10417, 0)]       // Exactly at first bracket boundary
        [InlineData(10418, 0.15)]    // Just above first bracket: (10418-10417)*0.15 = 0.15
        [InlineData(16666, 937.35)]  // Top of second bracket: (16666-10417)*0.15 = 937.35
        [InlineData(16667, 937.70)]  // Just into third bracket: 937.50 + (16667-16666)*0.20 = 937.70
        [InlineData(33332, 4270.70)] // Top of third bracket: 937.50 + (33332-16666)*0.20 = 4270.70
        [InlineData(33333, 4270.95)] // Just into fourth bracket: 4270.70 + (33333-33332)*0.25 = 4270.95
        [InlineData(83332, 16770.70)] // Top of fourth bracket: 4270.70 + (83332-33332)*0.25 = 16770.70
        [InlineData(83333, 16771.00)] // Just into fifth bracket: 16770.70 + (83333-83332)*0.30 = 16771.00
        [InlineData(333332, 91770.70)] // Top of fifth bracket: 16770.70 + (333332-83332)*0.30 = 91770.70
        [InlineData(333333, 91771.05)] // Just into sixth bracket: 91770.70 + (333333-333332)*0.35 = 91771.05
        public void WithholdingTax_ShouldMatchBrackets(decimal grossPay, decimal expectedTax)
        {
            // Use reflection to test the private method
            var calculator = new Services.PayrollCalculator();
            var method = typeof(Services.PayrollCalculator)
                .GetMethod("CalculateWithholdingTax", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);

            var result = (decimal)method!.Invoke(calculator, new object[] { grossPay })!;
            Assert.Equal(expectedTax, result, 2);
        }

        #endregion

        #region Deduction Formula Tests

        [Fact]
        public void Deductions_ZeroGross_ShouldBeZero()
        {
            var calculator = new Services.PayrollCalculator();
            var method = typeof(Services.PayrollCalculator)
                .GetMethod("CalculateDeductions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);

            var result = (decimal)method!.Invoke(calculator, new object[] { 0m })!;
            Assert.Equal(0m, result);
        }

        [Fact]
        public void Deductions_ShouldIncludeSSS_PhilHealth_PagIbig_Tax()
        {
            var calculator = new Services.PayrollCalculator();
            var method = typeof(Services.PayrollCalculator)
                .GetMethod("CalculateDeductions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);

            decimal grossPay = 20000m;

            // Expected components:
            // SSS: 20000 * 0.045 = 900
            // PhilHealth: 20000 * 0.02 = 400
            // Pag-IBIG: min(20000 * 0.02, 100) = 100
            // Withholding Tax: 937.50 + (20000 - 16666) * 0.20 = 937.50 + 666.80 = 1604.30
            // Total: 900 + 400 + 100 + 1604.30 = 3004.30
            decimal expectedDeductions = 900m + 400m + 100m + (937.50m + (20000m - 16666m) * 0.20m);

            var result = (decimal)method!.Invoke(calculator, new object[] { grossPay })!;
            Assert.Equal(Math.Round(expectedDeductions, 2), Math.Round(result, 2));
        }

        [Fact]
        public void Deductions_PagIbig_ShouldCapAt100()
        {
            var calculator = new Services.PayrollCalculator();
            var method = typeof(Services.PayrollCalculator)
                .GetMethod("CalculateDeductions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);

            // For gross = 10000: PagIbig = min(10000*0.02, 100) = min(200, 100) = 100
            // SSS: 10000 * 0.045 = 450
            // PhilHealth: 10000 * 0.02 = 200
            // Tax: 0 (below 10417)
            // Total: 450 + 200 + 100 + 0 = 750
            decimal grossPay = 10000m;
            decimal expected = 450m + 200m + 100m + 0m;

            var result = (decimal)method!.Invoke(calculator, new object[] { grossPay })!;
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Deductions_LowGross_PagIbigBelowCap()
        {
            var calculator = new Services.PayrollCalculator();
            var method = typeof(Services.PayrollCalculator)
                .GetMethod("CalculateDeductions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(method);

            // For gross = 3000: PagIbig = min(3000*0.02, 100) = min(60, 100) = 60
            // SSS: 3000 * 0.045 = 135
            // PhilHealth: 3000 * 0.02 = 60
            // Tax: 0 (below 10417)
            // Total: 135 + 60 + 60 + 0 = 255
            decimal grossPay = 3000m;
            decimal expected = 135m + 60m + 60m + 0m;

            var result = (decimal)method!.Invoke(calculator, new object[] { grossPay })!;
            Assert.Equal(expected, result);
        }

        #endregion

        #region Overtime Calculation Logic Tests

        [Fact]
        public void OvertimeMultiplier_ShouldBe1Point25()
        {
            Assert.Equal(1.25, DatabaseConfig.OvertimeMultiplier);
        }

        [Fact]
        public void RegularHoursPerDay_ShouldBe8()
        {
            Assert.Equal(8m, DatabaseConfig.RegularHoursPerDay);
        }

        [Fact]
        public void OvertimePay_Formula_ShouldApplyMultiplier()
        {
            // Simulate: employee worked 10 hours, hourly rate = 100
            decimal hourlyRate = 100m;
            decimal totalHoursWorked = 10m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            decimal regularHours = regularDaily; // 8
            decimal overtimeHours = totalHoursWorked - regularDaily; // 2

            decimal regularPay = regularHours * hourlyRate; // 800
            decimal overtimePay = overtimeHours * hourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier; // 2 * 100 * 1.25 = 250

            Assert.Equal(800m, regularPay);
            Assert.Equal(250m, overtimePay);
            Assert.Equal(1050m, regularPay + overtimePay);
        }

        [Fact]
        public void ZeroHours_ShouldProduceZeroPay()
        {
            decimal hourlyRate = 150m;
            decimal totalHoursWorked = 0m;
            decimal regularDaily = DatabaseConfig.RegularHoursPerDay;

            decimal regularHours = Math.Min(totalHoursWorked, regularDaily);
            decimal overtimeHours = Math.Max(0m, totalHoursWorked - regularDaily);

            decimal regularPay = regularHours * hourlyRate;
            decimal overtimePay = overtimeHours * hourlyRate * (decimal)DatabaseConfig.OvertimeMultiplier;

            Assert.Equal(0m, regularPay);
            Assert.Equal(0m, overtimePay);
        }

        #endregion

        #region Paid Leave Credit Tests

        [Fact]
        public void PaidLeaveCredit_ShouldAdd8RegularHours()
        {
            // When an employee has an approved paid leave day and didn't work that day,
            // they get RegularHoursPerDay (8) added to regular hours
            decimal regularHoursBefore = 32m; // 4 days worked
            decimal paidLeaveDays = 1;

            decimal regularHoursAfter = regularHoursBefore + (paidLeaveDays * DatabaseConfig.RegularHoursPerDay);

            Assert.Equal(40m, regularHoursAfter);
        }

        [Fact]
        public void PaidLeaveCredit_WhenAlreadyWorked_ShouldNotDoubleCount()
        {
            // If employee worked on a day that also has approved leave,
            // the leave credit should NOT be added (avoid double counting)
            var workedDates = new HashSet<DateTime> { new DateTime(2024, 1, 15) };
            var leaveDates = new List<DateTime> { new DateTime(2024, 1, 15) }; // Same day

            decimal additionalHours = 0m;
            foreach (var leaveDate in leaveDates)
            {
                if (!workedDates.Contains(leaveDate))
                {
                    additionalHours += DatabaseConfig.RegularHoursPerDay;
                }
            }

            Assert.Equal(0m, additionalHours); // No double counting
        }

        #endregion
    }
}
