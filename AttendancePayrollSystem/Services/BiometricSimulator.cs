using System;
using System.Threading.Tasks;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem.Services
{
    public class BiometricSimulator
    {
        private readonly Random _random = new();

        public async Task<BiometricResult> SimulateFingerprint(int employeeId)
        {
            await Task.Delay(DatabaseConfig.BiometricSimulationDelay);

            var success = _random.Next(100) > 5;

            return new BiometricResult
            {
                Success = success,
                EmployeeId = employeeId,
                Timestamp = DateTime.Now,
                Message = success ? "Fingerprint verified successfully" : "Fingerprint verification failed"
            };
        }

        public byte[] GenerateMockBiometricData()
        {
            var data = new byte[256];
            _random.NextBytes(data);
            return data;
        }
    }

    public class BiometricResult
    {
        public bool Success { get; set; }
        public int EmployeeId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
