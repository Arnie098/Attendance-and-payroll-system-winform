using Bogus;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;

const string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=AttendancePayrollDb;Trusted_Connection=True;TrustServerCertificate=True;";

var faker = new Faker();
var employees = new List<(int Id, string FullName)>();

using (var connection = new SqlConnection(connectionString))
using (var command = new SqlCommand("SELECT EmployeeId, FullName FROM Employees ORDER BY EmployeeId", connection))
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

using (var connection = new SqlConnection(connectionString))
{
    connection.Open();

    foreach (var employee in employees)
    {
        var imageBytes = GenerateFakeProfileImage(employee.FullName, faker);

        using var update = new SqlCommand("UPDATE Employees SET ProfileImage = @ProfileImage WHERE EmployeeId = @EmployeeId", connection);
        update.Parameters.AddWithValue("@EmployeeId", employee.Id);
        update.Parameters.AddWithValue("@ProfileImage", imageBytes);
        update.ExecuteNonQuery();

        Console.WriteLine($"Seeded image for EmployeeId={employee.Id}, Name={employee.FullName}");
    }
}

Console.WriteLine($"Done. Seeded {employees.Count} employee profile images.");

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

static string GetInitials(string fullName)
{
    var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) return "NA";
    if (parts.Length == 1) return parts[0][0].ToString().ToUpperInvariant();
    return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
}
