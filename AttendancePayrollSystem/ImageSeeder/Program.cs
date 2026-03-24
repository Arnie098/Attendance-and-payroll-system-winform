using Bogus;
using MySqlConnector;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;

var connectionString = GetConnectionString();
var faker = new Faker("en");
using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(10)
};

if (args.Any(a => a.Equals("--images-only", StringComparison.OrdinalIgnoreCase)))
{
    SeedImagesOnly(connectionString, faker, httpClient);
    return;
}

var count = ParseCount(args);
var seededEmployees = SeedFakeEmployees(connectionString, faker, count, httpClient);
SeedFakeAttendances(connectionString, faker, seededEmployees);
SeedFakePayrollRecords(connectionString, faker, seededEmployees);
PrintTableCounts(connectionString);

static byte[] GenerateFakeProfileImage(string fullName, Faker faker)
{
    using var bitmap = new Bitmap(256, 256);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

    var bgColor = Color.FromArgb(
        faker.Random.Int(40, 220),
        faker.Random.Int(40, 220),
        faker.Random.Int(40, 220));
    graphics.Clear(bgColor);

    var accent = Color.FromArgb(
        Math.Min(255, bgColor.R + 25),
        Math.Min(255, bgColor.G + 25),
        Math.Min(255, bgColor.B + 25));

    using var accentBrush = new SolidBrush(accent);
    graphics.FillEllipse(accentBrush, 18, 18, 220, 220);

    var initials = GetInitials(fullName);
    using var font = new Font("Segoe UI", 86, FontStyle.Bold, GraphicsUnit.Pixel);
    using var textBrush = new SolidBrush(Color.White);
    var size = graphics.MeasureString(initials, font);
    var x = (bitmap.Width - size.Width) / 2f;
    var y = (bitmap.Height - size.Height) / 2f - 6f;
    graphics.DrawString(initials, font, textBrush, x, y);

    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static string GetConnectionString()
{
    var fromEnv = Environment.GetEnvironmentVariable("ATTENDANCE_DB_CONNECTION");
    if (string.IsNullOrWhiteSpace(fromEnv))
    {
        throw new InvalidOperationException(
            "Missing ATTENDANCE_DB_CONNECTION. Provide a secure MySQL connection string via environment variable.");
    }

    var builder = new MySqlConnectionStringBuilder(fromEnv);
    if (string.IsNullOrWhiteSpace(builder.Server) || string.IsNullOrWhiteSpace(builder.UserID))
    {
        throw new InvalidOperationException("Connection string must include MySQL host and username.");
    }

    if (builder.Port == 0)
    {
        builder.Port = 3306;
    }

    if (builder.SslMode == MySqlSslMode.None)
    {
        builder.SslMode = MySqlSslMode.Preferred;
    }

    return builder.ConnectionString;
}

static byte[] GenerateFakeBiometricTemplate(Faker faker)
{
    // Simulates a small biometric template payload.
    return faker.Random.Bytes(512);
}

static string GetInitials(string fullName)
{
    var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) return "NA";
    if (parts.Length == 1) return parts[0][0].ToString().ToUpperInvariant();
    return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
}

static int ParseCount(string[] args)
{
    if (args.Length == 0) return 10;
    return int.TryParse(args[0], out var count) && count > 0 ? count : 10;
}

