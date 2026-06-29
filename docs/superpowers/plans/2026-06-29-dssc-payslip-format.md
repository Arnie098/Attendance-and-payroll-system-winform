# DSSC Payslip Format Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the payslip generator to match the DSSC two-column body layout (employee info card left, itemized deductions table right) driven by flexible per-payroll line items stored in a new `PayrollLineItems` DB table.

**Architecture:** New `PayrollLineItem` model + `PayrollLineItemRepository` handle flexible line items (Earning / Deduction / EmployerContribution). `DatabaseHelper.EnsureCoreSchema` gains three new migration helpers following the existing `EnsurePayrollColumnExists` pattern. `PayslipDocument.Build()` is rebuilt with a two-column WPF FlowDocument `Table`. Employee government-ID fields and branding certifying-officer fields are added via `ALTER TABLE` migrations.

**Tech Stack:** C# .NET 8 WPF, MySqlConnector (SQLite + MySQL), xUnit, existing raw-SQL repository pattern.

**Spec:** `docs/superpowers/specs/2026-06-29-dssc-payslip-format-design.md`

---

## File Map

| Status | File | Change |
|---|---|---|
| Create | `AttendancePayrollSystem/Models/PayrollLineItemType.cs` | New enum |
| Create | `AttendancePayrollSystem/Models/PayrollLineItem.cs` | New model |
| Modify | `AttendancePayrollSystem/Models/Employee.cs` | +10 properties |
| Modify | `AttendancePayrollSystem/Models/AppBranding.cs` | +2 properties |
| Modify | `AttendancePayrollSystem/DataAccess/DatabaseHelper.cs` | +3 migration helpers, call in EnsureCoreSchema |
| Create | `AttendancePayrollSystem/DataAccess/PayrollLineItemRepository.cs` | New repo |
| Modify | `AttendancePayrollSystem/DataAccess/EmployeeRepository.cs` | Extend SELECT/INSERT/UPDATE/MapEmployee |
| Modify | `AttendancePayrollSystem/DataAccess/AppBrandingRepository.cs` | Extend SELECT, add UpdateCertifyingOfficer |
| Create | `AttendancePayrollSystem.Tests/PayslipDocumentTests.cs` | Tests for Build() |
| Modify | `AttendancePayrollSystem/Services/PayslipDocument.cs` | Rebuild Build(), update Print/SaveAsPdf signatures |
| Modify | `AttendancePayrollSystem/PayrollModal.xaml` | Add line items DataGrid |
| Modify | `AttendancePayrollSystem/PayrollModal.xaml.cs` | Wire DataGrid save/load/print |
| Modify | `AttendancePayrollSystem/EmployeePayrollWindow.xaml.cs` | Pass line items to Print/SaveAsPdf |
| Modify | `AttendancePayrollSystem/EmployeeModal.xaml` | Add government IDs GroupBox |
| Modify | `AttendancePayrollSystem/EmployeeModal.xaml.cs` | Load/save new fields |
| Create | `AttendancePayrollSystem/SettingsWindow.xaml` | Certifying officer form |
| Create | `AttendancePayrollSystem/SettingsWindow.xaml.cs` | Load/save via AppBrandingRepository |
| Modify | `AttendancePayrollSystem/AdminWindow.xaml` (or main menu) | Add Settings menu item |

---

## Task 1: PayrollLineItem Model and Enum

**Files:**
- Create: `AttendancePayrollSystem/Models/PayrollLineItemType.cs`
- Create: `AttendancePayrollSystem/Models/PayrollLineItem.cs`

- [ ] **Step 1: Create the enum**

Create `AttendancePayrollSystem/Models/PayrollLineItemType.cs`:

```csharp
namespace AttendancePayrollSystem.Models
{
    public enum PayrollLineItemType
    {
        Earning = 0,
        Deduction = 1,
        EmployerContribution = 2
    }
}
```

- [ ] **Step 2: Create the model**

Create `AttendancePayrollSystem/Models/PayrollLineItem.cs`:

```csharp
namespace AttendancePayrollSystem.Models
{
    public class PayrollLineItem
    {
        public int Id { get; set; }
        public int PayrollId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public PayrollLineItemType ItemType { get; set; } = PayrollLineItemType.Deduction;
        public int SortOrder { get; set; }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add AttendancePayrollSystem/Models/PayrollLineItemType.cs AttendancePayrollSystem/Models/PayrollLineItem.cs
git commit -m "feat: add PayrollLineItem model and PayrollLineItemType enum"
```

---

## Task 2: Extend Employee and AppBranding Models

**Files:**
- Modify: `AttendancePayrollSystem/Models/Employee.cs`
- Modify: `AttendancePayrollSystem/Models/AppBranding.cs`

- [ ] **Step 1: Add government-ID and payroll-info properties to Employee**

Open `AttendancePayrollSystem/Models/Employee.cs`. Add these properties after the existing `BiometricTemplate` property:

```csharp
        public string AgencyId { get; set; } = string.Empty;
        public string SalaryGrade { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string FundSource { get; set; } = string.Empty;
        public string PayrollCycle { get; set; } = "Monthly";
        public string TinNumber { get; set; } = string.Empty;
        public string SssNumber { get; set; } = string.Empty;
        public string GsisNumber { get; set; } = string.Empty;
        public string PagIbigNumber { get; set; } = string.Empty;
        public string PhilHealthNumber { get; set; } = string.Empty;
```

The complete class body should end with:

```csharp
        public byte[]? ProfileImage { get; set; }
        public byte[]? BiometricTemplate { get; set; }
        public string AgencyId { get; set; } = string.Empty;
        public string SalaryGrade { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string FundSource { get; set; } = string.Empty;
        public string PayrollCycle { get; set; } = "Monthly";
        public string TinNumber { get; set; } = string.Empty;
        public string SssNumber { get; set; } = string.Empty;
        public string GsisNumber { get; set; } = string.Empty;
        public string PagIbigNumber { get; set; } = string.Empty;
        public string PhilHealthNumber { get; set; } = string.Empty;
    }
```

- [ ] **Step 2: Add certifying officer properties to AppBranding**

Open `AttendancePayrollSystem/Models/AppBranding.cs`. Add after `LogoImage`:

```csharp
        public byte[]? LogoImage { get; set; }
        public string CertifyingOfficerName { get; set; } = string.Empty;
        public string CertifyingOfficerTitle { get; set; } = string.Empty;
    }
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AttendancePayrollSystem/Models/Employee.cs AttendancePayrollSystem/Models/AppBranding.cs
git commit -m "feat: extend Employee with govt ID fields and AppBranding with certifying officer"
```

---

## Task 3: Database Migrations

**Files:**
- Modify: `AttendancePayrollSystem/DataAccess/DatabaseHelper.cs`

The project uses `EnsureCoreSchema(connection, transaction)` called at startup. New columns are added via the existing `EnsureEmployeeColumnExists` / `EnsurePayrollColumnExists` pattern (idempotent ALTER TABLE). Add parallel helpers for branding and a new CREATE TABLE for PayrollLineItems.

- [ ] **Step 1: Add EnsureEmployeePayrollInfoColumns private method**

In `DatabaseHelper.cs`, add this method after `EnsurePayrollDeductionColumns`:

