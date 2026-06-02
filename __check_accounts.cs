using System;
using System.IO;
using MySqlConnector;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
string? db = null;
foreach (var raw in File.ReadAllLines(envPath))
{
    var line = raw.Trim();
    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
    if (!line.StartsWith("ATTENDANCE_DB_CONNECTION=")) continue;
    db = line.Substring("ATTENDANCE_DB_CONNECTION=".Length).Trim().Trim('"');
    break;
}
if (string.IsNullOrWhiteSpace(db)) throw new Exception("Missing ATTENDANCE_DB_CONNECTION");
using var conn = new MySqlConnection(db);
conn.Open();
using var cmd = new MySqlCommand(@"
SELECT e.EmployeeId, e.EmployeeCode, e.FullName, e.SourceTeacherId,
       ua.Username, ua.IsActive AS AccountActive
FROM Employees e
LEFT JOIN UserAccounts ua ON ua.EmployeeId = e.EmployeeId AND ua.Role = 'Employee'
WHERE e.SourceTeacherId IS NOT NULL
ORDER BY e.EmployeeId
LIMIT 20;", conn);
using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader["EmployeeId"]}\t{reader["EmployeeCode"]}\t{reader["FullName"]}\t{reader["SourceTeacherId"]}\t{reader["Username"]}\t{reader["AccountActive"]}");
}