static List<SeededEmployee> SeedFakeEmployees(string connectionString, Faker faker, int count, HttpClient httpClient)
{
    var departments = new[] { "IT", "HR", "Finance", "Operations", "Sales", "Marketing", "Admin" };
    var positions = new[]
    {
        "Software Engineer", "HR Specialist", "Accountant", "Operations Analyst",
        "Sales Associate", "Marketing Coordinator", "Admin Officer", "Support Engineer"
    };
    var seededEmployees = new List<SeededEmployee>();

    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    var nextSequence = GetNextEmployeeCodeSequence(connection);
    var generated = 0;

    for (var i = 0; i < count; i++)
    {
        var fullName = faker.Name.FullName();
        var employeeCode = $"EMP-{nextSequence:D4}";
        nextSequence++;

        var emailUser = fullName.ToLowerInvariant().Replace(" ", ".");
        var employee = new
        {
            EmployeeCode = employeeCode,
            FullName = fullName,
            Email = $"{emailUser}@company.com",
            Phone = faker.Phone.PhoneNumber("09#########"),
            Position = faker.PickRandom(positions),
            Department = faker.PickRandom(departments),
            HourlyRate = faker.Random.Decimal(180m, 420m),
            HireDate = faker.Date.Past(6, DateTime.Today.AddMonths(-1)).Date,
            IsActive = faker.Random.Bool(0.9f),
            ProfileImage = GenerateHumanProfileImage(fullName, faker, httpClient),
            BiometricTemplate = GenerateFakeBiometricTemplate(faker)
        };

        using var command = new MySqlCommand(@"
            INSERT INTO Employees
            (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, ProfileImage, BiometricTemplate)
            VALUES
            (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @ProfileImage, @BiometricTemplate)", connection);

        command.Parameters.AddWithValue("@EmployeeCode", employee.EmployeeCode);
        command.Parameters.AddWithValue("@FullName", employee.FullName);
        command.Parameters.AddWithValue("@Email", employee.Email);
        command.Parameters.AddWithValue("@Phone", employee.Phone);
        command.Parameters.AddWithValue("@Position", employee.Position);
        command.Parameters.AddWithValue("@Department", employee.Department);
        command.Parameters.AddWithValue("@HourlyRate", employee.HourlyRate);
        command.Parameters.AddWithValue("@HireDate", employee.HireDate);
        command.Parameters.AddWithValue("@IsActive", employee.IsActive);
        command.Parameters.AddWithValue("@ProfileImage", employee.ProfileImage);
        command.Parameters.AddWithValue("@BiometricTemplate", employee.BiometricTemplate);
        command.ExecuteNonQuery();

        seededEmployees.Add(new SeededEmployee(
            Convert.ToInt32(command.LastInsertedId),
            employee.EmployeeCode,
            employee.FullName,
            employee.HourlyRate));

        generated++;
        Console.WriteLine($"Inserted {employee.EmployeeCode} | {employee.FullName}");
    }

    Console.WriteLine($"Done. Inserted {generated} fake employee profile(s).");
    return seededEmployees;
}

static void SeedFakeAttendances(string connectionString, Faker faker, IReadOnlyCollection<SeededEmployee> employees)
{
    if (employees.Count == 0)
    {
        return;
    }

    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    var inserted = 0;
    var startDate = DateTime.Today.AddDays(-14).Date;
    var endDate = DateTime.Today.Date;

    foreach (var employee in employees)
    {
        for (var attendanceDate = startDate; attendanceDate <= endDate; attendanceDate = attendanceDate.AddDays(1))
        {
            if (attendanceDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (faker.Random.Bool(0.12f))
            {
                continue;
            }

            var timeIn = attendanceDate
                .AddHours(8)
                .AddMinutes(faker.Random.Int(-10, 35));
            var overtimeHours = faker.Random.Bool(0.35f)
                ? faker.Random.Decimal(0.5m, 2.5m)
                : 0m;
            var timeOut = timeIn.AddHours(8).AddHours((double)overtimeHours).AddMinutes(faker.Random.Int(0, 12));
            var status = timeIn.TimeOfDay > new TimeSpan(8, 10, 0) ? "Late" : "Present";

            using var command = new MySqlCommand(@"
                INSERT INTO AttendanceRecords
                (EmployeeId, AttendanceDate, TimeIn, TimeOut, Status, IsBiometricVerified)
                VALUES
                (@EmployeeId, @AttendanceDate, @TimeIn, @TimeOut, @Status, @IsBiometricVerified)", connection);

            command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
            command.Parameters.AddWithValue("@AttendanceDate", attendanceDate);
            command.Parameters.AddWithValue("@TimeIn", timeIn);
            command.Parameters.AddWithValue("@TimeOut", timeOut);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@IsBiometricVerified", faker.Random.Bool(0.92f));
            command.ExecuteNonQuery();
            inserted++;
        }
    }

    Console.WriteLine($"Done. Inserted {inserted} fake attendance record(s).");
}

static void SeedFakePayrollRecords(string connectionString, Faker faker, IReadOnlyCollection<SeededEmployee> employees)
{
    if (employees.Count == 0)
    {
        return;
    }

    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    var inserted = 0;
    foreach (var employee in employees)
    {
        foreach (var period in GetRecentPayrollPeriods())
        {
            var regularHours = faker.Random.Decimal(72m, 88m);
            var overtimeHours = faker.Random.Bool(0.5f)
                ? faker.Random.Decimal(2m, 10m)
                : 0m;
            var grossPay = Math.Round((regularHours + (overtimeHours * 1.25m)) * employee.HourlyRate, 2);
            var deductions = Math.Round(grossPay * faker.Random.Decimal(0.06m, 0.14m), 2);
            var netPay = grossPay - deductions;

            using var command = new MySqlCommand(@"
                INSERT INTO PayrollRecords
                (EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours, GrossPay, Deductions, NetPay, Status)
                VALUES
                (@EmployeeId, @PayPeriodStart, @PayPeriodEnd, @RegularHours, @OvertimeHours, @GrossPay, @Deductions, @NetPay, @Status)", connection);

            command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
            command.Parameters.AddWithValue("@PayPeriodStart", period.Start);
            command.Parameters.AddWithValue("@PayPeriodEnd", period.End);
            command.Parameters.AddWithValue("@RegularHours", regularHours);
            command.Parameters.AddWithValue("@OvertimeHours", overtimeHours);
            command.Parameters.AddWithValue("@GrossPay", grossPay);
            command.Parameters.AddWithValue("@Deductions", deductions);
            command.Parameters.AddWithValue("@NetPay", netPay);
            command.Parameters.AddWithValue("@Status", faker.PickRandom("Pending", "Paid"));
            command.ExecuteNonQuery();
            inserted++;
        }
    }

    Console.WriteLine($"Done. Inserted {inserted} fake payroll record(s).");
}

static IReadOnlyList<(DateTime Start, DateTime End)> GetRecentPayrollPeriods()
{
    var currentEnd = DateTime.Today.Date.AddDays(-1);
    return
    [
        (currentEnd.AddDays(-13), currentEnd),
        (currentEnd.AddDays(-27), currentEnd.AddDays(-14))
    ];
}

static void PrintTableCounts(string connectionString)
{
    using var connection = new MySqlConnection(connectionString);
    connection.Open();

    foreach (var tableName in new[] { "Employees", "AttendanceRecords", "PayrollRecords", "UserAccounts" })
    {
        using var command = new MySqlCommand($"SELECT COUNT(*) FROM {tableName}", connection);
        var count = Convert.ToInt32(command.ExecuteScalar());
        Console.WriteLine($"{tableName}: {count}");
    }
}

static int GetNextEmployeeCodeSequence(MySqlConnection connection)
{
    using var command = new MySqlCommand(@"
        SELECT MAX(CAST(SUBSTRING(EmployeeCode, 5) AS UNSIGNED))
        FROM Employees
        WHERE EmployeeCode REGEXP '^EMP-[0-9]+$';", connection);

    var value = command.ExecuteScalar();
    if (value == null || value is DBNull) return 1;

    return Convert.ToInt32(value) + 1;
}

static void SeedImagesOnly(string connectionString, Faker faker, HttpClient httpClient)
{
    var employees = new List<(int Id, string FullName)>();

    using (var connection = new MySqlConnection(connectionString))
    using (var command = new MySqlCommand("SELECT EmployeeId, FullName FROM Employees ORDER BY EmployeeId", connection))
    {
        connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            employees.Add((Convert.ToInt32(reader["EmployeeId"]), Convert.ToString(reader["FullName"]) ?? "Unknown Person"));
        }
    }

    if (employees.Count == 0)
    {
        Console.WriteLine("No employees found. Nothing to seed.");
        return;
    }

    using var updateConnection = new MySqlConnection(connectionString);
    updateConnection.Open();

    foreach (var employee in employees)
    {
        var imageBytes = GenerateHumanProfileImage(employee.FullName, faker, httpClient);

        using var update = new MySqlCommand("UPDATE Employees SET ProfileImage = @ProfileImage WHERE EmployeeId = @EmployeeId", updateConnection);
        update.Parameters.AddWithValue("@EmployeeId", employee.Id);
        update.Parameters.AddWithValue("@ProfileImage", imageBytes);
        update.ExecuteNonQuery();

        Console.WriteLine($"Seeded image for EmployeeId={employee.Id}, Name={employee.FullName}");
    }

    Console.WriteLine($"Done. Seeded {employees.Count} employee profile images.");
}

static byte[] GenerateHumanProfileImage(string fullName, Faker faker, HttpClient httpClient)
{
    var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < 8; i++)
    {
        var gender = faker.PickRandom("men", "women");
        var index = faker.Random.Int(1, 99);
        var url = $"https://randomuser.me/api/portraits/{gender}/{index}.jpg";
        if (!tried.Add(url))
        {
            continue;
        }

        try
        {
            var bytes = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (bytes.Length > 0)
            {
                return bytes;
            }
        }
        catch
        {
            // Try a different portrait URL, then fallback to generated avatar below.
        }
    }

    return GenerateFakeProfileImage(fullName, faker);
}

internal sealed record SeededEmployee(int EmployeeId, string EmployeeCode, string FullName, decimal HourlyRate);