```csharp
        private static void EnsureEmployeePayrollInfoColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            var textDef = connection.Provider == DatabaseProvider.Sqlite ? "TEXT NOT NULL DEFAULT ''" : "VARCHAR(100) NOT NULL DEFAULT ''";
            EnsureEmployeeColumnExists(connection, transaction, "AgencyId", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "SalaryGrade", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "Designation", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "FundSource", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PayrollCycle", connection.Provider == DatabaseProvider.Sqlite
                ? "TEXT NOT NULL DEFAULT 'Monthly'"
                : "VARCHAR(50) NOT NULL DEFAULT 'Monthly'");
            EnsureEmployeeColumnExists(connection, transaction, "TinNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "SssNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "GsisNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PagIbigNumber", textDef);
            EnsureEmployeeColumnExists(connection, transaction, "PhilHealthNumber", textDef);
        }
```

- [ ] **Step 2: Add EnsureBrandingCertifyingColumns private method**

Add after `EnsureEmployeePayrollInfoColumns`:

```csharp
        private static void EnsureBrandingCertifyingColumns(MySqlConnection connection, MySqlTransaction transaction)
        {
            var textDef = connection.Provider == DatabaseProvider.Sqlite ? "TEXT NOT NULL DEFAULT ''" : "VARCHAR(255) NOT NULL DEFAULT ''";
            EnsureBrandingColumnExists(connection, transaction, "CertifyingOfficerName", textDef);
            EnsureBrandingColumnExists(connection, transaction, "CertifyingOfficerTitle", textDef);
        }

        private static void EnsureBrandingColumnExists(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string columnName,
            string definition)
        {
            if (ColumnExists(connection, "AppBrandingSettings", columnName, transaction))
            {
                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE AppBrandingSettings ADD COLUMN {columnName} {definition}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }
```

- [ ] **Step 3: Add EnsurePayrollLineItemsTable private method**

Add after `EnsureBrandingCertifyingColumns`:

```csharp
        private static void EnsurePayrollLineItemsTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    CREATE TABLE IF NOT EXISTS PayrollLineItems
                    (
                        Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        PayrollId INTEGER NOT NULL,
                        Label     TEXT    NOT NULL DEFAULT '',
                        Amount    REAL    NOT NULL DEFAULT 0,
                        ItemType  INTEGER NOT NULL DEFAULT 1,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (PayrollId) REFERENCES PayrollRecords(PayrollId) ON DELETE CASCADE
                    );"
                : @"
                    CREATE TABLE IF NOT EXISTS PayrollLineItems
                    (
                        Id        INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        PayrollId INT NOT NULL,
                        Label     VARCHAR(255) NOT NULL DEFAULT '',
                        Amount    DECIMAL(18, 2) NOT NULL DEFAULT 0,
                        ItemType  INT NOT NULL DEFAULT 1,
                        SortOrder INT NOT NULL DEFAULT 0,
                        CONSTRAINT FK_PayrollLineItems_PayrollRecords FOREIGN KEY (PayrollId)
                            REFERENCES PayrollRecords(PayrollId) ON DELETE CASCADE
                    ) ENGINE=InnoDB;";

            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }
```

- [ ] **Step 4: Call all three new helpers from EnsureCoreSchema**

Find the `EnsureCoreSchema` method. It currently ends with:

```csharp
            if (connection.Provider == DatabaseProvider.MySql)
            {
                EnsureEmployeeIntegrationColumns(connection, transaction);
                EnsureAttendanceTwoSessionColumns(connection, transaction);
                EnsurePayrollDeductionColumns(connection, transaction);
            }
            else
            {
                EnsurePayrollDeductionColumns(connection, transaction);
            }

            EnsureBrandingSeedRow(connection, transaction);
```

Replace that block with:

```csharp
            if (connection.Provider == DatabaseProvider.MySql)
            {
                EnsureEmployeeIntegrationColumns(connection, transaction);
                EnsureAttendanceTwoSessionColumns(connection, transaction);
                EnsurePayrollDeductionColumns(connection, transaction);
            }
            else
            {
                EnsurePayrollDeductionColumns(connection, transaction);
            }

            EnsureEmployeePayrollInfoColumns(connection, transaction);
            EnsureBrandingCertifyingColumns(connection, transaction);
            EnsurePayrollLineItemsTable(connection, transaction);
            EnsureBrandingSeedRow(connection, transaction);
```

- [ ] **Step 5: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add AttendancePayrollSystem/DataAccess/DatabaseHelper.cs
git commit -m "feat: add DB migrations for PayrollLineItems table, employee govt ID cols, branding certifying cols"
```

---

## Task 4: PayrollLineItemRepository

**Files:**
- Create: `AttendancePayrollSystem/DataAccess/PayrollLineItemRepository.cs`

- [ ] **Step 1: Create the repository**

Create `AttendancePayrollSystem/DataAccess/PayrollLineItemRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class PayrollLineItemRepository
    {
        public List<PayrollLineItem> GetByPayrollId(int payrollId)
        {
            var items = new List<PayrollLineItem>();
            const string sql = @"
                SELECT Id, PayrollId, Label, Amount, ItemType, SortOrder
                FROM PayrollLineItems
                WHERE PayrollId = @PayrollId
                ORDER BY SortOrder, Id";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payrollId);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                items.Add(MapLineItem(reader));
            }

            return items;
        }

        public void SaveLineItems(int payrollId, List<PayrollLineItem> items)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM PayrollLineItems WHERE PayrollId = @PayrollId",
                    connection,
                    transaction);
                deleteCommand.Parameters.AddWithValue("@PayrollId", payrollId);
                deleteCommand.ExecuteNonQuery();

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    const string insertSql = @"
                        INSERT INTO PayrollLineItems (PayrollId, Label, Amount, ItemType, SortOrder)
                        VALUES (@PayrollId, @Label, @Amount, @ItemType, @SortOrder)";

                    using var insertCommand = new MySqlCommand(insertSql, connection, transaction);
                    insertCommand.Parameters.AddWithValue("@PayrollId", payrollId);
                    insertCommand.Parameters.AddWithValue("@Label", item.Label.Trim());
                    insertCommand.Parameters.AddWithValue("@Amount", item.Amount);
                    insertCommand.Parameters.AddWithValue("@ItemType", (int)item.ItemType);
                    insertCommand.Parameters.AddWithValue("@SortOrder", i);
                    insertCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static PayrollLineItem MapLineItem(MySqlDataReader reader)
        {
            return new PayrollLineItem
            {
                Id = Convert.ToInt32(reader["Id"]),
                PayrollId = Convert.ToInt32(reader["PayrollId"]),
                Label = Convert.ToString(reader["Label"]) ?? string.Empty,
                Amount = Convert.ToDecimal(reader["Amount"]),
                ItemType = (PayrollLineItemType)Convert.ToInt32(reader["ItemType"]),
                SortOrder = Convert.ToInt32(reader["SortOrder"])
            };
        }
    }
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AttendancePayrollSystem/DataAccess/PayrollLineItemRepository.cs
git commit -m "feat: add PayrollLineItemRepository with GetByPayrollId and SaveLineItems"
```

---

## Task 5: Extend EmployeeRepository

**Files:**
- Modify: `AttendancePayrollSystem/DataAccess/EmployeeRepository.cs`

Add the 10 new columns to every SELECT, INSERT, UPDATE, and `MapEmployee`. The API path (`GetAllEmployeesViaApi`, `GetEmployeeByIdViaApi`, `AddEmployeeViaApi`, `UpdateEmployeeViaApi`) returns empty strings for new fields — no Supabase schema change needed.

- [ ] **Step 1: Update GetAllEmployees SELECT**

Find the SQL string starting with `SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,` in `GetAllEmployees`. Replace it with:

```csharp
            var sql = $@"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate,
                       AgencyId, SalaryGrade, Designation, FundSource, PayrollCycle,
                       TinNumber, SssNumber, GsisNumber, PagIbigNumber, PhilHealthNumber
                FROM Employees
                {sourceFilter}
                ORDER BY FullName";
