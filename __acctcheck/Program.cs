using System;
using MySqlConnector;

var connString = Environment.GetEnvironmentVariable("ATT_DB");
using var conn = new MySqlConnection(connString);
conn.Open();
using var cmd = new MySqlCommand(@"
SELECT e.EmployeeCode, e.FullName, ua.Username,
       CASE WHEN ua.PasswordSalt IS NULL OR LENGTH(ua.PasswordSalt) = 0 THEN 'LEGACY' ELSE 'LOCAL' END AS AuthMode,
       ua.IsActive
FROM Employees e
LEFT JOIN UserAccounts ua ON ua.EmployeeId = e.EmployeeId AND ua.Role = 'Employee'
WHERE e.SourceTeacherId IS NOT NULL
ORDER BY e.EmployeeId
LIMIT 20;", conn);
using var reader = cmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader["EmployeeCode"]}\t{reader["FullName"]}\t{reader["Username"]}\t{reader["AuthMode"]}\t{reader["IsActive"]}");
}
