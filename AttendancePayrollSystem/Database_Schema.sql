IF DB_ID('AttendancePayrollDb') IS NULL
BEGIN
    CREATE DATABASE AttendancePayrollDb;
END
GO

USE AttendancePayrollDb;
GO

IF OBJECT_ID('dbo.PayrollRecords', 'U') IS NOT NULL DROP TABLE dbo.PayrollRecords;
IF OBJECT_ID('dbo.AttendanceRecords', 'U') IS NOT NULL DROP TABLE dbo.AttendanceRecords;
IF OBJECT_ID('dbo.Employees', 'U') IS NOT NULL DROP TABLE dbo.Employees;
GO

CREATE TABLE dbo.Employees
(
    EmployeeId INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeCode NVARCHAR(20) NOT NULL UNIQUE,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NULL,
    Phone NVARCHAR(50) NULL,
    Position NVARCHAR(100) NULL,
    Department NVARCHAR(100) NULL,
    HourlyRate DECIMAL(18,2) NOT NULL,
    HireDate DATE NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT 1,
    ProfileImage VARBINARY(MAX) NULL,
    BiometricTemplate VARBINARY(MAX) NULL
);
GO

CREATE TABLE dbo.AttendanceRecords
(
    AttendanceId INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId INT NOT NULL,
    AttendanceDate DATE NOT NULL,
    TimeIn DATETIME NULL,
    TimeOut DATETIME NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Attendance_Status DEFAULT 'Present',
    IsBiometricVerified BIT NOT NULL CONSTRAINT DF_Attendance_Bio DEFAULT 0,
    CONSTRAINT FK_Attendance_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId),
    CONSTRAINT UQ_Attendance_EmployeeDate UNIQUE (EmployeeId, AttendanceDate)
);
GO

CREATE TABLE dbo.PayrollRecords
(
    PayrollId INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId INT NOT NULL,
    PayPeriodStart DATE NOT NULL,
    PayPeriodEnd DATE NOT NULL,
    RegularHours DECIMAL(10,2) NOT NULL,
    OvertimeHours DECIMAL(10,2) NOT NULL,
    GrossPay DECIMAL(18,2) NOT NULL,
    Deductions DECIMAL(18,2) NOT NULL,
    NetPay DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Payroll_Status DEFAULT 'Pending',
    CreatedAt DATETIME NOT NULL CONSTRAINT DF_Payroll_CreatedAt DEFAULT GETDATE(),
    CONSTRAINT FK_Payroll_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO

INSERT INTO dbo.Employees
(
    EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive
)
VALUES
('EMP-001', 'Alex Santos', 'alex.santos@company.com', '09170000001', 'Software Engineer', 'IT', 250.00, '2023-02-01', 1),
('EMP-002', 'Bianca Reyes', 'bianca.reyes@company.com', '09170000002', 'HR Specialist', 'HR', 220.00, '2022-11-15', 1),
('EMP-003', 'Carlo Dizon', 'carlo.dizon@company.com', '09170000003', 'Accountant', 'Finance', 230.00, '2021-08-10', 1);
GO
