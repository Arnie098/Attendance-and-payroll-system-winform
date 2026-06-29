using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var (columnCount, hasTable) = RunOnSta(() =>
            {
                var doc = PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Test Officer", "HRMO III");
                var table = doc.Blocks.OfType<Table>().FirstOrDefault();
                return (table?.Columns.Count ?? 0, table != null);
            });
            Assert.True(hasTable);
            Assert.Equal(2, columnCount);
        }

        [Fact]
        public void Build_LeftCellContainsEmployeeName()
        {
            var text = RunOnSta(() =>
            {
                var doc = PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Test Officer", "HRMO III");
                var table = doc.Blocks.OfType<Table>().First();
                var leftCell = table.RowGroups[0].Rows[0].Cells[0];
                return GetAllText(leftCell.Blocks);
            });
            Assert.Contains("Juan dela Cruz", text);
        }

        [Fact]
        public void Build_RightCellContainsCertifyingOfficer()
        {
            var text = RunOnSta(() =>
            {
                var doc = PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "Jane Smith", "HRMO III");
                var table = doc.Blocks.OfType<Table>().First();
                var rightCell = table.RowGroups[0].Rows[0].Cells[1];
                return GetAllText(rightCell.Blocks);
            });
            Assert.Contains("Jane Smith", text);
        }

        [Fact]
        public void Build_OmitsCertifyingBlockWhenNameIsEmpty()
        {
            var text = RunOnSta(() =>
            {
                var doc = PayslipDocument.Build(MakeEmployee(), MakePayroll(), MakeLineItems(), "", "HRMO III");
                var table = doc.Blocks.OfType<Table>().First();
                var rightCell = table.RowGroups[0].Rows[0].Cells[1];
                return GetAllText(rightCell.Blocks);
            });
            Assert.DoesNotContain("Certified By", text);
        }

        private static string GetAllText(BlockCollection blocks)
        {
            var sb = new StringBuilder();
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
