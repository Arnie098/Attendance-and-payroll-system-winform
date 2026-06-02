using System;
using Xunit;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests to verify DatabaseConfig constants haven't been accidentally changed.
    /// These are critical business rules that affect payroll calculations.
    /// </summary>
    public class DatabaseConfigTests
    {
        [Fact]
        public void BiometricSimulationDelay_ShouldBe1200ms()
        {
            Assert.Equal(1200, DatabaseConfig.BiometricSimulationDelay);
        }

        [Fact]
        public void RegularHoursPerDay_ShouldBe8()
        {
            Assert.Equal(8m, DatabaseConfig.RegularHoursPerDay);
        }

        [Fact]
        public void OvertimeMultiplier_ShouldBe1Point25()
        {
            Assert.Equal(1.25, DatabaseConfig.OvertimeMultiplier);
        }

        [Fact]
        public void MorningStartTime_ShouldBe8_30AM()
        {
            Assert.Equal(8, DatabaseConfig.MorningStartTime.Hours);
            Assert.Equal(30, DatabaseConfig.MorningStartTime.Minutes);
            Assert.Equal(0, DatabaseConfig.MorningStartTime.Seconds);
        }

        [Fact]
        public void AfternoonStartTime_ShouldBe1_00PM()
        {
            Assert.Equal(13, DatabaseConfig.AfternoonStartTime.Hours);
            Assert.Equal(0, DatabaseConfig.AfternoonStartTime.Minutes);
            Assert.Equal(0, DatabaseConfig.AfternoonStartTime.Seconds);
        }

        [Fact]
        public void GracePeriodMinutes_ShouldBe15()
        {
            Assert.Equal(15, DatabaseConfig.GracePeriodMinutes);
        }

        [Fact]
        public void OvertimeMultiplier_ShouldBeGreaterThan1()
        {
            Assert.True(DatabaseConfig.OvertimeMultiplier > 1.0,
                "Overtime multiplier must be greater than 1.0 to provide overtime premium");
        }

        [Fact]
        public void RegularHoursPerDay_ShouldBePositive()
        {
            Assert.True(DatabaseConfig.RegularHoursPerDay > 0,
                "Regular hours per day must be positive");
        }

        [Fact]
        public void GracePeriodMinutes_ShouldBeNonNegative()
        {
            Assert.True(DatabaseConfig.GracePeriodMinutes >= 0,
                "Grace period cannot be negative");
        }

        [Fact]
        public void MorningStartTime_ShouldBeBeforeAfternoonStartTime()
        {
            Assert.True(DatabaseConfig.MorningStartTime < DatabaseConfig.AfternoonStartTime,
                "Morning start must be before afternoon start");
        }
    }
}
