using System;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem.Models
{
    public class LeaveRequest
    {
        public int LeaveRequestId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public bool IsPaid { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = LeavePolicies.StatusPending;
        public string ReviewerNotes { get; set; } = string.Empty;
        public string ReviewedBy { get; set; } = string.Empty;
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public byte[]? SupportingDocument { get; set; }
        public string? SupportingDocumentName { get; set; }

        public bool HasSupportingDocument => SupportingDocument != null && SupportingDocument.Length > 0;

        public int DocumentCount { get; set; }
        public bool HasDocuments => DocumentCount > 0;

        public int TotalDays =>
            EndDate.Date < StartDate.Date
                ? 0
                : (EndDate.Date - StartDate.Date).Days + 1;

        public int ChargeableDays =>
            LeavePolicies.GetChargeableDayCount(StartDate, EndDate);

        public string PaymentLabel => IsPaid ? "Paid" : "Unpaid";

        public bool CanEmployeeCancel =>
            string.Equals(Status, LeavePolicies.StatusPending, StringComparison.OrdinalIgnoreCase);
    }
}
