using System;

namespace AttendancePayrollSystem.Models
{
    public class Payroll
    {
        public int PayrollId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime PayPeriodStart { get; set; }
        public DateTime PayPeriodEnd { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal GrossPay { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetPay { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal SssDeduction { get; set; }
        public decimal PhilHealthDeduction { get; set; }
        public decimal PagIbigDeduction { get; set; }
        public decimal WithholdingTax { get; set; }

        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;

        /// <summary>
        /// Total tardiness minutes accumulated during the pay period.
        /// </summary>
        public int TotalTardinessMinutes { get; set; }

        /// <summary>
        /// Amount deducted from pay due to tardiness (minute-rate based).
        /// This is already included in Deductions.
        /// </summary>
        public decimal TardinessDeduction { get; set; }

        /// <summary>
        /// Manual/additional deduction amount entered by the admin.
        /// This is already included in Deductions.
        /// </summary>
        public decimal ManualDeduction { get; set; }

        /// <summary>
        /// Optional note describing the reason for the manual deduction.
        /// </summary>
        public string ManualDeductionNote { get; set; } = string.Empty;
    }
}
