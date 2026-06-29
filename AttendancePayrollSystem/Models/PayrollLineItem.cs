namespace AttendancePayrollSystem.Models
{
    public class PayrollLineItem
    {
        public int Id { get; set; }
        public int PayrollId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public PayrollLineItemType ItemType { get; set; } = PayrollLineItemType.Deduction;
        public int SortOrder { get; set; }
    }
}
