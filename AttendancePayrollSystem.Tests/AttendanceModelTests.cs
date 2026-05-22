using System;
using Xunit;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Tests
{
    public class AttendanceModelTests
    {
        #region Morning Only Session Tests

        [Fact]
        public void TotalHours_MorningOnly_ShouldCalculateCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 12, 0, 0)
            };

            Assert.Equal(4.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_MorningPartialHour_ShouldRoundTo2Decimals()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 11, 30, 0)
            };

            Assert.Equal(3.5, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_MorningWithMinutes_ShouldRoundCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 11, 20, 0) // 3 hours 20 min = 3.33...
            };

            Assert.Equal(3.33, attendance.TotalHours);
        }

        #endregion

        #region Afternoon Only Session Tests

        [Fact]
        public void TotalHours_AfternoonOnly_ShouldCalculateCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 17, 0, 0)
            };

            Assert.Equal(4.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_AfternoonPartialHour_ShouldCalculateCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 17, 45, 0) // 4.75 hours
            };

            Assert.Equal(4.75, attendance.TotalHours);
        }

        #endregion

        #region Both Sessions Tests

        [Fact]
        public void TotalHours_BothSessions_ShouldSumCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 12, 0, 0),
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 17, 0, 0)
            };

            Assert.Equal(8.0, attendance.TotalHours); // 4 + 4 = 8
        }

        [Fact]
        public void TotalHours_BothSessions_UnequalHours_ShouldSumCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 12, 0, 0),   // 4 hours
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 18, 30, 0)   // 5.5 hours
            };

            Assert.Equal(9.5, attendance.TotalHours); // 4 + 5.5 = 9.5
        }

        [Fact]
        public void TotalHours_FullDayWithOvertime_ShouldCalculateCorrectly()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 7, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 12, 0, 0),   // 5 hours
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 19, 0, 0)    // 6 hours
            };

            Assert.Equal(11.0, attendance.TotalHours); // 5 + 6 = 11
        }

        #endregion

        #region No Sessions (Null Values) Tests

        [Fact]
        public void TotalHours_AllNull_ShouldReturnZero()
        {
            var attendance = new Attendance();

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_OnlyTimeInAM_NoTimeOut_ShouldReturnZero()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_OnlyTimeOutAM_NoTimeIn_ShouldReturnZero()
        {
            var attendance = new Attendance
            {
                TimeOutAM = new DateTime(2024, 1, 1, 12, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_OnlyTimeInPM_NoTimeOut_ShouldReturnZero()
        {
            var attendance = new Attendance
            {
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_OnlyTimeOutPM_NoTimeIn_ShouldReturnZero()
        {
            var attendance = new Attendance
            {
                TimeOutPM = new DateTime(2024, 1, 1, 17, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_MorningInAndPMOut_NoPairs_ShouldReturnZero()
        {
            // TimeInAM set but no TimeOutAM; TimeOutPM set but no TimeInPM
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 17, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        #endregion

        #region Edge Cases with Negative Time Spans

        [Fact]
        public void TotalHours_MorningTimeOutBeforeTimeIn_ShouldReturnZero()
        {
            // TimeOutAM is before TimeInAM - negative span should be ignored
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 12, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 8, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_AfternoonTimeOutBeforeTimeIn_ShouldReturnZero()
        {
            // TimeOutPM is before TimeInPM - negative span should be ignored
            var attendance = new Attendance
            {
                TimeInPM = new DateTime(2024, 1, 1, 17, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 13, 0, 0)
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_OneSessionNegative_OtherPositive_ShouldOnlyCountPositive()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 12, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 8, 0, 0),    // Negative - ignored
                TimeInPM = new DateTime(2024, 1, 1, 13, 0, 0),
                TimeOutPM = new DateTime(2024, 1, 1, 17, 0, 0)    // 4 hours - counted
            };

            Assert.Equal(4.0, attendance.TotalHours);
        }

        [Fact]
        public void TotalHours_SameTimeInAndOut_ShouldReturnZero()
        {
            var attendance = new Attendance
            {
                TimeInAM = new DateTime(2024, 1, 1, 8, 0, 0),
                TimeOutAM = new DateTime(2024, 1, 1, 8, 0, 0) // Same time = 0 hours
            };

            Assert.Equal(0.0, attendance.TotalHours);
        }

        #endregion

        #region Backward Compatibility Properties Tests

        [Fact]
        public void TimeIn_ShouldMapToTimeInAM()
        {
            var attendance = new Attendance();
            var time = new DateTime(2024, 1, 1, 8, 0, 0);

            attendance.TimeIn = time;

            Assert.Equal(time, attendance.TimeInAM);
            Assert.Equal(time, attendance.TimeIn);
        }

        [Fact]
        public void TimeOut_ShouldMapToTimeOutPM()
        {
            var attendance = new Attendance();
            var time = new DateTime(2024, 1, 1, 17, 0, 0);

            attendance.TimeOut = time;

            Assert.Equal(time, attendance.TimeOutPM);
            Assert.Equal(time, attendance.TimeOut);
        }

        #endregion
    }
}
