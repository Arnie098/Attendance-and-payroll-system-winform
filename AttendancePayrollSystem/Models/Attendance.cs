using System;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.Models
{
    public class Attendance
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public DateTime? TimeInAM { get; set; }
        public DateTime? TimeOutAM { get; set; }
        public DateTime? TimeInPM { get; set; }
        public DateTime? TimeOutPM { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsBiometricVerified { get; set; }

        /// <summary>
        /// Backward-compatible property: returns the morning Time In.
        /// </summary>
        public DateTime? TimeIn
        {
            get => TimeInAM;
            set => TimeInAM = value;
        }

        /// <summary>
        /// Backward-compatible property: returns the afternoon Time Out.
        /// </summary>
        public DateTime? TimeOut
        {
            get => TimeOutPM;
            set => TimeOutPM = value;
        }

        public double TotalHours
        {
            get
            {
                double total = 0;

                // Morning session: TimeInAM to TimeOutAM
                if (TimeInAM.HasValue && TimeOutAM.HasValue)
                {
                    var morning = (TimeOutAM.Value - TimeInAM.Value).TotalHours;
                    if (morning > 0) total += morning;
                }

                // Afternoon session: TimeInPM to TimeOutPM
                if (TimeInPM.HasValue && TimeOutPM.HasValue)
                {
                    var afternoon = (TimeOutPM.Value - TimeInPM.Value).TotalHours;
                    if (afternoon > 0) total += afternoon;
                }

                return Math.Round(total, 2);
            }
        }

        /// <summary>
        /// Total tardiness minutes for the day (morning + afternoon combined).
        /// Considers the grace period defined in DatabaseConfig.
        /// </summary>
        public int TardinessMinutes
        {
            get
            {
                int total = 0;

                // Morning tardiness: time in after scheduled start + grace
                if (TimeInAM.HasValue)
                {
                    var scheduledStart = AttendanceDate.Date.Add(DatabaseConfig.MorningStartTime);
                    var graceEnd = scheduledStart.AddMinutes(DatabaseConfig.GracePeriodMinutes);
                    if (TimeInAM.Value > graceEnd)
                    {
                        total += (int)Math.Ceiling((TimeInAM.Value - scheduledStart).TotalMinutes);
                    }
                }

                // Afternoon tardiness: time in after scheduled start + grace
                if (TimeInPM.HasValue)
                {
                    var scheduledStart = AttendanceDate.Date.Add(DatabaseConfig.AfternoonStartTime);
                    var graceEnd = scheduledStart.AddMinutes(DatabaseConfig.GracePeriodMinutes);
                    if (TimeInPM.Value > graceEnd)
                    {
                        total += (int)Math.Ceiling((TimeInPM.Value - scheduledStart).TotalMinutes);
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// Whether this attendance record has any tardiness.
        /// </summary>
        public bool IsLate => TardinessMinutes > 0;
    }
}
