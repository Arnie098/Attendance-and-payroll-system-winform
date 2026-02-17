using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.DataAccess
{
    public class PayrollRepository
    {
        public void AddPayroll(Payroll payroll)
        {
            const string sql = @"
                INSERT INTO PayrollRecords
                (EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours, GrossPay, Deductions, NetPay, Status)
                VALUES
                (@EmployeeId, @PayPeriodStart, @PayPeriodEnd, @RegularHours, @OvertimeHours, @GrossPay, @Deductions, @NetPay, @Status)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", payroll.EmployeeId);
            command.Parameters.AddWithValue("@PayPeriodStart", payroll.PayPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payroll.PayPeriodEnd.Date);
            command.Parameters.AddWithValue("@RegularHours", payroll.RegularHours);
            command.Parameters.AddWithValue("@OvertimeHours", payroll.OvertimeHours);
            command.Parameters.AddWithValue("@GrossPay", payroll.GrossPay);
            command.Parameters.AddWithValue("@Deductions", payroll.Deductions);
            command.Parameters.AddWithValue("@NetPay", payroll.NetPay);
            command.Parameters.AddWithValue("@Status", payroll.Status);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public List<Payroll> GetPayrollByEmployee(int employeeId)
        {
            var payrollList = new List<Payroll>();
            const string sql = @"
                SELECT p.PayrollId, p.EmployeeId, p.PayPeriodStart, p.PayPeriodEnd,
                       p.RegularHours, p.OvertimeHours, p.GrossPay, p.Deductions, p.NetPay, p.Status,
                       e.FullName, e.EmployeeCode
                FROM PayrollRecords p
                INNER JOIN Employees e ON e.EmployeeId = p.EmployeeId
                WHERE p.EmployeeId = @EmployeeId
                ORDER BY p.PayPeriodEnd DESC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                payrollList.Add(new Payroll
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
                    Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                    EmployeeName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                    EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty
                });
            }

            return payrollList;
        }

        public void UpdatePayroll(Payroll payroll)
        {
            const string sql = @"
                UPDATE PayrollRecords
                SET PayPeriodStart = @PayPeriodStart,
                    PayPeriodEnd = @PayPeriodEnd,
                    RegularHours = @RegularHours,
                    OvertimeHours = @OvertimeHours,
                    GrossPay = @GrossPay,
                    Deductions = @Deductions,
                    NetPay = @NetPay,
                    Status = @Status
                WHERE PayrollId = @PayrollId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payroll.PayrollId);
            command.Parameters.AddWithValue("@PayPeriodStart", payroll.PayPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payroll.PayPeriodEnd.Date);
            command.Parameters.AddWithValue("@RegularHours", payroll.RegularHours);
            command.Parameters.AddWithValue("@OvertimeHours", payroll.OvertimeHours);
            command.Parameters.AddWithValue("@GrossPay", payroll.GrossPay);
            command.Parameters.AddWithValue("@Deductions", payroll.Deductions);
            command.Parameters.AddWithValue("@NetPay", payroll.NetPay);
            command.Parameters.AddWithValue("@Status", payroll.Status);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void DeletePayroll(int payrollId)
        {
            const string sql = "DELETE FROM PayrollRecords WHERE PayrollId = @PayrollId";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payrollId);
            connection.Open();
            command.ExecuteNonQuery();
        }
    }
}
