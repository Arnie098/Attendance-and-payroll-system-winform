using System;
using System.Configuration;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public static class EmployeeSourcePolicy
    {
        private const string ExclusiveSourceEnvVar = "ATTENDANCE_SCHOOL_EMPLOYEES_ONLY";
        private const string ExclusiveSourceSetting = "SchoolEmployeesOnly";

        public static bool SchoolSyncEnabled =>
            !SupabaseConfig.UseApi && SchoolDatabaseHelper.IsConfigured();

        public static bool UseSchoolAsExclusiveSource =>
            SchoolSyncEnabled && GetOptionalBool(ExclusiveSourceSetting, ExclusiveSourceEnvVar);

        public static string EmployeeManagementMessage =>
            UseSchoolAsExclusiveSource
                ? "Employees are managed from the school management database while SCHOOL_DB_CONNECTION is configured. Use Refresh to sync teachers instead of creating, editing, or deleting local employee records."
                : "School teacher sync is enabled. Linked teacher records still refresh from the school management database, and local employees remain manageable here.";

        public static string RegistrationMessage =>
            "Employee self-registration is disabled because the school management database is the source of truth for employees.";

        public static string LinkedEmployeeEditMessage =>
            "This employee is linked to the school management database. Employee code, name, email, phone, hire date, and active status are refreshed from school data. Position, department, and hourly rate stay local.";

        public static string LinkedEmployeeDeleteMessage =>
            "This employee is linked to the school management database. Deleting it here removes the local copy only. It will be recreated on the next school sync unless removed from the school system.";

        public static bool IsSchoolManagedEmployee(Employee? employee)
        {
            return employee?.SourceTeacherId.HasValue == true ||
                   employee?.SourceUserId.HasValue == true;
        }

        public static void EnsureLocalEmployeeManagementAllowed(string action)
        {
            if (!UseSchoolAsExclusiveSource)
            {
                return;
            }

            throw new InvalidOperationException($"{action} is disabled. {EmployeeManagementMessage}");
        }

        public static void EnsureEmployeeRegistrationAllowed()
        {
            if (!UseSchoolAsExclusiveSource)
            {
                return;
            }

            throw new InvalidOperationException(RegistrationMessage);
        }

        private static bool GetOptionalBool(string appSettingKey, string envVar)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = ConfigurationManager.AppSettings[appSettingKey];
            }

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
