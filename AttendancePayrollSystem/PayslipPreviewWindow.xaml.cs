using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;

namespace AttendancePayrollSystem
{
    public partial class PayslipPreviewWindow : Window
    {
        private readonly Employee _employee;
        private readonly Payroll _payroll;
        private readonly List<PayrollLineItem> _lineItems;
        private readonly string _certOfficerName;
        private readonly string _certOfficerTitle;

        private const double LetterWidth = 816;
        private const double LetterHeight = 1056;
        private const double PagePadding = 48;

        public PayslipPreviewWindow(
            Employee employee,
            Payroll payroll,
            List<PayrollLineItem> lineItems,
            string certOfficerName,
            string certOfficerTitle)
        {
            InitializeComponent();
            _employee = employee;
            _payroll = payroll;
            _lineItems = lineItems;
            _certOfficerName = certOfficerName;
            _certOfficerTitle = certOfficerTitle;

            Title = $"Payslip Preview — {employee.FullName} ({payroll.PayPeriodStart:MMM d} – {payroll.PayPeriodEnd:MMM d, yyyy})";
            LoadPreview();
        }

        private FlowDocument BuildDocument()
        {
            var doc = PayslipDocument.Build(_employee, _payroll, _lineItems, _certOfficerName, _certOfficerTitle);
            doc.PageWidth = LetterWidth;
            doc.PageHeight = LetterHeight;
            doc.PagePadding = new Thickness(PagePadding);
            doc.ColumnWidth = LetterWidth - PagePadding * 2;
            return doc;
        }

        private void LoadPreview()
        {
            PreviewReader.Document = BuildDocument();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PayslipDocument.Print(_employee, _payroll, _lineItems, _certOfficerName, _certOfficerTitle);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to print.\n{ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PayslipDocument.SaveAsPdf(_employee, _payroll, _lineItems, _certOfficerName, _certOfficerTitle);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save as PDF.\n{ex.Message}", "Save as PDF Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
