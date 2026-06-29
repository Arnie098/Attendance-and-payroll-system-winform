using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AttendancePayrollSystem.Models;

namespace AttendancePayrollSystem.Services
{
    public static class DtrDocument
    {
        private const double LetterWidth = 816;
        private const double LetterHeight = 1056;
        private const double PagePadding = 48;
        private const string PdfPrinterName = "Microsoft Print to PDF";

        public static FlowDocument Build(
            Employee employee,
            List<Attendance> attendances,
            DateTime periodStart,
            DateTime periodEnd,
            string agencyName,
            string certOfficerName,
            string certOfficerTitle)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                PagePadding = new Thickness(PagePadding),
                ColumnWidth = double.PositiveInfinity
            };

            doc.Blocks.Add(BuildHeader(agencyName, periodStart, periodEnd));
            doc.Blocks.Add(BuildEmployeeInfo(employee));
            doc.Blocks.Add(BuildDailyTable(employee, attendances, periodStart, periodEnd));
            doc.Blocks.Add(BuildSummaryAndCertification(employee, attendances, certOfficerName, certOfficerTitle));

            return doc;
        }

        public static bool Print(
            Employee employee,
            List<Attendance> attendances,
            DateTime periodStart,
            DateTime periodEnd,
            string agencyName,
            string certOfficerName,
            string certOfficerTitle)
        {
            var document = Build(employee, attendances, periodStart, periodEnd, agencyName, certOfficerName, certOfficerTitle);
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
                return false;

            document.PageWidth = printDialog.PrintableAreaWidth;
            document.PageHeight = printDialog.PrintableAreaHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = printDialog.PrintableAreaWidth;
            printDialog.PrintDocument(
                ((IDocumentPaginatorSource)document).DocumentPaginator,
                $"DTR {employee.EmployeeCode} {periodStart:yyyy-MM}");
            return true;
        }

        public static void SaveAsPdf(
            Employee employee,
            List<Attendance> attendances,
            DateTime periodStart,
            DateTime periodEnd,
            string agencyName,
            string certOfficerName,
            string certOfficerTitle)
        {
            using var server = new LocalPrintServer();
            var pdfQueue = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .FirstOrDefault(q =>
                    string.Equals(q.FullName, PdfPrinterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase));

            if (pdfQueue == null)
                throw new InvalidOperationException($"The \"{PdfPrinterName}\" printer was not found.");

            var document = Build(employee, attendances, periodStart, periodEnd, agencyName, certOfficerName, certOfficerTitle);
            document.PageWidth = LetterWidth;
            document.PageHeight = LetterHeight;
            document.PagePadding = new Thickness(PagePadding);
            document.ColumnWidth = LetterWidth - PagePadding * 2;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(LetterWidth, LetterHeight);
            PrintQueue.CreateXpsDocumentWriter(pdfQueue).Write(paginator);
        }

        private static Block BuildHeader(string agencyName, DateTime periodStart, DateTime periodEnd)
        {
            var section = new Section();

            if (!string.IsNullOrWhiteSpace(agencyName))
            {
                section.Blocks.Add(new Paragraph(new Run(agencyName.ToUpper()))
                {
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            section.Blocks.Add(new Paragraph(new Run("DAILY TIME RECORD"))
            {
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var periodLabel = periodStart.Month == periodEnd.Month && periodStart.Year == periodEnd.Year
                ? periodStart.ToString("MMMM yyyy")
                : $"{periodStart:MMM d} – {periodEnd:MMM d, yyyy}";

            section.Blocks.Add(new Paragraph(new Run($"For the Month of {periodLabel}"))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 10)
            });

            return section;
        }

        private static Block BuildEmployeeInfo(Employee employee)
        {
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();

            group.Rows.Add(MakeLabelValueRow("Name:", employee.FullName, "Employee Code:", employee.EmployeeCode));
            group.Rows.Add(MakeLabelValueRow("Position:", employee.Position, "Department:", employee.Department));
            group.Rows.Add(MakeLabelValueRow("Designation:", employee.Designation, "Agency ID:", employee.AgencyId));
            group.Rows.Add(MakeLabelValueRow("Salary Grade:", employee.SalaryGrade, "Fund Source:", employee.FundSource));

            table.RowGroups.Add(group);

            var section = new Section();
            section.Blocks.Add(table);
            section.Blocks.Add(new Paragraph { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 8) });
            return section;
        }

        private static TableRow MakeLabelValueRow(string label1, string val1, string label2, string val2)
        {
            var row = new TableRow();
            row.Cells.Add(MakeInfoCell($"{label1}  {val1}"));
            row.Cells.Add(MakeInfoCell($"{label2}  {val2}"));
            return row;
        }

        private static TableCell MakeInfoCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0, 1, 0, 1) });
        }

        private static Block BuildDailyTable(
            Employee employee,
            List<Attendance> attendances,
            DateTime periodStart,
            DateTime periodEnd)
        {
            var attendanceByDate = attendances.ToDictionary(a => a.AttendanceDate.Date);

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            table.Columns.Add(new TableColumn { Width = new GridLength(34) });   // Day
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) }); // AM In
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) }); // AM Out
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) }); // PM In
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) }); // PM Out
            table.Columns.Add(new TableColumn { Width = new GridLength(52) });  // Undertime Hrs
            table.Columns.Add(new TableColumn { Width = new GridLength(52) });  // Undertime Min

            var group = new TableRowGroup();

            // Header row
            var hdr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(30, 58, 95)) };
            hdr.Cells.Add(HeaderCell("Day"));
            hdr.Cells.Add(HeaderCell("AM\nArrival"));
            hdr.Cells.Add(HeaderCell("AM\nDeparture"));
            hdr.Cells.Add(HeaderCell("PM\nArrival"));
            hdr.Cells.Add(HeaderCell("PM\nDeparture"));
            hdr.Cells.Add(HeaderCell("Under-\ntime\nHrs"));
            hdr.Cells.Add(HeaderCell("Under-\ntime\nMin"));
            group.Rows.Add(hdr);

            // One row per calendar day in period
            var day = new DateTime(periodStart.Year, periodStart.Month, 1);
            var lastDay = new DateTime(periodEnd.Year, periodEnd.Month, DateTime.DaysInMonth(periodEnd.Year, periodEnd.Month));
            bool alternate = false;

            while (day <= lastDay)
            {
                bool isWeekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                bool outOfRange = day < periodStart || day > periodEnd;
                attendanceByDate.TryGetValue(day.Date, out var att);

                var rowBg = isWeekend
                    ? new SolidColorBrush(Color.FromRgb(240, 240, 240))
                    : outOfRange
                        ? new SolidColorBrush(Color.FromRgb(250, 250, 250))
                        : alternate
                            ? new SolidColorBrush(Color.FromRgb(248, 252, 255))
                            : Brushes.White;

                var dataRow = new TableRow { Background = rowBg };

                // Day number + day-of-week abbreviation
                var dayLabel = $"{day.Day}\n{day.ToString("ddd")}";
                var dayCell = new TableCell(new Paragraph(new Run(dayLabel))
                {
                    Margin = new Thickness(2, 1, 2, 1),
                    TextAlignment = TextAlignment.Center,
                    FontWeight = isWeekend ? FontWeights.Bold : FontWeights.Normal
                })
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0, 0, 1, 1)
                };
                if (isWeekend)
                    dayCell.Foreground = new SolidColorBrush(Color.FromRgb(120, 80, 0));
                dataRow.Cells.Add(dayCell);

                if (isWeekend || outOfRange || att == null)
                {
                    dataRow.Cells.Add(DataCell(isWeekend ? "—" : string.Empty));
                    dataRow.Cells.Add(DataCell(isWeekend ? "—" : string.Empty));
                    dataRow.Cells.Add(DataCell(isWeekend ? "—" : string.Empty));
                    dataRow.Cells.Add(DataCell(isWeekend ? "—" : string.Empty));
                    dataRow.Cells.Add(DataCell(string.Empty));
                    dataRow.Cells.Add(DataCell(string.Empty));
                }
                else
                {
                    dataRow.Cells.Add(TimeCell(att.TimeInAM));
                    dataRow.Cells.Add(TimeCell(att.TimeOutAM));
                    dataRow.Cells.Add(TimeCell(att.TimeInPM));
                    dataRow.Cells.Add(TimeCell(att.TimeOutPM));

                    int tardinessMin = att.TardinessMinutes;
                    if (tardinessMin > 0)
                    {
                        dataRow.Cells.Add(DataCell((tardinessMin / 60).ToString(), center: true, foreground: Brushes.DarkRed));
                        dataRow.Cells.Add(DataCell((tardinessMin % 60).ToString(), center: true, foreground: Brushes.DarkRed));
                    }
                    else
                    {
                        dataRow.Cells.Add(DataCell(string.Empty));
                        dataRow.Cells.Add(DataCell(string.Empty));
                    }
                }

                group.Rows.Add(dataRow);
                alternate = !alternate;
                day = day.AddDays(1);
            }

            table.RowGroups.Add(group);
            return table;
        }

        private static Block BuildSummaryAndCertification(
            Employee employee,
            List<Attendance> attendances,
            string certOfficerName,
            string certOfficerTitle)
        {
            var totalHours = attendances.Sum(a => a.TotalHours);
            var totalDays = attendances.Count(a => a.TotalHours > 0);
            var totalTardinessMin = attendances.Sum(a => a.TardinessMinutes);
            var lateDays = attendances.Count(a => a.TardinessMinutes > 0);

            var section = new Section { Margin = new Thickness(0, 10, 0, 0) };

            // Summary row
            var summaryTable = new Table { CellSpacing = 0 };
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            summaryTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var summaryGroup = new TableRowGroup();
            var summaryRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(240, 248, 255)) };
            summaryRow.Cells.Add(SummaryCell($"Days Present: {totalDays}"));
            summaryRow.Cells.Add(SummaryCell($"Total Hours: {totalHours:N2}"));
            summaryRow.Cells.Add(SummaryCell($"Days Late: {lateDays}"));
            summaryRow.Cells.Add(SummaryCell($"Total Tardiness: {totalTardinessMin} min"));
            summaryGroup.Rows.Add(summaryRow);
            summaryTable.RowGroups.Add(summaryGroup);
            section.Blocks.Add(summaryTable);

            // Certification text
            section.Blocks.Add(new Paragraph(
                new Run("I certify on my honor that the above is a true and correct report of the hours of work performed, record of which was made daily at the time of arrival at and departure from office."))
            {
                Margin = new Thickness(0, 14, 0, 0),
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.DimGray
            });

            // Signature lines
            var sigTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 18, 0, 0) };
            sigTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            sigTable.Columns.Add(new TableColumn { Width = new GridLength(40) });
            sigTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var sigGroup = new TableRowGroup();

            // Signature name rows
            var sigNamesRow = new TableRow();
            sigNamesRow.Cells.Add(SignatureLine(employee.FullName.ToUpper()));
            sigNamesRow.Cells.Add(new TableCell(new Paragraph()) );
            sigNamesRow.Cells.Add(SignatureLine(!string.IsNullOrWhiteSpace(certOfficerName) ? certOfficerName.ToUpper() : string.Empty));
            sigGroup.Rows.Add(sigNamesRow);

            // Role/title rows
            var sigTitleRow = new TableRow();
            sigTitleRow.Cells.Add(SignatureTitle("Employee Signature"));
            sigTitleRow.Cells.Add(new TableCell(new Paragraph()));
            sigTitleRow.Cells.Add(SignatureTitle(!string.IsNullOrWhiteSpace(certOfficerTitle) ? certOfficerTitle : "Authorized Approving Official"));
            sigGroup.Rows.Add(sigTitleRow);

            sigTable.RowGroups.Add(sigGroup);
            section.Blocks.Add(sigTable);

            return section;
        }

        private static TableCell HeaderCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 3, 2, 3),
                FontWeight = FontWeights.Bold
            })
            {
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 90, 130)),
                BorderThickness = new Thickness(0, 0, 1, 1)
            };
        }

        private static TableCell DataCell(string text, bool center = false, Brush? foreground = null)
        {
            var para = new Paragraph(new Run(text))
            {
                Margin = new Thickness(3, 1, 3, 1),
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Left
            };
            var cell = new TableCell(para)
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 1, 1)
            };
            if (foreground != null) cell.Foreground = foreground;
            return cell;
        }

        private static TableCell TimeCell(DateTime? time)
        {
            var text = time.HasValue ? time.Value.ToString("h:mm tt") : string.Empty;
            return DataCell(text, center: true);
        }

        private static TableCell SummaryCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text))
            {
                Margin = new Thickness(4, 3, 4, 3),
                FontWeight = FontWeights.SemiBold
            })
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 1, 1, 1)
            };
        }

        private static TableCell SignatureLine(string name)
        {
            var para = new Paragraph
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 2),
                TextAlignment = TextAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(name))
                para.Inlines.Add(new Run(name) { FontWeight = FontWeights.Bold });
            return new TableCell(para);
        }

        private static TableCell SignatureTitle(string title)
        {
            return new TableCell(new Paragraph(new Run(title))
            {
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.DimGray,
                FontSize = 9
            });
        }
    }
}
