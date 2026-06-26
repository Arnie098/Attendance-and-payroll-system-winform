using System;

namespace AttendancePayrollSystem.Models
{
    public sealed class DtrLedgerQuery
    {
        public string SearchText { get; set; } = string.Empty;
        public string Department { get; set; } = "All";
        public string Status { get; set; } = "All";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool MissingPunchOnly { get; set; }
        public int Limit { get; set; } = 500;
    }
}
