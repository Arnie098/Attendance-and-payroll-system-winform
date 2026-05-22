using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace AttendancePayrollSystem.Services
{
    /// <summary>
    /// Lightweight structured logger that writes to a daily rotating log file.
    /// Logs are stored in %LocalAppData%\AttendancePayrollSystem\logs\.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _writeLock = new();
        private static readonly string _logDirectory;
        private static string? _currentLogFile;
        private static DateTime _currentLogDate;

        static AppLogger()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(localAppData, "AttendancePayrollSystem", "logs");
        }

        public static void Info(string message, [CallerMemberName] string? caller = null)
        {
            Write("INFO", message, caller);
        }

        public static void Warn(string message, [CallerMemberName] string? caller = null)
        {
            Write("WARN", message, caller);
        }

        public static void Error(string message, Exception? ex = null, [CallerMemberName] string? caller = null)
        {
            var entry = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write("ERROR", entry, caller);
        }

        public static void Error(Exception ex, string? context = null, [CallerMemberName] string? caller = null)
        {
            var message = string.IsNullOrWhiteSpace(context)
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"{context} | {ex.GetType().Name}: {ex.Message}";
            Write("ERROR", message, caller);
        }

        public static void Debug(string message, [CallerMemberName] string? caller = null)
        {
#if DEBUG
            Write("DEBUG", message, caller);
#endif
        }

        public static void Sync(string message, [CallerMemberName] string? caller = null)
        {
            Write("SYNC", message, caller);
        }

        public static void Auth(string message, [CallerMemberName] string? caller = null)
        {
            Write("AUTH", message, caller);
        }

        /// <summary>
        /// Gets the path to today's log file (useful for diagnostics UI).
        /// </summary>
        public static string GetCurrentLogFilePath()
        {
            return GetLogFilePath(DateTime.Now);
        }

        /// <summary>
        /// Gets the log directory path.
        /// </summary>
        public static string LogDirectory => _logDirectory;

        private static void Write(string level, string message, string? caller)
        {
            try
            {
                var now = DateTime.Now;
                var logFile = EnsureLogFile(now);
                var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var callerLabel = string.IsNullOrWhiteSpace(caller) ? "" : $"[{caller}] ";
                var line = $"{timestamp} [{level,-5}] {callerLabel}{message}";

                lock (_writeLock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never crash the application.
            }
        }

        private static string EnsureLogFile(DateTime now)
        {
            if (_currentLogFile != null && _currentLogDate == now.Date)
            {
                return _currentLogFile;
            }

            lock (_writeLock)
            {
                if (_currentLogFile != null && _currentLogDate == now.Date)
                {
                    return _currentLogFile;
                }

                Directory.CreateDirectory(_logDirectory);
                _currentLogDate = now.Date;
                _currentLogFile = GetLogFilePath(now);
                CleanOldLogs();
                return _currentLogFile;
            }
        }

        private static string GetLogFilePath(DateTime date)
        {
            return Path.Combine(_logDirectory, $"app-{date:yyyy-MM-dd}.log");
        }

        private static void CleanOldLogs()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-30);
                foreach (var file in Directory.GetFiles(_logDirectory, "app-*.log"))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoff)
                    {
                        fileInfo.Delete();
                    }
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
