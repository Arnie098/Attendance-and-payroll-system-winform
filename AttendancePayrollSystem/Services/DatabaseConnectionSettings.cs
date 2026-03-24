namespace AttendancePayrollSystem.Services
{
    public sealed class DatabaseConnectionSettings
    {
        public string Server { get; set; } = string.Empty;
        public uint Port { get; set; } = 3306;
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SslMode { get; set; } = "Preferred";
    }
}