```

- [ ] **Step 2: Update GetEmployeeById SELECT**

Find the `const string sql` in `GetEmployeeById`. Replace with:

```csharp
            const string sql = @"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate,
                       AgencyId, SalaryGrade, Designation, FundSource, PayrollCycle,
                       TinNumber, SssNumber, GsisNumber, PagIbigNumber, PhilHealthNumber
                FROM Employees
                WHERE EmployeeId = @EmployeeId";
```

- [ ] **Step 3: Update AddEmployee INSERT**

Find the INSERT SQL in `AddEmployee`. Replace with:

```csharp
            const string sql = @"
                INSERT INTO Employees
                (EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive,
                 SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate,
                 AgencyId, SalaryGrade, Designation, FundSource, PayrollCycle,
                 TinNumber, SssNumber, GsisNumber, PagIbigNumber, PhilHealthNumber)
                VALUES
                (@EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive,
                 @SourceTeacherId, @SourceUserId, @ProfileImage, @BiometricTemplate,
                 @AgencyId, @SalaryGrade, @Designation, @FundSource, @PayrollCycle,
                 @TinNumber, @SssNumber, @GsisNumber, @PagIbigNumber, @PhilHealthNumber);";
```

- [ ] **Step 4: Update UpdateEmployee SET**

Find the UPDATE SQL in `UpdateEmployee`. Replace with:

```csharp
            const string sql = @"
                UPDATE Employees
                SET EmployeeCode = @EmployeeCode,
                    FullName = @FullName,
                    Email = @Email,
                    Phone = @Phone,
                    Position = @Position,
                    Department = @Department,
                    HourlyRate = @HourlyRate,
                    HireDate = @HireDate,
                    IsActive = @IsActive,
                    SourceTeacherId = @SourceTeacherId,
                    SourceUserId = @SourceUserId,
                    ProfileImage = @ProfileImage,
                    BiometricTemplate = @BiometricTemplate,
                    AgencyId = @AgencyId,
                    SalaryGrade = @SalaryGrade,
                    Designation = @Designation,
                    FundSource = @FundSource,
                    PayrollCycle = @PayrollCycle,
                    TinNumber = @TinNumber,
                    SssNumber = @SssNumber,
                    GsisNumber = @GsisNumber,
                    PagIbigNumber = @PagIbigNumber,
                    PhilHealthNumber = @PhilHealthNumber
                WHERE EmployeeId = @EmployeeId";
```

- [ ] **Step 5: Update AddEmployeeParameters**

Find the `AddEmployeeParameters` private method. After `command.Parameters.AddWithValue("@BiometricTemplate", ...)`, add:

```csharp
            command.Parameters.AddWithValue("@AgencyId", employee.AgencyId ?? string.Empty);
            command.Parameters.AddWithValue("@SalaryGrade", employee.SalaryGrade ?? string.Empty);
            command.Parameters.AddWithValue("@Designation", employee.Designation ?? string.Empty);
            command.Parameters.AddWithValue("@FundSource", employee.FundSource ?? string.Empty);
            command.Parameters.AddWithValue("@PayrollCycle", string.IsNullOrWhiteSpace(employee.PayrollCycle) ? "Monthly" : employee.PayrollCycle);
            command.Parameters.AddWithValue("@TinNumber", employee.TinNumber ?? string.Empty);
            command.Parameters.AddWithValue("@SssNumber", employee.SssNumber ?? string.Empty);
            command.Parameters.AddWithValue("@GsisNumber", employee.GsisNumber ?? string.Empty);
            command.Parameters.AddWithValue("@PagIbigNumber", employee.PagIbigNumber ?? string.Empty);
            command.Parameters.AddWithValue("@PhilHealthNumber", employee.PhilHealthNumber ?? string.Empty);
```

- [ ] **Step 6: Update MapEmployee**

Find the `MapEmployee` private static method (returns a new Employee). After `BiometricTemplate = ...`, add:

```csharp
                AgencyId = Convert.ToString(reader["AgencyId"]) ?? string.Empty,
                SalaryGrade = Convert.ToString(reader["SalaryGrade"]) ?? string.Empty,
                Designation = Convert.ToString(reader["Designation"]) ?? string.Empty,
                FundSource = Convert.ToString(reader["FundSource"]) ?? string.Empty,
                PayrollCycle = Convert.ToString(reader["PayrollCycle"]) ?? "Monthly",
                TinNumber = Convert.ToString(reader["TinNumber"]) ?? string.Empty,
                SssNumber = Convert.ToString(reader["SssNumber"]) ?? string.Empty,
                GsisNumber = Convert.ToString(reader["GsisNumber"]) ?? string.Empty,
                PagIbigNumber = Convert.ToString(reader["PagIbigNumber"]) ?? string.Empty,
                PhilHealthNumber = Convert.ToString(reader["PhilHealthNumber"]) ?? string.Empty,
```

- [ ] **Step 7: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add AttendancePayrollSystem/DataAccess/EmployeeRepository.cs
git commit -m "feat: extend EmployeeRepository queries with govt ID and payroll info columns"
```

---

## Task 6: Extend AppBrandingRepository

**Files:**
- Modify: `AttendancePayrollSystem/DataAccess/AppBrandingRepository.cs`

- [ ] **Step 1: Update GetBrandingViaDatabase SELECT**

Find the `const string sql` in `GetBrandingViaDatabase`. Replace:

```csharp
            const string sql = @"
                SELECT BrandingSettingsId, LogoImage, CertifyingOfficerName, CertifyingOfficerTitle
                FROM AppBrandingSettings
                WHERE BrandingSettingsId = @BrandingSettingsId
                LIMIT 1";
```

- [ ] **Step 2: Update the mapping in GetBrandingViaDatabase**

Find the `return new AppBranding { ... }` in `GetBrandingViaDatabase`. Replace with:

```csharp
            return new AppBranding
            {
                BrandingSettingsId = Convert.ToInt32(reader["BrandingSettingsId"]),
                LogoImage = reader["LogoImage"] is DBNull ? null : (byte[])reader["LogoImage"],
                CertifyingOfficerName = Convert.ToString(reader["CertifyingOfficerName"]) ?? string.Empty,
                CertifyingOfficerTitle = Convert.ToString(reader["CertifyingOfficerTitle"]) ?? string.Empty
            };
```

- [ ] **Step 3: Add UpdateCertifyingOfficer public method**

After the `UpdateLogoImage` method, add:

