namespace AttendancePayrollSystem.Models
{
    public sealed class DtrLedgerSummary
    {
        public int RecordCount { get; set; }
        public int PresentCount { get; set; }
        public int LateCount { get; set; }
        public int LeaveCount { get; set; }
        public int MissingPunchCount { get; set; }
        public double TotalHours { get; set; }
        public int TotalTardinessMinutes { get; set; }
    }
}
