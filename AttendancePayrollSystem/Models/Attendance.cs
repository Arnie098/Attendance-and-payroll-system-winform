using System;

namespace AttendancePayrollSystem.Models
{
    public class Attendance
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsBiometricVerified { get; set; }

        public double TotalHours
        {
            get
            {
                if (!TimeIn.HasValue || !TimeOut.HasValue)
                {
                    return 0;
                }

                var total = (TimeOut.Value - TimeIn.Value).TotalHours;
                return total < 0 ? 0 : Math.Round(total, 2);
            }
        }
    }
}
