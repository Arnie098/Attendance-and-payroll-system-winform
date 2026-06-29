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
                throw new InvalidOperationException($"The \"{PdfPrinterName}\" printer was not found. Enable it under Windows Settings > Printers & scanners.");

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

            var leftCell = new TableCell
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 0, 8, 0)
            };
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
            yield return new Paragraph(new Run(employee.FullName))
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };

            yield return BuildLabelValueTable(new[]
            {
                ("Agency ID",     employee.AgencyId),
                ("Salary Grade",  employee.SalaryGrade),
                ("Date Hired",    employee.HireDate.ToString("yyyy-MM-dd")),
                ("Position",      employee.Position),
                ("Department",    employee.Department),
                ("Designation",   employee.Designation),
                ("Payroll Cycle", employee.PayrollCycle),
                ("Fund Source",   employee.FundSource),
                ("TIN",           employee.TinNumber),
                ("SSS",           employee.SssNumber),
                ("GSIS",          employee.GsisNumber),
                ("Pag-Ibig",      employee.PagIbigNumber),
                ("PhilHealth",    employee.PhilHealthNumber)
            });

            yield return new Paragraph(new Run($"Gross Income:  ₱ {payroll.GrossPay:N2}"))
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 4, 0, 0)
            };

            var employerItems = lineItems
                .Where(li => li.ItemType == PayrollLineItemType.EmployerContribution)
                .OrderBy(li => li.SortOrder)
                .ToList();

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

            var totalDeductions = lineItems
                .Where(li => li.ItemType == PayrollLineItemType.Deduction)
                .Sum(li => li.Amount);

            var itemsTable = new Table { CellSpacing = 0 };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1.3, GridUnitType.Star) });

            var group = new TableRowGroup();

            var headerRow = new TableRow { Background = Brushes.WhiteSmoke };
            headerRow.Cells.Add(MakeCell("ITEMS", bold: true));
            headerRow.Cells.Add(MakeCell("", right: true));
            headerRow.Cells.Add(MakeCell("", right: true));
            group.Rows.Add(headerRow);

            foreach (var item in displayItems)
            {
                var itemRow = new TableRow();
                itemRow.Cells.Add(MakeCell(item.Label));
                if (item.ItemType == PayrollLineItemType.Earning)
                {
                    itemRow.Cells.Add(MakeCell(item.Amount.ToString("N2"), right: true));
                    itemRow.Cells.Add(MakeCell(string.Empty, right: true));
                }
                else
                {
                    itemRow.Cells.Add(MakeCell(string.Empty, right: true));
                    itemRow.Cells.Add(MakeCell($"({item.Amount:N2})", right: true));
                }
                group.Rows.Add(itemRow);
            }

            var totalRow = new TableRow { Background = Brushes.WhiteSmoke };
            totalRow.Cells.Add(MakeCell("TOTAL", bold: true));
            totalRow.Cells.Add(MakeCell(string.Empty, right: true));
            totalRow.Cells.Add(MakeCell(totalDeductions.ToString("N2"), right: true, bold: true));
            group.Rows.Add(totalRow);

            var netRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(240, 248, 255)) };
            netRow.Cells.Add(MakeCell("NET PAY", bold: true));
            netRow.Cells.Add(MakeCell(string.Empty, right: true));
            netRow.Cells.Add(MakeCell(payroll.NetPay.ToString("N2"), right: true, bold: true));
            group.Rows.Add(netRow);

            itemsTable.RowGroups.Add(group);
            yield return itemsTable;

            if (!string.IsNullOrWhiteSpace(certOfficerName))
            {
                yield return new Paragraph(new Run("Certified By:"))
                {
                    Margin = new Thickness(0, 16, 0, 2),
                    FontStyle = FontStyles.Italic
                };
                yield return new Paragraph(new Run(certOfficerName)) { FontWeight = FontWeights.Bold };
                if (!string.IsNullOrWhiteSpace(certOfficerTitle))
                    yield return new Paragraph(new Run(certOfficerTitle));
            }
        }

        private static TableCell MakeCell(string text, bool right = false, bool bold = false)
        {
            var para = new Paragraph(new Run(text)) { Margin = new Thickness(2) };
            if (right) para.TextAlignment = TextAlignment.Right;
            if (bold) para.FontWeight = FontWeights.Bold;
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
