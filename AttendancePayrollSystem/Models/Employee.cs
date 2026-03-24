using System;

namespace AttendancePayrollSystem.Models
{
    public class Employee
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal HourlyRate { get; set; }
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }
        public long? SourceTeacherId { get; set; }
        public long? SourceUserId { get; set; }
        public byte[]? ProfileImage { get; set; }
        public byte[]? BiometricTemplate { get; set; }
    }
}
