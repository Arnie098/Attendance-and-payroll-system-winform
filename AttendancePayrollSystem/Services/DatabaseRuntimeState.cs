namespace AttendancePayrollSystem.Services
{
    public static class DatabaseRuntimeState
    {
        public static bool PreferOfflineDatabase { get; private set; }
        public static bool UseOfflineDatabase { get; private set; }
        public static bool IsOnlineAvailable { get; private set; }
        public static bool IsOfflineDatabaseAvailable { get; private set; }
        public static string StatusMessage { get; private set; } = string.Empty;

        public static void SetPreferredMode(bool preferOfflineDatabase)
        {
            PreferOfflineDatabase = preferOfflineDatabase;
        }

        public static void SetRuntimeState(
            bool useOfflineDatabase,
            bool isOnlineAvailable,
            bool isOfflineDatabaseAvailable,
            string statusMessage)
        {
            UseOfflineDatabase = useOfflineDatabase;
            IsOnlineAvailable = isOnlineAvailable;
            IsOfflineDatabaseAvailable = isOfflineDatabaseAvailable;
            StatusMessage = statusMessage ?? string.Empty;
        }
    }
}
