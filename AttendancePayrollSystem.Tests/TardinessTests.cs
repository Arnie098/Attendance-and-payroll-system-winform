using System;
using Xunit;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests for Attendance.TardinessMinutes and Attendance.IsLate properties.
    /// Schedule: Morning starts 8:30 AM, Afternoon starts 1:00 PM, Grace period: 15 minutes.
    /// Tardiness is counted from the scheduled start time (not from grace end).
    /// </summary>
    public class TardinessTests
    {
        // Helper to create attendance for a specific date with morning time-in
        private static Attendance CreateMorningAttendance(DateTime date, int hour, int minute)
        {
            return new Attendance
            {
                AttendanceDate = date,
                TimeInAM = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0)
            };
        }

        // Helper to create attendance for a specific date with afternoon time-in
        private static Attendance CreateAfternoonAttendance(DateTime date, int hour, int minute)
        {
            return new Attendance
            {
                AttendanceDate = date,
                TimeInPM = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0)
            };
        }

        private static readonly DateTime TestDate = new(2024, 6, 3); // A Monday

        #region Morning Tardiness Tests

        [Fact]
        public void TardinessMinutes_MorningOnTime_ShouldBeZero()
        {
            // Arrives at 8:30 AM exactly (scheduled start) — within grace
            var attendance = CreateMorningAttendance(TestDate, 8, 30);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningEarly_ShouldBeZero()
        {
            // Arrives at 8:00 AM — early
            var attendance = CreateMorningAttendance(TestDate, 8, 0);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningAtGraceBoundary_ShouldBeZero()
        {
            // Arrives at 8:45 AM exactly (grace end = 8:30 + 15 min)
            // TimeInAM is NOT > graceEnd, so no tardiness
            var attendance = CreateMorningAttendance(TestDate, 8, 45);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningOneMinuteAfterGrace_ShouldCountFromScheduledStart()
        {
            // Arrives at 8:46 AM — 1 minute past grace
            // Tardiness = ceiling((8:46 - 8:30).TotalMinutes) = ceiling(16) = 16 minutes
            var attendance = CreateMorningAttendance(TestDate, 8, 46);
            Assert.Equal(16, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_Morning30MinutesLate_ShouldBe30()
        {
            // Arrives at 9:00 AM
            // Tardiness = ceiling((9:00 - 8:30).TotalMinutes) = ceiling(30) = 30
            var attendance = CreateMorningAttendance(TestDate, 9, 0);
            Assert.Equal(30, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningVeryLate_ShouldCalculateCorrectly()
        {
            // Arrives at 10:15 AM
            // Tardiness = ceiling((10:15 - 8:30).TotalMinutes) = ceiling(105) = 105
            var attendance = CreateMorningAttendance(TestDate, 10, 15);
            Assert.Equal(105, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningWithSeconds_ShouldCeiling()
        {
            // Arrives at 8:46:30 AM (30 seconds past 8:46)
            // Tardiness = ceiling((8:46:30 - 8:30:00).TotalMinutes) = ceiling(16.5) = 17
            var attendance = new Attendance
            {
                AttendanceDate = TestDate,
                TimeInAM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 8, 46, 30)
            };
            Assert.Equal(17, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        #endregion

        #region Afternoon Tardiness Tests

        [Fact]
        public void TardinessMinutes_AfternoonOnTime_ShouldBeZero()
        {
            // Arrives at 1:00 PM exactly — within grace
            var attendance = CreateAfternoonAttendance(TestDate, 13, 0);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_AfternoonEarly_ShouldBeZero()
        {
            // Arrives at 12:50 PM — early
            var attendance = CreateAfternoonAttendance(TestDate, 12, 50);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_AfternoonAtGraceBoundary_ShouldBeZero()
        {
            // Arrives at 1:15 PM exactly (grace end = 1:00 + 15 min)
            var attendance = CreateAfternoonAttendance(TestDate, 13, 15);
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_AfternoonOneMinuteAfterGrace_ShouldCountFromScheduledStart()
        {
            // Arrives at 1:16 PM — 1 minute past grace
            // Tardiness = ceiling((13:16 - 13:00).TotalMinutes) = ceiling(16) = 16
            var attendance = CreateAfternoonAttendance(TestDate, 13, 16);
            Assert.Equal(16, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_Afternoon30MinutesLate_ShouldBe30()
        {
            // Arrives at 1:30 PM
            // Tardiness = ceiling((13:30 - 13:00).TotalMinutes) = ceiling(30) = 30
            var attendance = CreateAfternoonAttendance(TestDate, 13, 30);
            Assert.Equal(30, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        #endregion

        #region Both Sessions Tardiness Tests

        [Fact]
        public void TardinessMinutes_BothSessionsLate_ShouldAccumulate()
        {
            // Morning: arrives 9:00 AM → 30 min late
            // Afternoon: arrives 1:30 PM → 30 min late
            // Total: 60 minutes
            var attendance = new Attendance
            {
                AttendanceDate = TestDate,
                TimeInAM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 9, 0, 0),
                TimeInPM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 13, 30, 0)
            };
            Assert.Equal(60, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningLateAfternoonOnTime_ShouldOnlyCountMorning()
        {
            // Morning: arrives 9:00 AM → 30 min late
            // Afternoon: arrives 1:00 PM → on time (within grace)
            var attendance = new Attendance
            {
                AttendanceDate = TestDate,
                TimeInAM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 9, 0, 0),
                TimeInPM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 13, 0, 0)
            };
            Assert.Equal(30, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_MorningOnTimeAfternoonLate_ShouldOnlyCountAfternoon()
        {
            // Morning: arrives 8:30 AM → on time
            // Afternoon: arrives 1:30 PM → 30 min late
            var attendance = new Attendance
            {
                AttendanceDate = TestDate,
                TimeInAM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 8, 30, 0),
                TimeInPM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 13, 30, 0)
            };
            Assert.Equal(30, attendance.TardinessMinutes);
            Assert.True(attendance.IsLate);
        }

        #endregion

        #region Null / No Time-In Tests

        [Fact]
        public void TardinessMinutes_NoTimeIn_ShouldBeZero()
        {
            var attendance = new Attendance
            {
                AttendanceDate = TestDate
            };
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        [Fact]
        public void TardinessMinutes_OnlyTimeOutSet_ShouldBeZero()
        {
            // TimeOut without TimeIn — no tardiness to calculate
            var attendance = new Attendance
            {
                AttendanceDate = TestDate,
                TimeOutAM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 12, 0, 0),
                TimeOutPM = new DateTime(TestDate.Year, TestDate.Month, TestDate.Day, 17, 0, 0)
            };
            Assert.Equal(0, attendance.TardinessMinutes);
            Assert.False(attendance.IsLate);
        }

        #endregion

        #region DatabaseConfig Constants Verification

        [Fact]
        public void MorningStartTime_ShouldBe8_30()
        {
            Assert.Equal(new TimeSpan(8, 30, 0), DatabaseConfig.MorningStartTime);
        }

        [Fact]
        public void AfternoonStartTime_ShouldBe13_00()
        {
            Assert.Equal(new TimeSpan(13, 0, 0), DatabaseConfig.AfternoonStartTime);
        }

        [Fact]
        public void GracePeriodMinutes_ShouldBe15()
        {
            Assert.Equal(15, DatabaseConfig.GracePeriodMinutes);
        }

        #endregion
    }
}
