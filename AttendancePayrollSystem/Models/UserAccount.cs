namespace AttendancePayrollSystem.Models
{
    public static class UserRoles
    {
        public const string Admin = "Admin";
        public const string Employee = "Employee";
    }

    public class UserAccount
    {
        public int UserAccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int? EmployeeId { get; set; }
        public bool IsActive { get; set; }
    }
}
