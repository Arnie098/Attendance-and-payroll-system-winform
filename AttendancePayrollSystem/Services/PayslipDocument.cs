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
    /// <summary>
    /// Builds and outputs the printable payslip document. Shared by the admin payroll
    /// modal and the employee "My Payroll Records" window so the payslip layout lives
    /// in a single place.
    /// </summary>
    public static class PayslipDocument
    {
        // Letter size at 96 DPI, used when saving to PDF (no printer dialog to size against).
        private const double LetterWidth = 816;   // 8.5in * 96
        private const double LetterHeight = 1056;  // 11in * 96
        private const double PagePadding = 36;

        private const string PdfPrinterName = "Microsoft Print to PDF";

        /// <summary>
        /// Shows the system print dialog and prints the payslip for the given record.
        /// Returns false if the user cancelled the dialog.
        /// </summary>
        public static bool Print(Employee employee, Payroll payroll)
        {
            var document = Build(employee, payroll);
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                return false;
            }

            document.PageWidth = printDialog.PrintableAreaWidth;
            document.PageHeight = printDialog.PrintableAreaHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = printDialog.PrintableAreaWidth;

            printDialog.PrintDocument(
                ((IDocumentPaginatorSource)document).DocumentPaginator,
                PayslipJobName(employee, payroll));
            return true;
        }

        /// <summary>
        /// Saves the payslip as a PDF using the built-in "Microsoft Print to PDF" printer.
        /// Windows prompts for the output file location. Throws if that printer is missing.
        /// </summary>
        public static void SaveAsPdf(Employee employee, Payroll payroll)
        {
            using var server = new LocalPrintServer();
            var pdfQueue = server.GetPrintQueues(new[]
                {
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                })
                .FirstOrDefault(q => string.Equals(q.FullName, PdfPrinterName, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(q.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase));

            if (pdfQueue == null)
            {
                throw new InvalidOperationException(
                    $"The \"{PdfPrinterName}\" printer was not found. " +
                    "Enable it under Windows Settings > Printers & scanners, or use Print and choose a PDF printer.");
            }

            var document = Build(employee, payroll);
            document.PageWidth = LetterWidth;
            document.PageHeight = LetterHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = LetterWidth;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(LetterWidth, LetterHeight);

            // Writing to the "Microsoft Print to PDF" queue makes Windows show its own
            // "Save Print Output As" dialog for choosing the destination .pdf file.
            var writer = PrintQueue.CreateXpsDocumentWriter(pdfQueue);
            writer.Write(paginator);
        }

        /// <summary>
        /// Builds the payslip as a <see cref="FlowDocument"/>.
        /// </summary>
        public static FlowDocument Build(Employee employee, Payroll p)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                PagePadding = new Thickness(PagePadding),
                ColumnWidth = double.PositiveInfinity
            };

            doc.Blocks.Add(new Paragraph(new Run("Payroll Payslip"))
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            doc.Blocks.Add(new Paragraph(new Run($"{employee.FullName} ({employee.EmployeeCode})"))
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            doc.Blocks.Add(new Paragraph(new Run($"{p.PayPeriodStart:yyyy-MM-dd} to {p.PayPeriodEnd:yyyy-MM-dd}"))
            {
                Margin = new Thickness(0, 0, 0, 12)
            });

            doc.Blocks.Add(BuildSection("Earnings", new[]
            {
                $"Regular Hours: {p.RegularHours:N2}",
                $"Overtime Hours: {p.OvertimeHours:N2}",
                $"Gross Pay: ₱{p.GrossPay:N2}"
            }));

            doc.Blocks.Add(BuildSection("Deductions", new[]
            {
                $"Late: {p.TotalTardinessMinutes} min (₱{p.TardinessDeduction:N2})",
                $"Absent: {p.AbsentDays} day(s) (₱{p.AbsenceDeduction:N2})",
                $"Missed Session: {p.MissedSessionHours:N2} hr(s) (₱{p.MissedSessionDeduction:N2})",
                $"Manual Deduction: ₱{p.ManualDeduction:N2}",
                $"Total Deductions: ₱{p.Deductions:N2}"
            }));

            doc.Blocks.Add(new Paragraph(new Run($"Net Pay: ₱{p.NetPay:N2}"))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 12, 0, 0)
            });

            if (!string.IsNullOrWhiteSpace(p.ManualDeductionNote))
            {
                doc.Blocks.Add(new Paragraph(new Run($"Note: {p.ManualDeductionNote}"))
                {
                    FontStyle = FontStyles.Italic
                });
            }

            return doc;
        }

        private static string PayslipJobName(Employee employee, Payroll payroll) =>
            $"Payslip {employee.EmployeeCode} {payroll.PayPeriodStart:yyyy-MM-dd}";

        private static Block BuildSection(string title, IEnumerable<string> lines)
        {
            var section = new Section();
            section.Blocks.Add(new Paragraph(new Run(title))
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 10, 0, 6)
            });

            foreach (var line in lines)
            {
                section.Blocks.Add(new Paragraph(new Run(line))
                {
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            return section;
        }
    }
}
