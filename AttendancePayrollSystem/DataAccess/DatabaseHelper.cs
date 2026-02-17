using System;
using System.Configuration;
using System.Data.SqlClient;

namespace AttendancePayrollSystem.DataAccess
{
    public static class DatabaseHelper
    {
        private static readonly string _connectionString =
            ConfigurationManager.ConnectionStrings["AttendanceDb"]?.ConnectionString
            ?? throw new InvalidOperationException("Missing connection string: AttendanceDb");

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}
