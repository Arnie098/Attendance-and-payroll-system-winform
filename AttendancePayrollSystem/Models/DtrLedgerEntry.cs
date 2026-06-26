using System;

namespace AttendancePayrollSystem.Models
{
    public sealed class DtrLedgerEntry
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime AttendanceDate { get; set; }
        public DateTime? TimeInAM { get; set; }
        public DateTime? TimeOutAM { get; set; }
        public DateTime? TimeInPM { get; set; }
        public DateTime? TimeOutPM { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsBiometricVerified { get; set; }

        public double TotalHours
        {
            get
            {
                double total = 0;

                if (TimeInAM.HasValue && TimeOutAM.HasValue)
                {
                    var morning = (TimeOutAM.Value - TimeInAM.Value).TotalHours;
                    if (morning > 0)
                    {
                        total += morning;
                    }
                }

                if (TimeInPM.HasValue && TimeOutPM.HasValue)
                {
                    var afternoon = (TimeOutPM.Value - TimeInPM.Value).TotalHours;
                    if (afternoon > 0)
                    {
                        total += afternoon;
                    }
                }

                return Math.Round(total, 2);
            }
        }

        public int TardinessMinutes
        {
            get
            {
                int total = 0;

                if (TimeInAM.HasValue)
                {
                    var scheduledStart = AttendanceDate.Date.Add(DataAccess.DatabaseConfig.MorningStartTime);
                    var graceEnd = scheduledStart.AddMinutes(DataAccess.DatabaseConfig.GracePeriodMinutes);
                    if (TimeInAM.Value > graceEnd)
                    {
                        total += (int)Math.Ceiling((TimeInAM.Value - scheduledStart).TotalMinutes);
                    }
                }

                if (TimeInPM.HasValue)
                {
                    var scheduledStart = AttendanceDate.Date.Add(DataAccess.DatabaseConfig.AfternoonStartTime);
                    var graceEnd = scheduledStart.AddMinutes(DataAccess.DatabaseConfig.GracePeriodMinutes);
                    if (TimeInPM.Value > graceEnd)
                    {
                        total += (int)Math.Ceiling((TimeInPM.Value - scheduledStart).TotalMinutes);
                    }
                }

                return total;
            }
        }

        public string TimeInAMStatus => GetPunchStatus(TimeInAM, DataAccess.DatabaseConfig.MorningStartTime);

        public string TimeOutAMStatus => GetPunchStatus(TimeOutAM, DataAccess.DatabaseConfig.MorningEndTime);

        public string TimeInPMStatus => GetPunchStatus(TimeInPM, DataAccess.DatabaseConfig.AfternoonStartTime);

        public string TimeOutPMStatus => GetPunchStatus(TimeOutPM, DataAccess.DatabaseConfig.AfternoonEndTime);

        public string TimeInAMLabel => GetPunchLabel(
            TimeInAM,
            DataAccess.DatabaseConfig.MorningStartTime,
            TimeOutAM.HasValue,
            TimeInPM.HasValue || TimeOutPM.HasValue);

        public string TimeOutAMLabel => GetPunchLabel(
            TimeOutAM,
            DataAccess.DatabaseConfig.MorningEndTime,
            TimeInAM.HasValue,
            TimeInPM.HasValue || TimeOutPM.HasValue);

        public string TimeInPMLabel => GetPunchLabel(
            TimeInPM,
            DataAccess.DatabaseConfig.AfternoonStartTime,
            TimeOutPM.HasValue,
            TimeInAM.HasValue || TimeOutAM.HasValue);

        public string TimeOutPMLabel => GetPunchLabel(
            TimeOutPM,
            DataAccess.DatabaseConfig.AfternoonEndTime,
            TimeInPM.HasValue,
            TimeInAM.HasValue || TimeOutAM.HasValue);

        public bool HasMissingPunch =>
            TimeInAM.HasValue != TimeOutAM.HasValue ||
            TimeInPM.HasValue != TimeOutPM.HasValue;

        private string GetPunchStatus(DateTime? actualTime, TimeSpan scheduledTime)
        {
            if (!actualTime.HasValue)
            {
                return "-";
            }

            var scheduledMoment = AttendanceDate.Date.Add(scheduledTime);
            return actualTime.Value > scheduledMoment ? "Late" : "On Time";
        }

        private string GetPunchLabel(DateTime? actualTime, TimeSpan scheduledTime, bool siblingPunchExists, bool otherSessionHasAnyPunch)
        {
            if (!actualTime.HasValue)
            {
                if (siblingPunchExists)
                {
                    return "Missing Punch";
                }

                return otherSessionHasAnyPunch ? "Missed Session" : "-";
            }

            var scheduledMoment = AttendanceDate.Date.Add(scheduledTime);
            if (actualTime.Value <= scheduledMoment)
            {
                return "On Time";
            }

            var lateMinutes = (int)Math.Ceiling((actualTime.Value - scheduledMoment).TotalMinutes);
            return $"Late by {lateMinutes} min";
        }
    }
}
