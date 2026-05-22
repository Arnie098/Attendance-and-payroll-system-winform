using System;
using System.Collections.Generic;
using Xunit;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem.Tests
{
    public class LeavePoliciesTests
    {
        #region IsPaidLeaveType Tests

        [Theory]
        [InlineData("Vacation Leave", true)]
        [InlineData("Sick Leave", true)]
        [InlineData("Emergency Leave", true)]
        [InlineData("Maternity Leave", true)]
        [InlineData("Paternity Leave", true)]
        [InlineData("Unpaid Leave", false)]
        public void IsPaidLeaveType_DefaultLeaveTypes_ShouldReturnCorrectResult(string leaveType, bool expected)
        {
            var result = LeavePolicies.IsPaidLeaveType(leaveType);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("unpaid leave", false)]   // Case insensitive
        [InlineData("UNPAID LEAVE", false)]   // All caps
        [InlineData(" Unpaid Leave ", false)]  // With whitespace
        public void IsPaidLeaveType_UnpaidLeaveVariations_ShouldReturnFalse(string leaveType, bool expected)
        {
            var result = LeavePolicies.IsPaidLeaveType(leaveType);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        public void IsPaidLeaveType_NullOrEmpty_ShouldReturnFalse(string? leaveType, bool expected)
        {
            var result = LeavePolicies.IsPaidLeaveType(leaveType);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsPaidLeaveType_UnknownType_ShouldReturnTrue()
        {
            // Any non-"Unpaid Leave" string is considered paid
            var result = LeavePolicies.IsPaidLeaveType("Custom Leave");
            Assert.True(result);
        }

        #endregion

        #region GetChargeableDates Tests

        [Fact]
        public void GetChargeableDates_WeekdayRange_ShouldReturnAllWeekdays()
        {
            // Monday Jan 1, 2024 to Friday Jan 5, 2024 = 5 weekdays
            var start = new DateTime(2024, 1, 1); // Monday
            var end = new DateTime(2024, 1, 5);   // Friday

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Equal(5, result.Count);
            foreach (var date in result)
            {
                Assert.NotEqual(DayOfWeek.Saturday, date.DayOfWeek);
                Assert.NotEqual(DayOfWeek.Sunday, date.DayOfWeek);
            }
        }

        [Fact]
        public void GetChargeableDates_FullWeek_ShouldExcludeWeekends()
        {
            // Monday Jan 1, 2024 to Sunday Jan 7, 2024 = 5 weekdays
            var start = new DateTime(2024, 1, 1); // Monday
            var end = new DateTime(2024, 1, 7);   // Sunday

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void GetChargeableDates_TwoWeeks_ShouldReturn10Weekdays()
        {
            // Monday Jan 1, 2024 to Friday Jan 12, 2024 = 10 weekdays
            var start = new DateTime(2024, 1, 1);  // Monday
            var end = new DateTime(2024, 1, 12);   // Friday

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Equal(10, result.Count);
        }

        [Fact]
        public void GetChargeableDates_WeekendOnly_ShouldReturnEmpty()
        {
            // Saturday Jan 6, 2024 to Sunday Jan 7, 2024
            var start = new DateTime(2024, 1, 6); // Saturday
            var end = new DateTime(2024, 1, 7);   // Sunday

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Empty(result);
        }

        [Fact]
        public void GetChargeableDates_SingleWeekday_ShouldReturnOneDate()
        {
            var date = new DateTime(2024, 1, 3); // Wednesday

            var result = LeavePolicies.GetChargeableDates(date, date);

            Assert.Single(result);
            Assert.Equal(date, result[0]);
        }

        [Fact]
        public void GetChargeableDates_SingleSaturday_ShouldReturnEmpty()
        {
            var date = new DateTime(2024, 1, 6); // Saturday

            var result = LeavePolicies.GetChargeableDates(date, date);

            Assert.Empty(result);
        }

        [Fact]
        public void GetChargeableDates_EndBeforeStart_ShouldReturnEmpty()
        {
            var start = new DateTime(2024, 1, 5);
            var end = new DateTime(2024, 1, 1);

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Empty(result);
        }

        [Fact]
        public void GetChargeableDates_ShouldIgnoreTimeComponent()
        {
            // Even with time components, should work on date level
            var start = new DateTime(2024, 1, 1, 14, 30, 0); // Monday with time
            var end = new DateTime(2024, 1, 3, 8, 0, 0);     // Wednesday with time

            var result = LeavePolicies.GetChargeableDates(start, end);

            Assert.Equal(3, result.Count);
        }

        #endregion

        #region GetChargeableDayCount Tests

        [Fact]
        public void GetChargeableDayCount_WeekdayRange_ShouldMatchDatesCount()
        {
            var start = new DateTime(2024, 1, 1); // Monday
            var end = new DateTime(2024, 1, 5);   // Friday

            var count = LeavePolicies.GetChargeableDayCount(start, end);
            var dates = LeavePolicies.GetChargeableDates(start, end);

            Assert.Equal(dates.Count, count);
            Assert.Equal(5, count);
        }

        [Fact]
        public void GetChargeableDayCount_EndBeforeStart_ShouldReturnZero()
        {
            var count = LeavePolicies.GetChargeableDayCount(
                new DateTime(2024, 1, 10),
                new DateTime(2024, 1, 5));

            Assert.Equal(0, count);
        }

        [Fact]
        public void GetChargeableDayCount_MonthRange_ShouldCountCorrectly()
        {
            // January 2024: starts Monday, 31 days
            // Weekdays: 23 (31 - 4 Saturdays - 4 Sundays)
            var start = new DateTime(2024, 1, 1);
            var end = new DateTime(2024, 1, 31);

            var count = LeavePolicies.GetChargeableDayCount(start, end);

            Assert.Equal(23, count);
        }

        #endregion

        #region IsTerminalStatus Tests

        [Theory]
        [InlineData("Approved", true)]
        [InlineData("Rejected", true)]
        [InlineData("Cancelled", true)]
        [InlineData("Pending", false)]
        public void IsTerminalStatus_KnownStatuses_ShouldReturnCorrectResult(string status, bool expected)
        {
            var result = LeavePolicies.IsTerminalStatus(status);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("approved", true)]
        [InlineData("REJECTED", true)]
        [InlineData("cancelled", true)]
        [InlineData("pending", false)]
        public void IsTerminalStatus_CaseInsensitive_ShouldWork(string status, bool expected)
        {
            var result = LeavePolicies.IsTerminalStatus(status);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("Unknown", false)]
        public void IsTerminalStatus_InvalidInputs_ShouldReturnFalse(string? status, bool expected)
        {
            var result = LeavePolicies.IsTerminalStatus(status);
            Assert.Equal(expected, result);
        }

        #endregion

        #region IsLeaveAttendanceStatus Tests

        [Theory]
        [InlineData("On Leave", true)]
        [InlineData("Leave Without Pay", true)]
        [InlineData("Present", false)]
        [InlineData("Absent", false)]
        public void IsLeaveAttendanceStatus_KnownStatuses_ShouldReturnCorrectResult(string status, bool expected)
        {
            var result = LeavePolicies.IsLeaveAttendanceStatus(status);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("on leave", true)]
        [InlineData("ON LEAVE", true)]
        [InlineData("leave without pay", true)]
        [InlineData("LEAVE WITHOUT PAY", true)]
        public void IsLeaveAttendanceStatus_CaseInsensitive_ShouldWork(string status, bool expected)
        {
            var result = LeavePolicies.IsLeaveAttendanceStatus(status);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        public void IsLeaveAttendanceStatus_NullOrEmpty_ShouldReturnFalse(string? status, bool expected)
        {
            var result = LeavePolicies.IsLeaveAttendanceStatus(status);
            Assert.Equal(expected, result);
        }

        #endregion

        #region GetAttendanceStatus Tests

        [Fact]
        public void GetAttendanceStatus_PaidLeave_ShouldReturnOnLeave()
        {
            var result = LeavePolicies.GetAttendanceStatus(true);
            Assert.Equal("On Leave", result);
        }

        [Fact]
        public void GetAttendanceStatus_UnpaidLeave_ShouldReturnLeaveWithoutPay()
        {
            var result = LeavePolicies.GetAttendanceStatus(false);
            Assert.Equal("Leave Without Pay", result);
        }

        #endregion

        #region Constants Tests

        [Fact]
        public void DefaultLeaveTypes_ShouldContain6Types()
        {
            Assert.Equal(6, LeavePolicies.DefaultLeaveTypes.Length);
        }

        [Fact]
        public void DefaultLeaveTypes_ShouldContainExpectedTypes()
        {
            Assert.Contains("Vacation Leave", LeavePolicies.DefaultLeaveTypes);
            Assert.Contains("Sick Leave", LeavePolicies.DefaultLeaveTypes);
            Assert.Contains("Emergency Leave", LeavePolicies.DefaultLeaveTypes);
            Assert.Contains("Maternity Leave", LeavePolicies.DefaultLeaveTypes);
            Assert.Contains("Paternity Leave", LeavePolicies.DefaultLeaveTypes);
            Assert.Contains("Unpaid Leave", LeavePolicies.DefaultLeaveTypes);
        }

        #endregion
    }
}
