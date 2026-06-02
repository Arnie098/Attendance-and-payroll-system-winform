using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class PayrollRepository
    {
        public void AddPayroll(Payroll payroll)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.InsertAndReturnSingle<Payroll>("payrollrecords", BuildPayrollPayload(payroll));
                return;
            }

            const string sql = @"
                INSERT INTO PayrollRecords
                (EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours, GrossPay, Deductions, NetPay, ManualDeduction, ManualDeductionNote, Status)
                VALUES
                (@EmployeeId, @PayPeriodStart, @PayPeriodEnd, @RegularHours, @OvertimeHours, @GrossPay, @Deductions, @NetPay, @ManualDeduction, @ManualDeductionNote, @Status)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", payroll.EmployeeId);
            command.Parameters.AddWithValue("@PayPeriodStart", payroll.PayPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payroll.PayPeriodEnd.Date);
            command.Parameters.AddWithValue("@RegularHours", payroll.RegularHours);
            command.Parameters.AddWithValue("@OvertimeHours", payroll.OvertimeHours);
            command.Parameters.AddWithValue("@GrossPay", payroll.GrossPay);
            command.Parameters.AddWithValue("@Deductions", payroll.Deductions);
            command.Parameters.AddWithValue("@NetPay", payroll.NetPay);
            command.Parameters.AddWithValue("@ManualDeduction", payroll.ManualDeduction);
            command.Parameters.AddWithValue("@ManualDeductionNote", payroll.ManualDeductionNote ?? string.Empty);
            command.Parameters.AddWithValue("@Status", payroll.Status);
            connection.Open();
            command.ExecuteNonQuery();
            MySqlOfflineSyncService.QueuePayrollUpsert(Convert.ToInt32(command.LastInsertedId), payroll.EmployeeId);
        }

        public List<Payroll> GetPayrollByEmployee(int employeeId)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetPayrollByEmployeeViaApi(employeeId);
            }

            var payrollList = new List<Payroll>();
            const string sql = @"
                SELECT p.PayrollId, p.EmployeeId, p.PayPeriodStart, p.PayPeriodEnd,
                       p.RegularHours, p.OvertimeHours, p.GrossPay, p.Deductions, p.NetPay,
                       p.ManualDeduction, p.ManualDeductionNote, p.Status,
                       e.FullName, e.EmployeeCode
                FROM PayrollRecords p
                INNER JOIN Employees e ON e.EmployeeId = p.EmployeeId
                WHERE p.EmployeeId = @EmployeeId
                ORDER BY p.PayPeriodEnd DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                payrollList.Add(MapPayroll(reader));
            }

            return payrollList;
        }

        public Payroll? GetPayrollByEmployeeAndPeriod(int employeeId, DateTime payPeriodStart, DateTime payPeriodEnd)
        {
            if (SupabaseConfig.UseApi)
            {
                return GetPayrollByEmployeeAndPeriodViaApi(employeeId, payPeriodStart, payPeriodEnd);
            }

            const string sql = @"
                SELECT p.PayrollId, p.EmployeeId, p.PayPeriodStart, p.PayPeriodEnd,
                       p.RegularHours, p.OvertimeHours, p.GrossPay, p.Deductions, p.NetPay,
                       p.ManualDeduction, p.ManualDeductionNote, p.Status,
                       e.FullName, e.EmployeeCode
                FROM PayrollRecords p
                INNER JOIN Employees e ON e.EmployeeId = p.EmployeeId
                WHERE p.EmployeeId = @EmployeeId
                  AND p.PayPeriodStart = @PayPeriodStart
                  AND p.PayPeriodEnd = @PayPeriodEnd
                LIMIT 1";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.Parameters.AddWithValue("@PayPeriodStart", payPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payPeriodEnd.Date);
            connection.Open();
            using var reader = command.ExecuteReader();

            return reader.Read() ? MapPayroll(reader) : null;
        }

        public void UpdatePayroll(Payroll payroll)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.Update(
                    "payrollrecords",
                    BuildPayrollPayload(payroll),
                    new Dictionary<string, string>
                    {
                        ["payrollid"] = $"eq.{payroll.PayrollId}"
                    });
                return;
            }

            const string sql = @"
                UPDATE PayrollRecords
                SET PayPeriodStart = @PayPeriodStart,
                    PayPeriodEnd = @PayPeriodEnd,
                    RegularHours = @RegularHours,
                    OvertimeHours = @OvertimeHours,
                    GrossPay = @GrossPay,
                    Deductions = @Deductions,
                    NetPay = @NetPay,
                    ManualDeduction = @ManualDeduction,
                    ManualDeductionNote = @ManualDeductionNote,
                    Status = @Status
                WHERE PayrollId = @PayrollId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payroll.PayrollId);
            command.Parameters.AddWithValue("@PayPeriodStart", payroll.PayPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payroll.PayPeriodEnd.Date);
            command.Parameters.AddWithValue("@RegularHours", payroll.RegularHours);
            command.Parameters.AddWithValue("@OvertimeHours", payroll.OvertimeHours);
            command.Parameters.AddWithValue("@GrossPay", payroll.GrossPay);
            command.Parameters.AddWithValue("@Deductions", payroll.Deductions);
            command.Parameters.AddWithValue("@NetPay", payroll.NetPay);
            command.Parameters.AddWithValue("@ManualDeduction", payroll.ManualDeduction);
            command.Parameters.AddWithValue("@ManualDeductionNote", payroll.ManualDeductionNote ?? string.Empty);
            command.Parameters.AddWithValue("@Status", payroll.Status);
            connection.Open();
            command.ExecuteNonQuery();
            MySqlOfflineSyncService.QueuePayrollUpsert(payroll.PayrollId, payroll.EmployeeId);
        }

        public void DeletePayroll(int payrollId)
        {
            if (SupabaseConfig.UseApi)
            {
                SupabaseRestClient.Delete(
                    "payrollrecords",
                    new Dictionary<string, string>
                    {
                        ["payrollid"] = $"eq.{payrollId}"
                    });
                return;
            }

            const string sql = "DELETE FROM PayrollRecords WHERE PayrollId = @PayrollId";

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            var employeeId = GetEmployeeId(connection, payrollId);

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payrollId);
            command.ExecuteNonQuery();

            if (employeeId.HasValue)
            {
                MySqlOfflineSyncService.QueuePayrollDelete(payrollId, employeeId.Value);
            }
        }

        private static List<Payroll> GetPayrollByEmployeeViaApi(int employeeId)
        {
            var payrolls = SupabaseRestClient.GetList<Payroll>(
                "payrollrecords",
                new Dictionary<string, string>
                {
                    ["select"] = "payrollid,employeeid,payperiodstart,payperiodend,regularhours,overtimehours,grosspay,deductions,netpay,status",
                    ["employeeid"] = $"eq.{employeeId}",
                    ["order"] = "payperiodend.desc"
                });

            var employee = new EmployeeRepository().GetEmployeeById(employeeId);
            foreach (var payroll in payrolls)
            {
                payroll.EmployeeName = employee?.FullName ?? string.Empty;
                payroll.EmployeeCode = employee?.EmployeeCode ?? string.Empty;
            }

            return payrolls;
        }

        private static Payroll? GetPayrollByEmployeeAndPeriodViaApi(int employeeId, DateTime payPeriodStart, DateTime payPeriodEnd)
        {
            var payroll = SupabaseRestClient.GetSingleOrDefault<Payroll>(
                "payrollrecords",
                new Dictionary<string, string>
                {
                    ["select"] = "payrollid,employeeid,payperiodstart,payperiodend,regularhours,overtimehours,grosspay,deductions,netpay,status",
                    ["employeeid"] = $"eq.{employeeId}",
                    ["payperiodstart"] = $"eq.{payPeriodStart:yyyy-MM-dd}",
                    ["payperiodend"] = $"eq.{payPeriodEnd:yyyy-MM-dd}",
                    ["limit"] = "1"
                });

            if (payroll == null)
            {
                return null;
            }

            var employee = new EmployeeRepository().GetEmployeeById(employeeId);
            payroll.EmployeeName = employee?.FullName ?? string.Empty;
            payroll.EmployeeCode = employee?.EmployeeCode ?? string.Empty;
            return payroll;
        }

        private static object BuildPayrollPayload(Payroll payroll)
        {
            return new
            {
                employeeid = payroll.EmployeeId,
                payperiodstart = payroll.PayPeriodStart.Date,
                payperiodend = payroll.PayPeriodEnd.Date,
                regularhours = payroll.RegularHours,
                overtimehours = payroll.OvertimeHours,
                grosspay = payroll.GrossPay,
                deductions = payroll.Deductions,
                netpay = payroll.NetPay,
                manualdeduction = payroll.ManualDeduction,
                manualdeductionnote = payroll.ManualDeductionNote ?? string.Empty,
                status = payroll.Status
            };
        }

        private static Payroll MapPayroll(MySqlDataReader reader)
        {
            return new Payroll
            {
                PayrollId = Convert.ToInt32(reader["PayrollId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                PayPeriodStart = Convert.ToDateTime(reader["PayPeriodStart"]),
                PayPeriodEnd = Convert.ToDateTime(reader["PayPeriodEnd"]),
                RegularHours = Convert.ToDecimal(reader["RegularHours"]),
                OvertimeHours = Convert.ToDecimal(reader["OvertimeHours"]),
                GrossPay = Convert.ToDecimal(reader["GrossPay"]),
                Deductions = Convert.ToDecimal(reader["Deductions"]),
                NetPay = Convert.ToDecimal(reader["NetPay"]),
                ManualDeduction = reader["ManualDeduction"] is DBNull ? 0m : Convert.ToDecimal(reader["ManualDeduction"]),
                ManualDeductionNote = reader["ManualDeductionNote"] is DBNull ? string.Empty : Convert.ToString(reader["ManualDeductionNote"]) ?? string.Empty,
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                EmployeeName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty
            };
        }

        private static int? GetEmployeeId(MySqlConnection connection, int payrollId)
        {
            using var command = new MySqlCommand(
                "SELECT EmployeeId FROM PayrollRecords WHERE PayrollId = @PayrollId LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@PayrollId", payrollId);
            var result = command.ExecuteScalar();
            return result == null || result is DBNull ? null : Convert.ToInt32(result);
        }
    }
}
