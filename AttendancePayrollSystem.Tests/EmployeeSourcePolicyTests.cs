using System;
using Xunit;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem.Tests
{
    /// <summary>
    /// Tests for EmployeeSourcePolicy static methods.
    /// Validates school-managed employee detection and policy enforcement.
    /// </summary>
    public class EmployeeSourcePolicyTests
    {
        #region IsSchoolManagedEmployee Tests

        [Fact]
        public void IsSchoolManagedEmployee_NullEmployee_ShouldReturnFalse()
        {
            var result = EmployeeSourcePolicy.IsSchoolManagedEmployee(null);
            Assert.False(result);
        }

        [Fact]
        public void IsSchoolManagedEmployee_NoSourceIds_ShouldReturnFalse()
        {
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "Test Employee",
                SourceTeacherId = null,
                SourceUserId = null
            };

            var result = EmployeeSourcePolicy.IsSchoolManagedEmployee(employee);
            Assert.False(result);
        }

        [Fact]
        public void IsSchoolManagedEmployee_HasSourceTeacherId_ShouldReturnTrue()
        {
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "Teacher Employee",
                SourceTeacherId = 100,
                SourceUserId = null
            };

            var result = EmployeeSourcePolicy.IsSchoolManagedEmployee(employee);
            Assert.True(result);
        }

        [Fact]
        public void IsSchoolManagedEmployee_HasSourceUserId_ShouldReturnTrue()
        {
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "User Employee",
                SourceTeacherId = null,
                SourceUserId = 200
            };

            var result = EmployeeSourcePolicy.IsSchoolManagedEmployee(employee);
            Assert.True(result);
        }

        [Fact]
        public void IsSchoolManagedEmployee_HasBothSourceIds_ShouldReturnTrue()
        {
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "Both Sources",
                SourceTeacherId = 100,
                SourceUserId = 200
            };

            var result = EmployeeSourcePolicy.IsSchoolManagedEmployee(employee);
            Assert.True(result);
        }

        #endregion

        #region EnsureLocalEmployeeManagementAllowed Tests

        [Fact]
        public void EnsureLocalEmployeeManagementAllowed_WhenNotExclusive_ShouldNotThrow()
        {
            // When ATTENDANCE_SCHOOL_EMPLOYEES_ONLY is not set or false,
            // local management should be allowed
            // Clear the env var to ensure it's not set
            var originalValue = Environment.GetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY");

            try
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", "false");

                var exception = Record.Exception(() =>
                    EmployeeSourcePolicy.EnsureLocalEmployeeManagementAllowed("Create employee"));

                Assert.Null(exception);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", originalValue);
            }
        }

        [Fact]
        public void EnsureLocalEmployeeManagementAllowed_WhenExclusive_ShouldThrow()
        {
            var originalValue = Environment.GetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY");

            try
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", "true");
                // Force reload of the static property by clearing any cached state
                // Note: UseSchoolAsExclusiveSource reads env var directly each time

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    EmployeeSourcePolicy.EnsureLocalEmployeeManagementAllowed("Create employee"));

                Assert.Contains("Create employee", exception.Message);
                Assert.Contains("disabled", exception.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", originalValue);
            }
        }

        #endregion

        #region EnsureEmployeeRegistrationAllowed Tests

        [Fact]
        public void EnsureEmployeeRegistrationAllowed_WhenNotExclusive_ShouldNotThrow()
        {
            var originalValue = Environment.GetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY");

            try
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", "false");

                var exception = Record.Exception(() =>
                    EmployeeSourcePolicy.EnsureEmployeeRegistrationAllowed());

                Assert.Null(exception);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", originalValue);
            }
        }

        [Fact]
        public void EnsureEmployeeRegistrationAllowed_WhenExclusive_ShouldThrow()
        {
            var originalValue = Environment.GetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY");

            try
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", "true");

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    EmployeeSourcePolicy.EnsureEmployeeRegistrationAllowed());

                Assert.Contains("registration", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", originalValue);
            }
        }

        #endregion

        #region UseSchoolAsExclusiveSource Tests

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("TRUE", true)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("Yes", true)]
        [InlineData("false", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        [InlineData("", false)]
        public void UseSchoolAsExclusiveSource_ShouldParseBooleanValues(string envValue, bool expected)
        {
            var originalValue = Environment.GetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY");

            try
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", envValue);

                var result = EmployeeSourcePolicy.UseSchoolAsExclusiveSource;
                Assert.Equal(expected, result);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ATTENDANCE_SCHOOL_EMPLOYEES_ONLY", originalValue);
            }
        }

        #endregion

        #region Message Property Tests

        [Fact]
        public void EmployeeManagementMessage_ShouldNotBeNullOrEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(EmployeeSourcePolicy.EmployeeManagementMessage));
        }

        [Fact]
        public void RegistrationMessage_ShouldNotBeNullOrEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(EmployeeSourcePolicy.RegistrationMessage));
        }

        [Fact]
        public void LinkedEmployeeEditMessage_ShouldNotBeNullOrEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(EmployeeSourcePolicy.LinkedEmployeeEditMessage));
        }

        [Fact]
        public void LinkedEmployeeDeleteMessage_ShouldNotBeNullOrEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(EmployeeSourcePolicy.LinkedEmployeeDeleteMessage));
        }

        #endregion
    }
}
