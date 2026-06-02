using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using MySqlConnector;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests that validate connection string format without actually connecting to a database.
    /// These catch common mistakes like using commas instead of semicolons as delimiters,
    /// missing required fields, or malformed key=value pairs.
    /// 
    /// These tests read the .env file directly to validate connection strings at test time,
    /// even though DotEnv.Load() only runs at application startup.
    /// </summary>
    public class ConnectionStringValidationTests
    {
        /// <summary>
        /// Reads key=value pairs from the .env file without setting environment variables.
        /// This allows tests to validate .env content independently of app startup.
        /// </summary>
        private static Dictionary<string, string> ReadEnvFile()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Search for .env file starting from test output directory up to project root
            var searchDir = AppContext.BaseDirectory;
            string? envPath = null;

            var dir = new DirectoryInfo(searchDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    envPath = candidate;
                    break;
                }
                dir = dir.Parent;
            }

            if (envPath == null || !File.Exists(envPath))
                return result;

            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var eqIndex = line.IndexOf('=');
                if (eqIndex <= 0)
                    continue;

                var key = line[..eqIndex].Trim();
                var value = line[(eqIndex + 1)..].Trim();

                // Strip surrounding quotes
                if (value.Length >= 2 &&
                    ((value.StartsWith('"') && value.EndsWith('"')) ||
                     (value.StartsWith('\'') && value.EndsWith('\''))))
                {
                    value = value[1..^1];
                }

                result[key] = value;
            }

            return result;
        }

        #region Connection String Format Validation (reads .env directly)

        [Fact]
        public void MainDbConnection_ShouldBeParseableByMySqlConnector()
        {
            var envVars = ReadEnvFile();
            if (!envVars.TryGetValue("ATTENDANCE_DB_CONNECTION", out var connectionString) ||
                string.IsNullOrWhiteSpace(connectionString))
            {
                return; // Skip if not configured
            }

            // First validate our custom format check
            Assert.True(IsValidMySqlConnectionStringFormat(connectionString),
                $"ATTENDANCE_DB_CONNECTION has invalid format. Likely a comma used instead of semicolon.\nValue: {connectionString}");

            var builder = new MySqlConnectionStringBuilder(connectionString);

            Assert.False(string.IsNullOrWhiteSpace(builder.Server),
                "Server/Host is missing or empty. Check connection string format — use semicolons (;) as delimiters, not commas.");
            Assert.False(string.IsNullOrWhiteSpace(builder.Database),
                "Database name is missing or empty. Check connection string format.");
            Assert.False(string.IsNullOrWhiteSpace(builder.UserID),
                "User ID is missing or empty. Check connection string format.");
            Assert.True(builder.Port > 0,
                $"Port should be a positive number, got {builder.Port}. If you used a comma instead of semicolon (e.g., 'Server=host,Port=3306'), the port won't be parsed correctly.");
        }

        [Fact]
        public void SchoolDbConnection_ShouldBeParseableByMySqlConnector()
        {
            var envVars = ReadEnvFile();
            if (!envVars.TryGetValue("SCHOOL_DB_CONNECTION", out var connectionString) ||
                string.IsNullOrWhiteSpace(connectionString))
            {
                return; // Skip if not configured
            }

            // Custom format validation — catches comma-as-delimiter mistakes
            Assert.True(IsValidMySqlConnectionStringFormat(connectionString),
                $"SCHOOL_DB_CONNECTION has invalid format. A comma was likely used instead of a semicolon as delimiter.\n" +
                $"Value: {connectionString}\n" +
                $"Fix: Replace commas between key=value pairs with semicolons (;).");

            var builder = new MySqlConnectionStringBuilder(connectionString);

            Assert.False(string.IsNullOrWhiteSpace(builder.Server),
                "Server/Host is missing or empty in SCHOOL_DB_CONNECTION.");
            Assert.False(builder.Server.Contains(','),
                $"Server field contains a comma, which means a comma was used as delimiter instead of semicolon.\nParsed Server: '{builder.Server}'");
            Assert.False(string.IsNullOrWhiteSpace(builder.Database),
                "Database name is missing or empty in SCHOOL_DB_CONNECTION.");
            Assert.False(string.IsNullOrWhiteSpace(builder.UserID),
                "User ID is missing or empty in SCHOOL_DB_CONNECTION.");
            Assert.True(builder.Port > 0 && builder.Port <= 65535,
                $"Port should be between 1-65535, got {builder.Port}.");
        }

        [Fact]
        public void OfflineDbConnection_ShouldBeParseableConnectionString()
        {
            var envVars = ReadEnvFile();
            if (!envVars.TryGetValue("ATTENDANCE_OFFLINE_DB_CONNECTION", out var connectionString) ||
                string.IsNullOrWhiteSpace(connectionString))
            {
                return; // Skip if not configured
            }

            // SQLite connection strings start with "Data Source="
            if (connectionString.Contains("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("Data Source=", connectionString, StringComparison.OrdinalIgnoreCase);
                var dataSourceStart = connectionString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase) + "Data Source=".Length;
                var dataSourceValue = connectionString[dataSourceStart..].Split(';')[0].Trim();
                Assert.False(string.IsNullOrWhiteSpace(dataSourceValue),
                    "SQLite Data Source path is empty.");
            }
            else
            {
                // MySQL offline — validate same as main connection
                Assert.True(IsValidMySqlConnectionStringFormat(connectionString),
                    "ATTENDANCE_OFFLINE_DB_CONNECTION has invalid format.");
                var builder = new MySqlConnectionStringBuilder(connectionString);
                Assert.False(string.IsNullOrWhiteSpace(builder.Server),
                    "Server is missing in ATTENDANCE_OFFLINE_DB_CONNECTION.");
                Assert.False(string.IsNullOrWhiteSpace(builder.Database),
                    "Database is missing in ATTENDANCE_OFFLINE_DB_CONNECTION.");
            }
        }

        [Fact]
        public void AllConnectionStrings_ShouldUseSemicolonDelimiters()
        {
            var envVars = ReadEnvFile();

            // Verify we actually found and read the .env file
            Assert.True(envVars.Count > 0,
                $"No .env file found or it's empty. Searched from: {AppContext.BaseDirectory}");

            // Verify SCHOOL_DB_CONNECTION was read (it's the one with the known comma bug)
            Assert.True(envVars.ContainsKey("SCHOOL_DB_CONNECTION"),
                $"SCHOOL_DB_CONNECTION not found in .env. Keys found: {string.Join(", ", envVars.Keys)}");

            var connectionKeys = new[] { "ATTENDANCE_DB_CONNECTION", "SCHOOL_DB_CONNECTION", "ATTENDANCE_OFFLINE_DB_CONNECTION" };

            foreach (var key in connectionKeys)
            {
                if (!envVars.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    continue;

                // Skip SQLite connection strings (they use different format)
                if (value.Contains("Data Source", StringComparison.OrdinalIgnoreCase))
                    continue;

                Assert.True(IsValidMySqlConnectionStringFormat(value),
                    $"{key} uses commas instead of semicolons as delimiters.\n" +
                    $"Current: {value}\n" +
                    $"Fix: Replace commas between key=value pairs with semicolons (;).");
            }
        }

        #endregion

        #region Common Mistake Detection Tests

        [Fact]
        public void ConnectionString_CommaAfterServer_ShouldMisparseServerField()
        {
            // "Server=host,Port=3306;Database=db;..." — MySqlConnector absorbs "host,Port=3306" as the server name
            // This means Port never gets set properly (defaults to 3306 by luck, but Server is wrong)
            var badConnectionString = "Server=srv1237.hstgr.io,Port=3306;Database=test;User ID=root;Password=pass;";
            var builder = new MySqlConnectionStringBuilder(badConnectionString);

            // The server field incorrectly contains the comma-separated junk
            Assert.Contains(",", builder.Server,
                StringComparison.Ordinal);
            // This proves the connection would fail at runtime — it would try to connect to "srv1237.hstgr.io,Port=3306"
            Assert.NotEqual("srv1237.hstgr.io", builder.Server);
        }

        [Fact]
        public void ConnectionString_CommaAfterPort_ShouldThrowArgumentException()
        {
            // "Server=host;Port=3306,Database=test;..." — MySqlConnector tries to parse "3306,Database=test" as port
            var badConnectionString = "Server=localhost;Port=3306,Database=test;User ID=root;Password=pass;";

            Assert.Throws<ArgumentException>(() => new MySqlConnectionStringBuilder(badConnectionString));
        }

        [Fact]
        public void ConnectionString_AllCommaDelimiters_ShouldMisparse()
        {
            // All commas — entire string becomes the Server value, nothing else parses
            var badConnectionString = "Server=host.com,Port=3306,Database=mydb,User ID=user,Password=pass";
            var builder = new MySqlConnectionStringBuilder(badConnectionString);

            // Server absorbs everything after the first comma that isn't a recognized key
            // Database and UserID won't be set correctly
            bool hasProblem = builder.Server.Contains(",") ||
                              string.IsNullOrWhiteSpace(builder.Database) ||
                              string.IsNullOrWhiteSpace(builder.UserID);

            Assert.True(hasProblem,
                "All-comma connection string should misparse. Use semicolons (;) as delimiters.");
        }

        [Theory]
        [InlineData("Server=localhost;Port=3306;Database=test;User ID=root;Password=pass;")]
        [InlineData("Server=srv1237.hstgr.io;Port=3306;Database=mydb;User ID=user;Password=P@ss;SslMode=None;")]
        public void ConnectionString_WithSemicolonDelimiters_ShouldParseCorrectly(string goodConnectionString)
        {
            var builder = new MySqlConnectionStringBuilder(goodConnectionString);

            Assert.False(string.IsNullOrWhiteSpace(builder.Server));
            Assert.False(string.IsNullOrWhiteSpace(builder.Database));
            Assert.False(string.IsNullOrWhiteSpace(builder.UserID));
            Assert.Equal(3306u, builder.Port);
        }

        [Fact]
        public void ConnectionString_MissingPassword_ShouldStillParse()
        {
            // Password can be empty for some local setups
            var connectionString = "Server=localhost;Port=3306;Database=test;User ID=root;";
            var builder = new MySqlConnectionStringBuilder(connectionString);

            Assert.Equal("localhost", builder.Server);
            Assert.Equal("test", builder.Database);
            Assert.Equal("root", builder.UserID);
        }

        [Fact]
        public void ConnectionString_PasswordWithSpecialChars_ShouldParse()
        {
            // Passwords often have special characters
            var connectionString = "Server=localhost;Port=3306;Database=test;User ID=root;Password=P@ss#2026!;";
            var builder = new MySqlConnectionStringBuilder(connectionString);

            Assert.Equal("P@ss#2026!", builder.Password);
        }

        #endregion

        #region Delimiter Detection Helper Tests

        [Theory]
        [InlineData("Server=host;Port=3306;Database=db;", true)]
        [InlineData("Server=host;Port=3306;Database=db;User ID=root;", true)]
        public void HasValidDelimiters_ValidStrings_ShouldReturnTrue(string connectionString, bool expectedValid)
        {
            bool isValid = IsValidMySqlConnectionStringFormat(connectionString);
            Assert.Equal(expectedValid, isValid);
        }

        [Theory]
        [InlineData("Server=host,Port=3306,Database=db,")]
        [InlineData("Server=host,Port=3306;Database=db;")]
        public void HasValidDelimiters_CommaStrings_ShouldReturnFalse(string connectionString)
        {
            // Our custom validator catches commas between key=value pairs
            bool isValid = IsValidMySqlConnectionStringFormat(connectionString);
            Assert.False(isValid,
                "Connection strings with commas as delimiters should be detected as invalid.");
        }

        /// <summary>
        /// Checks if a connection string uses proper semicolon delimiters.
        /// Detects the common mistake of using commas between key=value pairs.
        /// Known MySQL connection string keys are checked in values to detect
        /// cases like "Server=host,Port=3306" where Port is absorbed into the Server value.
        /// </summary>
        private static bool IsValidMySqlConnectionStringFormat(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            // Known MySQL connection string keys
            string[] knownKeys = { "Server", "Port", "Database", "User ID", "Password", "SslMode", "Uid", "Pwd", "Host" };

            // Split by semicolons — each segment should be key=value
            var segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                    return false;

                var key = trimmed[..equalsIndex].Trim();
                var value = trimmed[(equalsIndex + 1)..].Trim();

                // If the key contains a comma, it means a comma was used as delimiter
                if (key.Contains(','))
                    return false;

                // Check if the value contains what looks like another key=value pair
                // e.g., "host,Port=3306" means a comma was used instead of semicolon
                foreach (var knownKey in knownKeys)
                {
                    if (value.Contains($",{knownKey}=", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains($",{knownKey} =", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        #endregion


    }
}
