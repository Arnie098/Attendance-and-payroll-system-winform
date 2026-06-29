using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class DtrPreviewWindow : Window
    {
        private readonly List<Attendance> _attendances;
        private readonly DateTime _periodStart;
        private readonly DateTime _periodEnd;
        private readonly AppBranding _branding;

        // For single-employee mode
        private readonly Employee _employee;

        private const double LetterWidth = 816;
        private const double LetterHeight = 1056;
        private const double PagePadding = 48;

        public DtrPreviewWindow(
            Employee employee,
            List<Attendance> attendances,
            DateTime periodStart,
            DateTime periodEnd,
            AppBranding branding)
        {
            InitializeComponent();
            _employee = employee;
            _attendances = attendances;
            _periodStart = periodStart;
            _periodEnd = periodEnd;
            _branding = branding;

            Title = $"DTR Preview — {employee.FullName} ({periodStart:MMM d} – {periodEnd:MMM d, yyyy})";
            LoadPreview();
        }

        private void LoadPreview()
        {
            var doc = DtrDocument.Build(
                _employee, _attendances, _periodStart, _periodEnd,
                _branding.CertifyingOfficerName,
                _branding.CertifyingOfficerName,
                _branding.CertifyingOfficerTitle);

            doc.PageWidth = LetterWidth;
            doc.PageHeight = LetterHeight;
            doc.PagePadding = new Thickness(PagePadding);
            doc.ColumnWidth = LetterWidth - PagePadding * 2;

            PreviewReader.Document = doc;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DtrDocument.Print(_employee, _attendances, _periodStart, _periodEnd,
                    _branding.CertifyingOfficerName,
                    _branding.CertifyingOfficerName,
                    _branding.CertifyingOfficerTitle);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to print.\n{ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrevEmployee_Click(object sender, RoutedEventArgs e) { }
        private void NextEmployee_Click(object sender, RoutedEventArgs e) { }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
