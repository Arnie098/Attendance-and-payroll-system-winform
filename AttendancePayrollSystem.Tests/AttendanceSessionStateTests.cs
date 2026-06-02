using System;
using Xunit;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests for AttendanceRepository.GetSessionState() which determines
    /// what biometric action should be taken next based on current time and existing records.
    /// 
    /// Morning period: before 12:00
    /// Afternoon period: 12:00 or later
    /// </summary>
    public class AttendanceSessionStateTests
    {
        private readonly AttendanceRepository _repository = new();

        #region No Existing Record Tests

        [Fact]
        public void GetSessionState_NullAttendance_MorningTime_ShouldNeedMorningTimeIn()
        {
            // Simulating morning scenario: no record exists
            // The method checks DateTime.Now internally for time-of-day,
            // but the null path only depends on whether todayAttendance is null
            var result = _repository.GetSessionState(null);

            // Result depends on current time of day
            var now = DateTime.Now;
            if (now.Hour < 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsMorningTimeIn, result);
            }
            else
            {
                Assert.Equal(AttendanceSessionState.NeedsAfternoonTimeIn, result);
            }
        }

        #endregion

        #region Morning Period Tests (before 12:00)

        [Fact]
        public void GetSessionState_MorningPeriod_NoTimeInAM_ShouldNeedMorningTimeIn()
        {
            // Record exists but no morning time-in
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour < 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsMorningTimeIn, result);
            }
        }

        [Fact]
        public void GetSessionState_MorningPeriod_HasTimeInAM_NoTimeOutAM_ShouldNeedMorningTimeOut()
        {
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour < 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsMorningTimeOut, result);
            }
        }

        [Fact]
        public void GetSessionState_MorningPeriod_MorningComplete_ShouldReturnMorningComplete()
        {
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30),
                TimeOutAM = DateTime.Today.AddHours(12)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour < 12)
            {
                Assert.Equal(AttendanceSessionState.MorningComplete, result);
            }
        }

        #endregion

        #region Afternoon Period Tests (12:00 or later)

        [Fact]
        public void GetSessionState_AfternoonPeriod_NoTimeInAtAll_ShouldNeedAfternoonTimeIn()
        {
            // No morning or afternoon time-in
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour >= 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsAfternoonTimeIn, result);
            }
        }

        [Fact]
        public void GetSessionState_AfternoonPeriod_HasMorningIn_NoMorningOut_ShouldNeedMorningTimeOut()
        {
            // Morning time-in exists but no time-out (forgot to clock out in morning)
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour >= 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsMorningTimeOut, result);
            }
        }

        [Fact]
        public void GetSessionState_AfternoonPeriod_MorningComplete_NoPMIn_ShouldNeedAfternoonTimeIn()
        {
            // Morning session complete, no afternoon time-in yet
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30),
                TimeOutAM = DateTime.Today.AddHours(12)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour >= 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsAfternoonTimeIn, result);
            }
        }

        [Fact]
        public void GetSessionState_AfternoonPeriod_HasPMIn_NoPMOut_ShouldNeedAfternoonTimeOut()
        {
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30),
                TimeOutAM = DateTime.Today.AddHours(12),
                TimeInPM = DateTime.Today.AddHours(13)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour >= 12)
            {
                Assert.Equal(AttendanceSessionState.NeedsAfternoonTimeOut, result);
            }
        }

        [Fact]
        public void GetSessionState_AllComplete_ShouldReturnAllComplete()
        {
            var attendance = new Attendance
            {
                AttendanceDate = DateTime.Today,
                TimeInAM = DateTime.Today.AddHours(8).AddMinutes(30),
                TimeOutAM = DateTime.Today.AddHours(12),
                TimeInPM = DateTime.Today.AddHours(13),
                TimeOutPM = DateTime.Today.AddHours(17)
            };

            var result = _repository.GetSessionState(attendance);
            var now = DateTime.Now;

            if (now.Hour >= 12)
            {
                Assert.Equal(AttendanceSessionState.AllComplete, result);
            }
        }

        #endregion

        #region Enum Value Coverage Tests

        [Fact]
        public void AttendanceSessionState_ShouldHave6Values()
        {
            var values = Enum.GetValues<AttendanceSessionState>();
            Assert.Equal(6, values.Length);
        }

        [Fact]
        public void AttendanceSessionState_ShouldContainExpectedValues()
        {
            Assert.True(Enum.IsDefined(AttendanceSessionState.NeedsMorningTimeIn));
            Assert.True(Enum.IsDefined(AttendanceSessionState.NeedsMorningTimeOut));
            Assert.True(Enum.IsDefined(AttendanceSessionState.MorningComplete));
            Assert.True(Enum.IsDefined(AttendanceSessionState.NeedsAfternoonTimeIn));
            Assert.True(Enum.IsDefined(AttendanceSessionState.NeedsAfternoonTimeOut));
            Assert.True(Enum.IsDefined(AttendanceSessionState.AllComplete));
        }

        #endregion
    }
}