```csharp
        public void UpdateCertifyingOfficer(string name, string title)
        {
            using var connection = DatabaseHelper.GetConnection();
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, CertifyingOfficerName, CertifyingOfficerTitle)
                    VALUES (@BrandingSettingsId, @Name, @Title)
                    ON CONFLICT(BrandingSettingsId) DO UPDATE SET
                        CertifyingOfficerName = excluded.CertifyingOfficerName,
                        CertifyingOfficerTitle = excluded.CertifyingOfficerTitle"
                : @"
                    INSERT INTO AppBrandingSettings (BrandingSettingsId, CertifyingOfficerName, CertifyingOfficerTitle)
                    VALUES (@BrandingSettingsId, @Name, @Title)
                    ON DUPLICATE KEY UPDATE
                        CertifyingOfficerName = VALUES(CertifyingOfficerName),
                        CertifyingOfficerTitle = VALUES(CertifyingOfficerTitle)";

            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BrandingSettingsId", AppBranding.DefaultBrandingSettingsId);
            command.Parameters.AddWithValue("@Name", name.Trim());
            command.Parameters.AddWithValue("@Title", title.Trim());
            connection.Open();
            command.ExecuteNonQuery();
        }
```

- [ ] **Step 4: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/DataAccess/AppBrandingRepository.cs
git commit -m "feat: extend AppBrandingRepository with certifying officer read/write"
```

---

## Task 7: Write PayslipDocument Tests (TDD)

**Files:**
- Create: `AttendancePayrollSystem.Tests/PayslipDocumentTests.cs`

WPF FlowDocument requires STA thread. Wrap each test body in an STA Thread.

- [ ] **Step 1: Write failing tests**

Create `AttendancePayrollSystem.Tests/PayslipDocumentTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using Xunit;

namespace AttendancePayrollSystem.Tests
{
    public class PayslipDocumentTests
    {
        private static Employee MakeEmployee() => new()
        {
            EmployeeId = 1,
            EmployeeCode = "EMP001",
            FullName = "Juan dela Cruz",
            Position = "Associate Professor III",
            Department = "College of Information and Digital Sciences",
            HireDate = new DateTime(2023, 1, 1),
            AgencyId = "2023-00001",
            SalaryGrade = "21-1",
            Designation = "Associate Professor III",
            FundSource = "3434",
            PayrollCycle = "Monthly",
            TinNumber = "111",
            SssNumber = "222",
            GsisNumber = "333",
            PagIbigNumber = "444",
            PhilHealthNumber = "555"
        };

        private static Payroll MakePayroll() => new()
        {
            PayrollId = 1,
            PayPeriodStart = new DateTime(2026, 6, 1),
            PayPeriodEnd = new DateTime(2026, 6, 30),
            GrossPay = 73303m,
            NetPay = 24253.83m,
            Deductions = 51049.17m,
            Status = "Approved"
        };

        private static List<PayrollLineItem> MakeLineItems() =>
        [
            new() { Label = "Salary", Amount = 73303m, ItemType = PayrollLineItemType.Earning, SortOrder = 0 },
            new() { Label = "GSIS", Amount = 9634.44m, ItemType = PayrollLineItemType.Deduction, SortOrder = 1 },
            new() { Label = "GSIS Employer Share", Amount = 8796.36m, ItemType = PayrollLineItemType.EmployerContribution, SortOrder = 0 }
        ];

