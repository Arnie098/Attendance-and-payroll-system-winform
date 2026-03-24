-- MySQL schema for Attendance Payroll.
-- Import this into Hostinger phpMyAdmin for the configured database.

CREATE TABLE IF NOT EXISTS Employees
(
    EmployeeId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    EmployeeCode VARCHAR(20) NOT NULL UNIQUE,
    FullName VARCHAR(150) NOT NULL,
    Email VARCHAR(150) NULL,
    Phone VARCHAR(50) NULL,
    Position VARCHAR(100) NULL,
    Department VARCHAR(100) NULL,
    HourlyRate DECIMAL(18, 2) NOT NULL,
    HireDate DATE NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    SourceTeacherId BIGINT NULL,
    SourceUserId BIGINT NULL,
    ProfileImage LONGBLOB NULL,
    BiometricTemplate LONGBLOB NULL,
    UNIQUE KEY UQ_Employees_SourceTeacherId (SourceTeacherId),
    UNIQUE KEY UQ_Employees_SourceUserId (SourceUserId)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS AttendanceRecords
(
    AttendanceId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    EmployeeId INT NOT NULL,
    AttendanceDate DATE NOT NULL,
    TimeIn DATETIME NULL,
    TimeOut DATETIME NULL,
    Status VARCHAR(30) NOT NULL DEFAULT 'Present',
    IsBiometricVerified BOOLEAN NOT NULL DEFAULT FALSE,
    INDEX IDX_AttendanceRecords_EmployeeId (EmployeeId),
    CONSTRAINT FK_AttendanceRecords_Employees FOREIGN KEY (EmployeeId)
        REFERENCES Employees(EmployeeId),
    CONSTRAINT UQ_Attendance_EmployeeDate UNIQUE (EmployeeId, AttendanceDate)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS PayrollRecords
(
    PayrollId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    EmployeeId INT NOT NULL,
    PayPeriodStart DATE NOT NULL,
    PayPeriodEnd DATE NOT NULL,
    RegularHours DECIMAL(10, 2) NOT NULL,
    OvertimeHours DECIMAL(10, 2) NOT NULL,
    GrossPay DECIMAL(18, 2) NOT NULL,
    Deductions DECIMAL(18, 2) NOT NULL,
    NetPay DECIMAL(18, 2) NOT NULL,
    Status VARCHAR(30) NOT NULL DEFAULT 'Pending',
    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX IDX_PayrollRecords_EmployeeId (EmployeeId),
    CONSTRAINT FK_PayrollRecords_Employees FOREIGN KEY (EmployeeId)
        REFERENCES Employees(EmployeeId)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS UserAccounts
(
    UserAccountId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(80) NOT NULL UNIQUE,
    PasswordHash VARCHAR(256) NOT NULL,
    PasswordSalt VARBINARY(64) NULL,
    HashIterations INT NOT NULL DEFAULT 210000,
    Role VARCHAR(20) NOT NULL,
    EmployeeId INT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX IDX_UserAccounts_Role (Role),
    CONSTRAINT FK_UserAccounts_Employee FOREIGN KEY (EmployeeId)
        REFERENCES Employees(EmployeeId)
        ON DELETE CASCADE
) ENGINE=InnoDB;
