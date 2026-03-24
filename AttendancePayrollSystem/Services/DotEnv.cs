using System;
using System.Collections.Generic;
using System.IO;

namespace AttendancePayrollSystem.Services
{
    public static class DotEnv
    {
        public static void Load()
        {
            foreach (var path in GetCandidatePaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var value = line[(separatorIndex + 1)..].Trim();
                    if (value.Length >= 2 &&
                        ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                         (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))))
                    {
                        value = value[1..^1];
                    }

                    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }
            }
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appOverridePath = DatabaseConnectionSettingsStore.SettingsFilePath;
            if (seen.Add(appOverridePath))
            {
                yield return appOverridePath;
            }

            foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(startPath))
                {
                    continue;
                }

                var directory = new DirectoryInfo(Path.GetFullPath(startPath));
                while (directory != null)
                {
                    var candidate = Path.Combine(directory.FullName, ".env");
                    if (seen.Add(candidate))
                    {
                        yield return candidate;
                    }

                    directory = directory.Parent;
                }
            }
        }
    }
}