        private static T RunOnSta<T>(Func<T> func)
        {
            T result = default!;
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try { result = func(); }
                catch (Exception e) { ex = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (ex != null) throw ex;
            return result;
        }

        [Fact]
        public void Build_ReturnsNonNullFlowDocument()
        {
            var doc = RunOnSta(() =>
                PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Test Officer", "HRMO III"));
            Assert.NotNull(doc);
        }

        [Fact]
        public void Build_DocumentContainsTwoColumnTable()
        {
            var doc = RunOnSta(() =>
                PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Test Officer", "HRMO III"));
            var table = doc.Blocks.OfType<Table>().FirstOrDefault();
            Assert.NotNull(table);
            Assert.Equal(2, table!.Columns.Count);
        }

        [Fact]
        public void Build_LeftCellContainsEmployeeName()
        {
            var doc = RunOnSta(() =>
                PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Test Officer", "HRMO III"));
            var table = doc.Blocks.OfType<Table>().First();
            var leftCell = table.RowGroups[0].Rows[0].Cells[0];
            var text = string.Concat(leftCell.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<Run>())
                .Select(r => r.Text));
            Assert.Contains("Juan dela Cruz", text);
        }

        [Fact]
        public void Build_RightCellContainsCertifyingOfficer()
        {
            var doc = RunOnSta(() =>
                PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Jane Smith", "HRMO III"));
            var table = doc.Blocks.OfType<Table>().First();
            var rightCell = table.RowGroups[0].Rows[0].Cells[1];

            // Traverse all text in the right cell (tables and paragraphs)
            var allText = GetAllText(rightCell.Blocks);
            Assert.Contains("Jane Smith", allText);
        }

        [Fact]
        public void Build_OmitsCertifyingBlockWhenNameIsEmpty()
        {
            var doc = RunOnSta(() =>
                PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "", "HRMO III"));
            var table = doc.Blocks.OfType<Table>().First();
            var rightCell = table.RowGroups[0].Rows[0].Cells[1];
            var allText = GetAllText(rightCell.Blocks);
            Assert.DoesNotContain("Certified By", allText);
        }

        private static string GetAllText(BlockCollection blocks)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var block in blocks)
            {
                if (block is Paragraph p)
                    sb.Append(string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)));
                else if (block is Table t)
                    foreach (var rg in t.RowGroups)
                        foreach (var row in rg.Rows)
                            foreach (var cell in row.Cells)
                                sb.Append(GetAllText(cell.Blocks));
                else if (block is Section s)
                    sb.Append(GetAllText(s.Blocks));
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (Build signature doesn't match yet)**

```bash
dotnet test "AttendancePayrollSystem.Tests/AttendancePayrollSystem.Tests.csproj" --filter "FullyQualifiedName~PayslipDocumentTests" --no-build
```

Expected: Build error — `PayslipDocument.Build` does not match the new signature. This confirms the tests are meaningful.

- [ ] **Step 3: Commit failing tests**

```bash
git add AttendancePayrollSystem.Tests/PayslipDocumentTests.cs
git commit -m "test: add PayslipDocument tests for two-column DSSC layout (currently failing)"
```

---

## Task 8: Rebuild PayslipDocument

**Files:**
- Modify: `AttendancePayrollSystem/Services/PayslipDocument.cs`

Replace the entire `Build` method and add private helpers. Keep `Print` and `SaveAsPdf` but update their signatures.

- [ ] **Step 1: Replace PayslipDocument.cs entirely**

Overwrite `AttendancePayrollSystem/Services/PayslipDocument.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public static class PayslipDocument
    {
        private const double LetterWidth = 816;
        private const double LetterHeight = 1056;
        private const double PagePadding = 36;
        private const string PdfPrinterName = "Microsoft Print to PDF";

        public static bool Print(Employee employee, Payroll payroll, List<PayrollLineItem> lineItems, string certOfficerName, string certOfficerTitle)
        {
            var document = Build(employee, payroll, lineItems, certOfficerName, certOfficerTitle);
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return false;

            document.PageWidth = printDialog.PrintableAreaWidth;
            document.PageHeight = printDialog.PrintableAreaHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = printDialog.PrintableAreaWidth;
            printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, PayslipJobName(employee, payroll));
            return true;
        }

        public static void SaveAsPdf(Employee employee, Payroll payroll, List<PayrollLineItem> lineItems, string certOfficerName, string certOfficerTitle)
        {
            using var server = new LocalPrintServer();
            var pdfQueue = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .FirstOrDefault(q =>
                    string.Equals(q.FullName, PdfPrinterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase));

            if (pdfQueue == null)
                throw new InvalidOperationException($"The \"{PdfPrinterName}\" printer was not found.");

            var document = Build(employee, payroll, lineItems, certOfficerName, certOfficerTitle);
            document.PageWidth = LetterWidth;
            document.PageHeight = LetterHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = LetterWidth;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(LetterWidth, LetterHeight);
            PrintQueue.CreateXpsDocumentWriter(pdfQueue).Write(paginator);
        }

        public static FlowDocument Build(
            Employee employee,
            Payroll payroll,
            List<PayrollLineItem> lineItems,
            string certOfficerName,
            string certOfficerTitle)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                PagePadding = new Thickness(PagePadding),
                ColumnWidth = double.PositiveInfinity
            };

            doc.Blocks.Add(BuildPayPeriodHeader(payroll));
            doc.Blocks.Add(BuildTwoColumnBody(employee, payroll, lineItems, certOfficerName, certOfficerTitle));

            return doc;
        }

        private static Block BuildPayPeriodHeader(Payroll payroll)
        {
            var section = new Section();
            section.Blocks.Add(new Paragraph(new Run("Payslip"))
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            section.Blocks.Add(new Paragraph(new Run($"Pay Period: {payroll.PayPeriodStart:MMM d} – {payroll.PayPeriodEnd:MMM d, yyyy}"))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
            return section;
        }

        private static Block BuildTwoColumnBody(
            Employee employee,
            Payroll payroll,
            List<PayrollLineItem> lineItems,
            string certOfficerName,
            string certOfficerTitle)
        {
            var outerTable = new Table { CellSpacing = 8 };
            outerTable.Columns.Add(new TableColumn { Width = new GridLength(2.5, GridUnitType.Star) });
            outerTable.Columns.Add(new TableColumn { Width = new GridLength(3.5, GridUnitType.Star) });

            var rowGroup = new TableRowGroup();
            var row = new TableRow();

            var leftCell = new TableCell { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 8, 0) };
            foreach (var block in BuildLeftCellBlocks(employee, payroll, lineItems))
                leftCell.Blocks.Add(block);

            var rightCell = new TableCell { Padding = new Thickness(8, 0, 0, 0) };
            foreach (var block in BuildRightCellBlocks(payroll, lineItems, certOfficerName, certOfficerTitle))
                rightCell.Blocks.Add(block);

            row.Cells.Add(leftCell);
            row.Cells.Add(rightCell);
            rowGroup.Rows.Add(row);
            outerTable.RowGroups.Add(rowGroup);
            return outerTable;
        }

        private static IEnumerable<Block> BuildLeftCellBlocks(Employee employee, Payroll payroll, List<PayrollLineItem> lineItems)
        {
            // Employee name
            yield return new Paragraph(new Run(employee.FullName))
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Identity fields table
            yield return BuildLabelValueTable(new[]
            {
                ("Agency ID", employee.AgencyId),
                ("Salary Grade", employee.SalaryGrade),
                ("Date Hired", employee.HireDate.ToString("yyyy-MM-dd")),
                ("Position", employee.Position),
                ("Department", employee.Department),
                ("Designation", employee.Designation),
                ("Payroll Cycle", employee.PayrollCycle),
                ("Fund Source", employee.FundSource),
                ("TIN", employee.TinNumber),
                ("SSS", employee.SssNumber),
                ("GSIS", employee.GsisNumber),
                ("Pag-Ibig", employee.PagIbigNumber),
                ("PhilHealth", employee.PhilHealthNumber)
            });

            // Gross income
            yield return new Paragraph(new Run($"Gross Income:  ₱ {payroll.GrossPay:N2}"))
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 4, 0, 0)
            };

            // Employer contributions
            var employerItems = lineItems.Where(li => li.ItemType == PayrollLineItemType.EmployerContribution).OrderBy(li => li.SortOrder).ToList();
            if (employerItems.Count > 0)
            {
                yield return new Paragraph(new Run("Employer Contribution"))
                {
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 6, 0, 2)
                };
                yield return BuildLabelValueTable(employerItems.Select(li => (li.Label, $"₱ {li.Amount:N2}")));
            }
        }

        private static IEnumerable<Block> BuildRightCellBlocks(
            Payroll payroll,
            List<PayrollLineItem> lineItems,
            string certOfficerName,
            string certOfficerTitle)
        {
            var displayItems = lineItems
                .Where(li => li.ItemType != PayrollLineItemType.EmployerContribution)
                .OrderBy(li => li.SortOrder)
                .ThenBy(li => li.Label)
                .ToList();

            var totalDeductions = lineItems.Where(li => li.ItemType == PayrollLineItemType.Deduction).Sum(li => li.Amount);

            // Items table (label | earning amount | deduction amount)
            var itemsTable = new Table { CellSpacing = 0 };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });

            var group = new TableRowGroup();

            // Header
            var headerRow = new TableRow { Background = Brushes.WhiteSmoke };
            headerRow.Cells.Add(MakeCell("ITEMS", bold: true));
            headerRow.Cells.Add(MakeCell("", right: true));
            headerRow.Cells.Add(MakeCell("", right: true));
            group.Rows.Add(headerRow);

            // Line items
            foreach (var item in displayItems)
            {
                var itemRow = new TableRow();
                itemRow.Cells.Add(MakeCell(item.Label));
                if (item.ItemType == PayrollLineItemType.Earning)
                {
                    itemRow.Cells.Add(MakeCell(item.Amount.ToString("N2"), right: true));
                    itemRow.Cells.Add(MakeCell("", right: true));
                }
                else
                {
                    itemRow.Cells.Add(MakeCell("", right: true));
                    itemRow.Cells.Add(MakeCell($"({item.Amount:N2})", right: true));
                }
                group.Rows.Add(itemRow);
            }

            // TOTAL row
            var totalRow = new TableRow { Background = Brushes.WhiteSmoke };
            totalRow.Cells.Add(MakeCell("TOTAL", bold: true));
            totalRow.Cells.Add(MakeCell("", right: true));
            totalRow.Cells.Add(MakeCell(totalDeductions.ToString("N2"), right: true, bold: true));
            group.Rows.Add(totalRow);

            // NET PAY row
            var netRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(240, 248, 255)) };
            netRow.Cells.Add(MakeCell("NET PAY", bold: true));
            netRow.Cells.Add(MakeCell("", right: true));
            netRow.Cells.Add(MakeCell(payroll.NetPay.ToString("N2"), right: true, bold: true));
            group.Rows.Add(netRow);

            itemsTable.RowGroups.Add(group);
            yield return itemsTable;

            // Certified By
            if (!string.IsNullOrWhiteSpace(certOfficerName))
            {
                yield return new Paragraph(new Run("Certified By:"))
                {
                    Margin = new Thickness(0, 16, 0, 2),
                    FontStyle = FontStyles.Italic
                };
                yield return new Paragraph(new Run(certOfficerName))
                {
                    FontWeight = FontWeights.Bold
                };
                if (!string.IsNullOrWhiteSpace(certOfficerTitle))
                {
                    yield return new Paragraph(new Run(certOfficerTitle));
                }
            }
        }

        private static TableCell MakeCell(string text, bool right = false, bool bold = false)
        {
            var para = new Paragraph(new Run(text));
            if (right) para.TextAlignment = TextAlignment.Right;
            if (bold) para.FontWeight = FontWeights.Bold;
            para.Margin = new Thickness(2);
            return new TableCell(para);
        }

        private static Block BuildLabelValueTable(IEnumerable<(string label, string value)> rows)
        {
            var table = new Table { CellSpacing = 0, FontSize = 10 };
            table.Columns.Add(new TableColumn { Width = new GridLength(90) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            foreach (var (label, value) in rows)
            {
                var row = new TableRow();
                var labelCell = new TableCell(new Paragraph(new Run(label)) { Margin = new Thickness(0, 1, 4, 1) })
                {
                    FontWeight = FontWeights.SemiBold
                };
                var valueCell = new TableCell(new Paragraph(new Run(value ?? string.Empty)) { Margin = new Thickness(0, 1, 0, 1) });
                row.Cells.Add(labelCell);
                row.Cells.Add(valueCell);
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static string PayslipJobName(Employee employee, Payroll payroll) =>
            $"Payslip {employee.EmployeeCode} {payroll.PayPeriodStart:yyyy-MM-dd}";
    }
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run PayslipDocument tests**

```bash
dotnet test "AttendancePayrollSystem.Tests/AttendancePayrollSystem.Tests.csproj" --filter "FullyQualifiedName~PayslipDocumentTests"
```

Expected: 4 tests pass.

- [ ] **Step 4: Run full test suite to check for regressions**

```bash
dotnet test "AttendancePayrollSystem.Tests/AttendancePayrollSystem.Tests.csproj"
```

Expected: All previously passing tests still pass. Any new failures indicate regressions to fix before proceeding.

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/Services/PayslipDocument.cs
git commit -m "feat: rebuild PayslipDocument with two-column DSSC layout and flexible line items"
```

---

## Task 9: Update Print/PDF Callers

Both `PayrollModal.xaml.cs` and `EmployeePayrollWindow.xaml.cs` call `PayslipDocument.Print` and `PayslipDocument.SaveAsPdf`. Both must be updated to load line items and certifying officer before calling.

**Files:**
- Modify: `AttendancePayrollSystem/PayrollModal.xaml.cs`
- Modify: `AttendancePayrollSystem/EmployeePayrollWindow.xaml.cs`

- [ ] **Step 1: Update PayrollModal — add repository field**

In `PayrollModal.xaml.cs`, add these two fields after `private readonly PayrollCalculator _payrollCalculator`:

```csharp
        private readonly PayrollLineItemRepository _lineItemRepository = new();
        private readonly AppBrandingRepository _brandingRepository = new();
```

- [ ] **Step 2: Update PayrollModal.PrintPayslip_Click**

Find `PrintPayslip_Click`. Replace the `PayslipDocument.Print(_employee, _selectedPayroll);` call with:

```csharp
                var lineItems = _lineItemRepository.GetByPayrollId(_selectedPayroll.PayrollId);
                var branding = _brandingRepository.GetBranding();
                PayslipDocument.Print(_employee, _selectedPayroll, lineItems, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle);
```

- [ ] **Step 3: Update PayrollModal.SavePdf_Click**

Find `SavePdf_Click`. Replace `PayslipDocument.SaveAsPdf(_employee, _selectedPayroll);` with:

```csharp
                var lineItems = _lineItemRepository.GetByPayrollId(_selectedPayroll.PayrollId);
                var branding = _brandingRepository.GetBranding();
                PayslipDocument.SaveAsPdf(_employee, _selectedPayroll, lineItems, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle);
```

- [ ] **Step 4: Update EmployeePayrollWindow — add repository fields**

In `EmployeePayrollWindow.xaml.cs`, add after `private readonly PayrollRepository _payrollRepository`:

```csharp
        private readonly PayrollLineItemRepository _lineItemRepository = new();
        private readonly AppBrandingRepository _brandingRepository = new();
```

- [ ] **Step 5: Update EmployeePayrollWindow.PrintPayslip_Click**

Find `PrintPayslip_Click`. Replace `PayslipDocument.Print(_employee, selected);` with:

```csharp
                var lineItems = _lineItemRepository.GetByPayrollId(selected.PayrollId);
                var branding = _brandingRepository.GetBranding();
                PayslipDocument.Print(_employee, selected, lineItems, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle);
```

- [ ] **Step 6: Update EmployeePayrollWindow.SavePdf_Click**

Find `SavePdf_Click`. Replace `PayslipDocument.SaveAsPdf(_employee, selected);` with:

```csharp
                var lineItems = _lineItemRepository.GetByPayrollId(selected.PayrollId);
                var branding = _brandingRepository.GetBranding();
                PayslipDocument.SaveAsPdf(_employee, selected, lineItems, branding.CertifyingOfficerName, branding.CertifyingOfficerTitle);
```

- [ ] **Step 7: Build + full test suite**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore && dotnet test "AttendancePayrollSystem.Tests/AttendancePayrollSystem.Tests.csproj"
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add AttendancePayrollSystem/PayrollModal.xaml.cs AttendancePayrollSystem/EmployeePayrollWindow.xaml.cs
git commit -m "feat: update PayrollModal and EmployeePayrollWindow to pass line items and certifying officer to payslip"
```

---

## Task 10: EmployeeModal — Add Government ID Fields

**Files:**
- Modify: `AttendancePayrollSystem/EmployeeModal.xaml`
- Modify: `AttendancePayrollSystem/EmployeeModal.xaml.cs`

- [ ] **Step 1: Add GroupBox to EmployeeModal.xaml**

Open `AttendancePayrollSystem/EmployeeModal.xaml`. Find the closing tag of the last GroupBox or StackPanel that wraps the existing fields (before the Save/Cancel buttons row). Add this GroupBox immediately before the buttons:

```xml
<GroupBox Header="Government IDs &amp; Payroll Info" Margin="0,8,0,0">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="120"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="120"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Grid.Column="0" Text="Agency ID" VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="AgencyIdTextBox" Grid.Row="0" Grid.Column="1" Margin="0,4,8,4"/>
        <TextBlock Grid.Row="0" Grid.Column="2" Text="Salary Grade" VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="SalaryGradeTextBox" Grid.Row="0" Grid.Column="3" Margin="0,4"/>

        <TextBlock Grid.Row="1" Grid.Column="0" Text="Designation" VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="DesignationTextBox" Grid.Row="1" Grid.Column="1" Margin="0,4,8,4"/>
        <TextBlock Grid.Row="1" Grid.Column="2" Text="Fund Source" VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="FundSourceTextBox" Grid.Row="1" Grid.Column="3" Margin="0,4"/>

        <TextBlock Grid.Row="2" Grid.Column="0" Text="Payroll Cycle" VerticalAlignment="Center" Margin="0,4"/>
        <ComboBox x:Name="PayrollCycleCombo" Grid.Row="2" Grid.Column="1" Margin="0,4,8,4" SelectedValuePath="Content">
            <ComboBoxItem Content="Monthly" IsSelected="True"/>
            <ComboBoxItem Content="Semi-Monthly"/>
            <ComboBoxItem Content="Daily"/>
        </ComboBox>
        <TextBlock Grid.Row="2" Grid.Column="2" Text="TIN" VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="TinNumberTextBox" Grid.Row="2" Grid.Column="3" Margin="0,4"/>

        <TextBlock Grid.Row="3" Grid.Column="0" Text="SSS No." VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="SssNumberTextBox" Grid.Row="3" Grid.Column="1" Margin="0,4,8,4"/>
        <TextBlock Grid.Row="3" Grid.Column="2" Text="GSIS No." VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="GsisNumberTextBox" Grid.Row="3" Grid.Column="3" Margin="0,4"/>

        <TextBlock Grid.Row="4" Grid.Column="0" Text="Pag-Ibig No." VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="PagIbigNumberTextBox" Grid.Row="4" Grid.Column="1" Margin="0,4,8,4"/>
        <TextBlock Grid.Row="4" Grid.Column="2" Text="PhilHealth No." VerticalAlignment="Center" Margin="0,4"/>
        <TextBox x:Name="PhilHealthNumberTextBox" Grid.Row="4" Grid.Column="3" Margin="0,4"/>
    </Grid>
</GroupBox>
```

- [ ] **Step 2: Populate new fields in LoadEmployeeData**

In `EmployeeModal.xaml.cs`, find `LoadEmployeeData`. After the `IsActiveCheckBox.IsChecked = _existingEmployee.IsActive;` line, add:

```csharp
            AgencyIdTextBox.Text = _existingEmployee.AgencyId;
            SalaryGradeTextBox.Text = _existingEmployee.SalaryGrade;
            DesignationTextBox.Text = _existingEmployee.Designation;
            FundSourceTextBox.Text = _existingEmployee.FundSource;
            foreach (ComboBoxItem item in PayrollCycleCombo.Items)
            {
                if (item.Content?.ToString() == _existingEmployee.PayrollCycle)
                {
                    PayrollCycleCombo.SelectedItem = item;
                    break;
                }
            }
            TinNumberTextBox.Text = _existingEmployee.TinNumber;
            SssNumberTextBox.Text = _existingEmployee.SssNumber;
            GsisNumberTextBox.Text = _existingEmployee.GsisNumber;
            PagIbigNumberTextBox.Text = _existingEmployee.PagIbigNumber;
            PhilHealthNumberTextBox.Text = _existingEmployee.PhilHealthNumber;
```

- [ ] **Step 3: Collect new fields in Save_Click**

In `Save_Click`, find `ResultEmployee = new Employee { ... }`. After all existing property assignments (before `DialogResult = true;`), add:

```csharp
            ResultEmployee.AgencyId = AgencyIdTextBox.Text.Trim();
            ResultEmployee.SalaryGrade = SalaryGradeTextBox.Text.Trim();
            ResultEmployee.Designation = DesignationTextBox.Text.Trim();
            ResultEmployee.FundSource = FundSourceTextBox.Text.Trim();
            ResultEmployee.PayrollCycle = (PayrollCycleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Monthly";
            ResultEmployee.TinNumber = TinNumberTextBox.Text.Trim();
            ResultEmployee.SssNumber = SssNumberTextBox.Text.Trim();
            ResultEmployee.GsisNumber = GsisNumberTextBox.Text.Trim();
            ResultEmployee.PagIbigNumber = PagIbigNumberTextBox.Text.Trim();
            ResultEmployee.PhilHealthNumber = PhilHealthNumberTextBox.Text.Trim();
```

- [ ] **Step 4: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/EmployeeModal.xaml AttendancePayrollSystem/EmployeeModal.xaml.cs
git commit -m "feat: add government ID and payroll info fields to EmployeeModal"
```

---

## Task 11: PayrollModal — Line Items DataGrid

**Files:**
- Modify: `AttendancePayrollSystem/PayrollModal.xaml`
- Modify: `AttendancePayrollSystem/PayrollModal.xaml.cs`

Line items are saved when payroll is created or updated. They are display-only for the payslip — the payroll calculation is unchanged.

- [ ] **Step 1: Add line items DataGrid to PayrollModal.xaml**

Open `AttendancePayrollSystem/PayrollModal.xaml`. Find the section with `ManualDeductionAmountTextBox` and `ManualDeductionNoteTextBox`. Replace that entire block with:

```xml
<!-- Payslip Line Items -->
<GroupBox Header="Payslip Line Items" Margin="0,8,0,0" x:Name="LineItemsSection">
    <StackPanel>
        <TextBlock Text="Items shown on the printed payslip (Earning = positive, Deduction = in parentheses, Employer Contribution = left column)."
                   FontSize="10" Foreground="Gray" Margin="0,0,0,4" TextWrapping="Wrap"/>
        <DataGrid x:Name="LineItemsGrid"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  CanUserDeleteRows="False"
                  MinHeight="80" MaxHeight="200"
                  Margin="0,0,0,4">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Label" Binding="{Binding Label, UpdateSourceTrigger=PropertyChanged}" Width="2*"/>
                <DataGridTextColumn Header="Amount" Binding="{Binding Amount, UpdateSourceTrigger=PropertyChanged, StringFormat=N2}" Width="*"/>
                <DataGridComboBoxColumn Header="Type"
                                        SelectedItemBinding="{Binding ItemType, UpdateSourceTrigger=PropertyChanged}"
                                        Width="*">
                    <DataGridComboBoxColumn.ElementStyle>
                        <Style TargetType="ComboBox">
                            <Setter Property="ItemsSource" Value="{Binding Source={StaticResource LineItemTypeValues}}"/>
                        </Style>
                    </DataGridComboBoxColumn.ElementStyle>
                    <DataGridComboBoxColumn.EditingElementStyle>
                        <Style TargetType="ComboBox">
                            <Setter Property="ItemsSource" Value="{Binding Source={StaticResource LineItemTypeValues}}"/>
                        </Style>
                    </DataGridComboBoxColumn.EditingElementStyle>
                </DataGridComboBoxColumn>
            </DataGrid.Columns>
        </DataGrid>
        <StackPanel Orientation="Horizontal">
            <Button Content="Add Row" Click="AddLineItem_Click" Margin="0,0,4,0" Padding="8,4"/>
            <Button Content="Remove Selected" Click="RemoveLineItem_Click" Padding="8,4"/>
        </StackPanel>
    </StackPanel>
</GroupBox>
```

Also add this resource to the Window's Resources section (add `<Window.Resources>` if it doesn't exist, or add inside existing one):

```xml
<Window.Resources>
    <ObjectDataProvider x:Key="LineItemTypeValues"
                        MethodName="GetValues"
                        ObjectType="{x:Type sys:Enum}">
        <ObjectDataProvider.MethodParameters>
            <x:Type TypeName="models:PayrollLineItemType"/>
        </ObjectDataProvider.MethodParameters>
    </ObjectDataProvider>
</Window.Resources>
```

Add these namespace declarations to the `<Window>` tag (after existing `xmlns:` declarations):

```xml
xmlns:sys="clr-namespace:System;assembly=mscorlib"
xmlns:models="clr-namespace:AttendancePayrollSystem.Models"
```

- [ ] **Step 2: Add repository field and line items collection to PayrollModal.xaml.cs**

In `PayrollModal.xaml.cs`, add after the existing repository fields:

```csharp
        private readonly PayrollLineItemRepository _lineItemRepository = new();
        private readonly ObservableCollection<PayrollLineItem> _lineItems = new();
```

In the constructor, after `DataContext = _viewModel;`, add:

```csharp
            LineItemsGrid.ItemsSource = _lineItems;
```

- [ ] **Step 3: Add AddLineItem_Click and RemoveLineItem_Click handlers**

Add these methods to `PayrollModal.xaml.cs`:

```csharp
        private void AddLineItem_Click(object sender, RoutedEventArgs e)
        {
            _lineItems.Add(new PayrollLineItem
            {
                Label = "New Item",
                Amount = 0m,
                ItemType = PayrollLineItemType.Deduction,
                SortOrder = _lineItems.Count
            });
        }

        private void RemoveLineItem_Click(object sender, RoutedEventArgs e)
        {
            if (LineItemsGrid.SelectedItem is PayrollLineItem selected)
            {
                _lineItems.Remove(selected);
            }
        }
```

- [ ] **Step 4: Save line items after payroll is saved in CalculatePayroll_Click**

In `CalculatePayroll_Click`, after the block that calls `_payrollRepository.AddPayroll(payroll)` or `_payrollRepository.UpdatePayroll(payroll)`, add:

```csharp
                _lineItemRepository.SaveLineItems(payroll.PayrollId, _lineItems.ToList());
```

The full try block should become:

```csharp
                // ... existing calculation code ...
                var action = "created";
                if (existingPayroll != null)
                {
                    payroll.PayrollId = existingPayroll.PayrollId;
                    payroll.Status = existingPayroll.Status;
                    _payrollRepository.UpdatePayroll(payroll);
                    action = "updated";
                }
                else
                {
                    _payrollRepository.AddPayroll(payroll);
                }
                _lineItemRepository.SaveLineItems(payroll.PayrollId, _lineItems.ToList());
                LoadPayrolls();
                // ... existing message box ...
```

- [ ] **Step 5: Load line items when a payroll record is selected**

Find `PayrollDataGrid_SelectionChanged`. After `_selectedPayroll = PayrollDataGrid.SelectedItem as Payroll;`, add:

```csharp
            _lineItems.Clear();
            if (_selectedPayroll != null)
            {
                foreach (var item in _lineItemRepository.GetByPayrollId(_selectedPayroll.PayrollId))
                    _lineItems.Add(item);
            }
```

- [ ] **Step 6: Build to verify no compile errors**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add AttendancePayrollSystem/PayrollModal.xaml AttendancePayrollSystem/PayrollModal.xaml.cs
git commit -m "feat: add line items DataGrid to PayrollModal for flexible payslip items"
```

---

## Task 12: SettingsWindow — Certifying Officer

**Files:**
- Create: `AttendancePayrollSystem/SettingsWindow.xaml`
- Create: `AttendancePayrollSystem/SettingsWindow.xaml.cs`
- Modify: Admin menu XAML (find the window that has the main admin navigation — look for `AdminWindow.xaml` or equivalent)

- [ ] **Step 1: Create SettingsWindow.xaml**

Create `AttendancePayrollSystem/SettingsWindow.xaml`:

```xml
<Window x:Class="AttendancePayrollSystem.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Settings" Width="420" Height="220"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="20">
        <TextBlock Text="Certifying Officer" FontSize="14" FontWeight="Bold" Margin="0,0,0,12"/>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="110"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Grid.Column="0" Text="Name" VerticalAlignment="Center" Margin="0,4"/>
            <TextBox x:Name="OfficerNameTextBox" Grid.Row="0" Grid.Column="1" Margin="0,4"/>

            <TextBlock Grid.Row="1" Grid.Column="0" Text="Title" VerticalAlignment="Center" Margin="0,4"/>
            <TextBox x:Name="OfficerTitleTextBox" Grid.Row="1" Grid.Column="1" Margin="0,4"/>
        </Grid>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Save" Width="80" Click="Save_Click" Margin="0,0,8,0"/>
            <Button Content="Cancel" Width="80" Click="Cancel_Click"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Create SettingsWindow.xaml.cs**

Create `AttendancePayrollSystem/SettingsWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using AttendancePayrollSystem.DataAccess;

namespace AttendancePayrollSystem
{
    public partial class SettingsWindow : Window
    {
        private readonly AppBrandingRepository _brandingRepository = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                var branding = _brandingRepository.GetBranding();
                OfficerNameTextBox.Text = branding.CertifyingOfficerName;
                OfficerTitleTextBox.Text = branding.CertifyingOfficerTitle;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load settings.\n{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _brandingRepository.UpdateCertifyingOfficer(
                    OfficerNameTextBox.Text.Trim(),
                    OfficerTitleTextBox.Text.Trim());
                MessageBox.Show("Settings saved.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings.\n{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

- [ ] **Step 3: Add Settings menu item to the admin window**

First, find the admin window file:

```bash
grep -rl "AdminWindow\|MainWindow\|DashboardWindow" "AttendancePayrollSystem/" --include="*.xaml" | head -5
```

Open the file that contains the main admin navigation menu. Add a Settings button or menu item. For example, if there is a menu bar with `MenuItem` elements:

```xml
<MenuItem Header="Settings" Click="Settings_Click"/>
```

Or if there is a toolbar with `Button` elements:

```xml
<Button Content="Settings" Click="Settings_Click" Margin="4,0"/>
```

In the corresponding `.xaml.cs` file, add the handler:

```csharp
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.ShowDialog();
        }
```

- [ ] **Step 4: Build + full test suite**

```bash
dotnet build "AttendancePayrollSystem/AttendancePayrollSystem.csproj" --no-restore && dotnet test "AttendancePayrollSystem.Tests/AttendancePayrollSystem.Tests.csproj"
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add AttendancePayrollSystem/SettingsWindow.xaml AttendancePayrollSystem/SettingsWindow.xaml.cs
git commit -m "feat: add SettingsWindow for certifying officer name and title"
```

- [ ] **Step 6: Commit admin menu change**

```bash
git add <admin-window-xaml-file> <admin-window-cs-file>
git commit -m "feat: add Settings menu item to admin navigation"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All 7 design spec sections covered: data model (Tasks 1-2), DB (Task 3), repos (Tasks 4-6), UI (Tasks 10-12), PayslipDocument (Tasks 7-9).
- [x] **No placeholders:** All steps contain complete code. Step 3 of Task 12 uses a grep to find the admin window first — correct because the exact file name is unknown without running it.
- [x] **Type consistency:** `PayrollLineItemType` used consistently. `PayrollLineItem` properties (Id, PayrollId, Label, Amount, ItemType, SortOrder) match in model, repo SQL, and Build() call sites. `AppBranding.CertifyingOfficerName/Title` match in model, repo, and SettingsWindow. `Employee` new properties match in model, repo, and EmployeeModal.
- [x] **Certifying officer omission:** Build() skips the "Certified By" block when name is empty — tested in Task 7.
- [x] **EmployerContribution filtering:** BuildRightCellBlocks filters out EmployerContribution items; BuildLeftCellBlocks shows them — consistent throughout.
