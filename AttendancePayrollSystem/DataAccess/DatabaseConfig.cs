using System;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseConfig
    {
        public const int BiometricSimulationDelay = 1200;
        public const decimal RegularHoursPerDay = 8m;
        public const double OvertimeMultiplier = 1.25;

        // Schedule thresholds for tardiness detection
        // Morning session starts at 8:30 AM
        public static readonly TimeSpan MorningStartTime = new(8, 30, 0);
        // Morning session ends at 12:00 PM
        public static readonly TimeSpan MorningEndTime = new(12, 0, 0);
        // Afternoon session starts at 1:00 PM
        public static readonly TimeSpan AfternoonStartTime = new(13, 0, 0);
        // Afternoon session ends at 5:00 PM
        public static readonly TimeSpan AfternoonEndTime = new(17, 0, 0);
        // Grace period in minutes before tardiness is counted
        public const int GracePeriodMinutes = 15;
    }
}
